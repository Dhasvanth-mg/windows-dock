using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace WindowsDock;

internal sealed record AgentSlot(AgentProvider Provider, string? ChatId, string? ProjectPath, string Label)
{
    internal bool Matches(AgentSession session) => Provider == session.Provider &&
        (ChatId != null ? string.Equals(ChatId, session.DesktopId ?? session.Id, StringComparison.OrdinalIgnoreCase)
            : ProjectPath != null && session.ProjectPath != null &&
              string.Equals(Normalize(ProjectPath), Normalize(session.ProjectPath), StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');
}

internal sealed record AgentGridTile(AgentSession? Session, string Title, string Step);

internal static class AgentSlots
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsDock", "agent-slots.json");

    internal static AgentSlot?[] Load()
    {
        try
        {
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length < 16_384)
            {
                var slots = JsonSerializer.Deserialize<AgentSlot?[]>(File.ReadAllText(file.FullName));
                if (slots is { Length: 4 })
                    return slots;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new AgentSlot?[4];
    }

    internal static void Save(AgentSlot?[] slots)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(slots));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static AgentGridTile[] Select(AgentSlot?[] slots, IEnumerable<AgentSession> sessions)
    {
        var ranked = sessions.ToArray();
        var used = new HashSet<(AgentProvider, string)>();
        var tiles = new AgentGridTile[4];
        for (var index = 0; index < tiles.Length; index++)
        {
            if (slots[index] is not { } slot)
                continue;
            var session = ranked.FirstOrDefault(item => slot.Matches(item) && used.Add((item.Provider, item.Id)));
            tiles[index] = new(session, session?.Title ?? slot.Label, session?.Step ?? "");
        }
        for (var index = 0; index < tiles.Length; index++)
        {
            if (tiles[index] != null)
                continue;
            var session = ranked.FirstOrDefault(item => used.Add((item.Provider, item.Id)));
            tiles[index] = new(session, session?.Title ?? "", session?.Step ?? "");
        }
        return tiles;
    }
}

internal sealed class AgentGrid : IDisposable
{
    private const int RevisionSize = 4;
    private const int TileSize = 528;
    private const int SelectionOffset = RevisionSize + 4 * TileSize;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private byte[] previous = [];

    private AgentGrid(MemoryMappedFile mapping)
    {
        this.mapping = mapping;
        view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
    }

    internal static AgentGrid? Connect(int pid)
    {
        if (pid <= 0)
            return null;
        MemoryMappedFile? mapping = null;
        try
        {
            mapping = MemoryMappedFile.OpenExisting(SessionObject(pid, "AgentGrid"), MemoryMappedFileRights.ReadWrite);
            return new(mapping);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            mapping?.Dispose();
            return null;
        }
    }

    internal static string SessionObject(int pid, string name) => $@"Local\WindowsDock.NativeTaskbar.{pid}.{name}";

    internal void Publish(AgentGridTile[] tiles)
    {
        var data = new byte[4 * TileSize];
        for (var index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            var offset = TileSize * index;
            if (tile.Session is { } session)
            {
                BitConverter.TryWriteBytes(data.AsSpan(offset, 4), (int)session.Provider + 1);
                BitConverter.TryWriteBytes(data.AsSpan(offset + 4, 4), (int)session.State);
                WriteText(data, offset + 8, 96, session.Id);
            }
            WriteText(data, offset + 200, 100, tile.Title);
            WriteText(data, offset + 400, 64, tile.Step);
        }
        if (data.AsSpan().SequenceEqual(previous))
            return;
        var revision = (view.ReadInt32(0) & ~1) + 1;
        view.Write(0, revision);
        Thread.MemoryBarrier();
        view.WriteArray(RevisionSize, data, 0, data.Length);
        Thread.MemoryBarrier();
        view.Write(0, revision + 1);
        previous = data;
    }

    internal (int Slot, AgentProvider? Provider, string Id) ReadSelection()
    {
        var revision = view.ReadInt32(SelectionOffset);
        if ((revision & 1) != 0)
            return (-1, null, "");
        Thread.MemoryBarrier();
        var provider = view.ReadInt32(SelectionOffset + 4);
        var data = new byte[192];
        view.ReadArray(SelectionOffset + 8, data, 0, data.Length);
        var slot = view.ReadInt32(SelectionOffset + 200);
        Thread.MemoryBarrier();
        if (revision != view.ReadInt32(SelectionOffset))
            return (-1, null, "");
        return (slot is >= 0 and < 4 ? slot : -1,
            provider is 1 or 2 ? (AgentProvider)(provider - 1) : null,
            Encoding.Unicode.GetString(data).TrimEnd('\0'));
    }

    private static void WriteText(byte[] data, int offset, int size, string value)
    {
        var length = Math.Min(size - 1, value.Length);
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        Encoding.Unicode.GetBytes(value.AsSpan(0, length), data.AsSpan(offset, size * 2));
    }

    public void Dispose()
    {
        view.Dispose();
        mapping.Dispose();
    }
}
