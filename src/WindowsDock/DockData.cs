using System.IO;
using System.Text.Json;
using System.Threading;

namespace WindowsDock;

internal static class DockData
{
    internal static string FilePath(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDock", "Dashboard", name);

    internal static T? Read<T>(string name)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath(name))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }

    internal static void Save<T>(string name, T value) =>
        Write(name, path => File.WriteAllText(path, JsonSerializer.Serialize(value)));

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
            var next = change(Read<T>(name));
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
}
