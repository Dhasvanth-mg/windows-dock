using System.Diagnostics;
using System.IO;
using System.Text;

namespace WindowsDock;

/// <summary>
/// Small append-only diagnostic log shared by every Windows Dock process (and, for the native side, by
/// <c>native.log</c> in the same folder). It records what failed and where, never prompts, replies, or credentials.
/// </summary>
internal static class DockLog
{
    private const long MaxBytes = 512 * 1024;
    private const int MaxLine = 1500;

    internal static string Folder => Path.Combine(DockData.Root, "Logs");
    internal static string FilePath => Path.Combine(Folder, "dock.log");

    /// <summary>
    /// Records one line. The exception type and HRESULT are always logged. The message and top stack frames
    /// are logged only when <paramref name="detail"/> is set, because messages from provider CLIs can echo user content.
    /// </summary>
    internal static void Write(string area, string message, Exception? error = null, bool detail = false)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
                .Append(" [").Append(Process.GetCurrentProcess().Id).Append("] ")
                .Append(area).Append(": ").Append(message);
            if (error != null)
            {
                line.Append(" | ").Append(error.GetType().Name).Append(" 0x").Append(error.HResult.ToString("X8"));
                if (detail)
                {
                    line.Append(" | ").Append(error.Message);
                    var frames = (error.StackTrace ?? "").Split('\n').Take(6).Select(frame => frame.Trim());
                    line.Append(" | ").Append(string.Join(" <- ", frames));
                }
            }
            var text = Flatten(line.ToString());
            if (text.Length > MaxLine)
                text = text[..MaxLine];

            Directory.CreateDirectory(Folder);
            var path = FilePath;
            try
            {
                if (new FileInfo(path) is { Exists: true, Length: > MaxBytes })
                    File.Move(path, path + ".1", true);
            }
            catch (Exception rotate) when (rotate is IOException or UnauthorizedAccessException) { }

            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var bytes = Encoding.UTF8.GetBytes(text + Environment.NewLine);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception) { }
    }

    private static string Flatten(string text) =>
        new(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
}
