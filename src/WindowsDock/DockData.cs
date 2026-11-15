using System.IO;
using System.Text.Json;
using System.Threading;

namespace WindowsDock;

internal static class DockData
{
    private const int KeptAside = 3;

    /// <summary>Test seam. When set, all managed state lives under this folder instead of LocalAppData.</summary>
    internal static string? RootOverride { get; set; }

    internal static string Root => RootOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDock");

    internal static string FilePath(string name) => Path.Combine(Root, "Dashboard", name);

    /// <summary>
    /// Reads a state file. A missing or inaccessible file gives the default. A file that is not valid JSON is moved
    /// aside (see <see cref="Quarantine"/>) so a later write cannot silently destroy it.
    /// </summary>
    internal static T? Read<T>(string name)
    {
        try { return ReadCore<T>(name); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return default; }
    }

    private static T? ReadCore<T>(string name)
    {
        var path = FilePath(name);
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return default; }
        try { return JsonSerializer.Deserialize<T>(text); }
        catch (JsonException error)
        {
            Quarantine(path, error);
            return default;
        }
    }

    internal static void Save<T>(string name, T value) =>
        Write(name, path => File.WriteAllText(path, JsonSerializer.Serialize(value)));

    /// <summary>
    /// Read-modify-write under a named mutex. Unlike <see cref="Read{T}"/>, a transient read failure (a locked or
    /// unreadable file) aborts the update instead of replacing the file with a value built from the default.
    /// </summary>
    internal static T Update<T>(string name, Func<T?, T> change)
    {
        using var gate = new Mutex(false, @"Local\WindowsDock.Data." + name);
        try
        {
            if (!gate.WaitOne(TimeSpan.FromSeconds(2)))
                throw new IOException("Windows Dock is saving another change.");
        }
        catch (AbandonedMutexException) { }
        try
        {
            var next = change(ReadCore<T>(name));
            Save(name, next);
            return next;
        }
        finally { gate.ReleaseMutex(); }
    }

    internal static void Write(string name, Action<string> write)
    {
        var path = FilePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            write(pending);
            File.Move(pending, path, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private static void Quarantine(string path, Exception error)
    {
        try
        {
            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            DockLog.Write("data", "Moved unreadable " + Path.GetFileName(path) + " aside; using defaults.", error);
            KeepNewest(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*", KeptAside);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> files matching the pattern.</summary>
    internal static void KeepNewest(string directory, string pattern, int keep)
    {
        try
        {
            foreach (var stale in new DirectoryInfo(directory).EnumerateFiles(pattern)
                .OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name).Skip(keep))
            {
                try { stale.Delete(); }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
}
