using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WindowsDock;

internal sealed record SavedTurn(bool User, string Text, ChatFile[]? Files = null, AssistantContext? Context = null,
    string[]? OutputFiles = null);
internal sealed record SavedChat(Guid Id, string Persona, string Title, DateTimeOffset Updated, SavedTurn[] Turns,
    AssistantProvider? Provider = null, string? Model = null);
internal sealed record ChatFile(Guid Id, string Name, string? MediaType, string? Text);
internal sealed record PickedChatFile(ChatFile File, byte[] Bytes);
internal sealed record ChatMedia(string Name, string MediaType, byte[] Bytes)
{
    internal string DataUrl => $"data:{MediaType};base64,{Convert.ToBase64String(Bytes)}";
}

internal static class DockChats
{
    private const string Preference = "assistant-chat-folder.json";
    private const string AutoSavePreference = "assistant-save-named-files.json";
    private const string Transcript = "chat.json";
    private static readonly object LibraryLock = new();
    internal static string Root => DockData.Read<string>(Preference) is { } saved && Path.IsPathFullyQualified(saved)
        ? saved : DockData.FilePath("Chats");
    internal static bool AutoSaveFiles => DockData.Read<bool?>(AutoSavePreference) ?? true;
    internal static void SetAutoSaveFiles(bool value) => DockData.Save(AutoSavePreference, value);

    internal static SavedChat[] Load()
    {
        var root = Root;
        if (!Directory.Exists(root))
            return [];
        CheckLink(root);
        return Directory.EnumerateDirectories(root).Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
            .Select(ReadChat).OfType<SavedChat>().OrderByDescending(chat => chat.Updated).ToArray();
    }

    private static SavedChat? ReadChat(string directory)
    {
        try
        {
            CheckLink(directory);
            var path = Path.Combine(directory, Transcript);
            CheckLink(path);
            using var file = File.OpenRead(path);
            var chat = JsonSerializer.Deserialize<SavedChat>(file);
            return chat is { Turns: { Length: > 0 }, Title: not null, Persona: not null } &&
                chat.Id.ToString("N") == Path.GetFileName(directory) &&
                chat.Turns.All(turn => turn?.Text != null && (turn.Files == null ||
                    turn.Files.All(item => item?.Name != null && IsLeaf(item.Name)))) ? chat : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static void Save(SavedChat chat, IReadOnlyList<PickedChatFile>? attachments = null)
    {
        lock (LibraryLock)
        {
            var directory = ChatDirectory(Root, chat.Id);
            if (attachments is { Count: > 0 })
                SaveAttachments(directory, attachments);
            AtomicWrite(Path.Combine(directory, Transcript), stream => JsonSerializer.Serialize(stream, chat), replace: true);
        }
    }

    private static void SaveAttachments(string chatDirectory, IReadOnlyList<PickedChatFile> picked)
    {
        var directory = EnsureDirectory(Path.Combine(chatDirectory, "attachments"));
        foreach (var item in picked)
        {
            var path = Path.Combine(directory, AttachmentName(item.File));
            if (!File.Exists(path))
                AtomicWrite(path, stream => stream.Write(item.Bytes), replace: false);
            else
                CheckLink(path);
        }
    }

    internal static ChatMedia ReadMedia(Guid chat, ChatFile file)
    {
        var root = Root;
        var directory = Path.Combine(root, chat.ToString("N"));
        var attachments = Path.Combine(directory, "attachments");
        CheckLink(root);
        CheckLink(directory);
        CheckLink(attachments);
        var path = Path.Combine(attachments, AttachmentName(file));
        CheckLink(path);
        var bytes = ChatAttachments.ReadBounded(path);
        if (file.MediaType == null || ChatAttachments.MediaType(bytes) != file.MediaType)
            throw new IOException($"The stored attachment {file.Name} has changed.");
        return new(file.Name, file.MediaType, bytes);
    }

    internal static string SaveFile(Guid chat, string name, string text)
    {
        lock (LibraryLock)
            return SaveFileCore(chat, name, text);
    }

    private static string SaveFileCore(Guid chat, string name, string text)
    {
        if (!IsLeaf(name))
            throw new ArgumentException("Choose a file name without a folder path.");
        var directory = EnsureDirectory(Path.Combine(ChatDirectory(Root, chat), "files"));
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var index = 1; index <= 500; index++)
        {
            var path = Path.Combine(directory, index == 1 ? name : $"{stem}-{index}{extension}");
            try
            {
                AtomicWrite(path, stream => stream.Write(Encoding.UTF8.GetBytes(text)), replace: false);
                return path;
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { }
        }
        throw new IOException("Choose another file name.");
    }

    internal static void ChooseFolder(string selected)
    {
        lock (LibraryLock)
            ChooseFolderCore(selected);
    }

    private static void ChooseFolderCore(string selected)
    {
        if (!Path.IsPathFullyQualified(selected))
            throw new ArgumentException("Choose a full folder path.");
        var previous = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root));
        var next = Path.Combine(Path.GetFullPath(selected), "Windows Dock Chats");
        if (string.Equals(previous, next, StringComparison.OrdinalIgnoreCase))
            return;
        if (next.StartsWith(previous + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a folder outside the current chat library.");
        EnsureDirectory(next);
        foreach (var chat in Load())
        {
            var source = Path.Combine(previous, chat.Id.ToString("N"));
            var target = ChatDirectory(next, chat.Id);
            foreach (var kind in new[] { "attachments", "files" })
                CopyFolder(Path.Combine(source, kind), Path.Combine(target, kind));
            var present = ReadChat(target);
            if (present == null || present.Updated < chat.Updated)
                AtomicWrite(Path.Combine(target, Transcript), stream => JsonSerializer.Serialize(stream, chat), replace: true);
        }
        DockData.Save(Preference, next);
    }

    private static void CopyFolder(string source, string target)
    {
        if (!Directory.Exists(source))
            return;
        CheckLink(source);
        EnsureDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            CheckLink(file);
            var name = Path.GetFileName(file);
            if (!IsLeaf(name, 180))
                continue;
            var copy = Path.Combine(target, name);
            if (File.Exists(copy))
            {
                CheckLink(copy);
                if (SameFile(file, copy))
                    continue;
                if (Path.GetFileName(source) == "attachments")
                    throw new IOException($"Two different attachments already use the name {name}.");
                copy = Path.Combine(target, $"copy-{Guid.NewGuid():N}-{name[..Math.Min(name.Length, 120)]}");
            }
            AtomicWrite(copy, output =>
            {
                using var input = File.OpenRead(file);
                input.CopyTo(output);
            }, replace: false);
        }
    }

    private static bool SameFile(string first, string second)
    {
        using var left = File.OpenRead(first);
        using var right = File.OpenRead(second);
        return left.Length == right.Length &&
            System.Security.Cryptography.SHA256.HashData(left).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(right));
    }

    private static string AttachmentName(ChatFile file)
    {
        if (file.Id == Guid.Empty || !IsLeaf(file.Name))
            throw new IOException("The attachment name is invalid.");
        return $"{file.Id:N}-{file.Name}";
    }

    internal static bool IsLeaf(string name, int limit = 120)
    {
        if (name.Length is 0 || name.Length > limit || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith(' '))
            return false;
        var stem = name.Split('.')[0].ToUpperInvariant();
        return !Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$");
    }

    private static string ChatDirectory(string root, Guid id)
    {
        EnsureDirectory(root);
        return EnsureDirectory(Path.Combine(root, id.ToString("N")));
    }

    private static string EnsureDirectory(string path)
    {
        CheckLink(path);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CheckLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The chat library cannot follow a redirected file or subfolder.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
    }

    private static void AtomicWrite(string path, Action<FileStream> write, bool replace)
    {
        CheckLink(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(file);
            File.Move(temp, path, replace);
        }
        finally { File.Delete(temp); }
    }

    internal static string Title(IEnumerable<SavedTurn> turns)
    {
        var first = turns.FirstOrDefault(turn => turn.User && !string.IsNullOrWhiteSpace(turn.Text))?.Text ?? "";
        var line = Regex.Replace(first.Trim(), @"\s+", " ");
        return line.Length == 0 ? "New chat" : line.Length > 46 ? line[..46].TrimEnd() + "…" : line;
    }
}

internal static class ChatAttachments
{
    internal const int FileLimit = 6;
    internal const int TextLimit = 24_000;
    internal const int MediaLimit = 10 * 1024 * 1024;
    private const int SizeLimit = 4 * 1024 * 1024;

    internal static PickedChatFile Read(string path)
    {
        var name = Path.GetFileName(path);
        if (!DockChats.IsLeaf(name))
            throw new IOException("The file name is too long or contains unsupported characters.");
        var bytes = ReadBounded(path);
        var media = MediaType(bytes);
        if (media != null)
            return new(new(Guid.NewGuid(), name, media, null), bytes);
        var text = Path.GetExtension(name).Equals(".docx", StringComparison.OrdinalIgnoreCase)
            ? ReadWord(bytes) : ReadText(bytes);
        if (string.IsNullOrWhiteSpace(text))
            throw new IOException("Choose a text file, Word document, PDF, PNG, or JPEG.");
        return new(new(Guid.NewGuid(), name, null, text[..Math.Min(TextLimit, text.Length)]), bytes);
    }

    internal static byte[] ReadBounded(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is 0 or > SizeLimit)
            throw new IOException("Choose a file between 1 byte and 4 MB.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        return bytes;
    }

    internal static string? MediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
            return "image/jpeg";
        return bytes.StartsWith("%PDF-"u8) ? "application/pdf" : null;
    }

    private static string? ReadText(byte[] bytes)
    {
        var utf16 = bytes.AsSpan().StartsWith(new byte[] { 255, 254 }) || bytes.AsSpan().StartsWith(new byte[] { 254, 255 });
        if (!utf16 && bytes.AsSpan(0, Math.Min(bytes.Length, 4096)).Contains((byte)0))
            return null;
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string? ReadWord(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes));
        if (archive.GetEntry("word/document.xml") is not { Length: <= 8 * 1024 * 1024 } entry)
            return null;
        using var reader = XmlReader.Create(entry.Open(), new XmlReaderSettings { CloseInput = true, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", document.Descendants(w + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(run => run.Value))));
    }
}
