using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace WindowsDock;

internal sealed record FocusState(int Minutes, DateTimeOffset? Ends);

internal static class DockMonitor
{
    internal static int Run(int controllerPid)
    {
        using var grid = AgentGrid.Connect(controllerPid);
        using var controller = FindController(controllerPid);
        if (grid == null || controller == null ||
            !EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(controllerPid, "Stop"), out var stop))
            return 1;

        using (stop)
        {
            var focusFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsDock", "Dashboard", "focus.json");
            var nextScan = DateTime.UtcNow;
            var started = nextScan;
            var lastFocusWrite = DateTime.MinValue;
            DateTimeOffset? focusEnds = null;
            do
            {
                var now = DateTime.UtcNow;
                if (now >= nextScan)
                {
                    if (controller.HasExited)
                        return 0;
                    var sessions = AgentSessions.Rank(AgentSessions.Read(), AgentApprovals.Read(), grid.QuotaBlocked());
                    grid.Publish(AgentSlots.Select(AgentSlots.Load(), sessions));
                    nextScan = DateTime.UtcNow.AddSeconds(6);
                    var stamp = LastFocusWrite(focusFile);
                    if (stamp != lastFocusWrite)
                    {
                        var focus = ReadFocus(focusFile);
                        focusEnds = focus is { Minutes: >= 1 and <= 120 } && focus.Ends > started ? focus.Ends : null;
                        lastFocusWrite = stamp;
                    }
                }
                if (focusEnds is { } ends && ends <= now)
                {
                    if (LastFocusWrite(focusFile) != lastFocusWrite)
                    {
                        nextScan = now;
                        continue;
                    }
                    focusEnds = null;
                    System.Media.SystemSounds.Asterisk.Play();
                }
                var wake = focusEnds is { } end && end.UtcDateTime < nextScan ? end.UtcDateTime : nextScan;
                if (stop.WaitOne(TimeSpan.FromMilliseconds(Math.Max(0, (wake - DateTime.UtcNow).TotalMilliseconds))))
                    return 0;
            } while (true);
        }
    }

    private static FocusState? ReadFocus(string path)
    {
        try { return JsonSerializer.Deserialize<FocusState>(File.ReadAllText(path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static DateTime LastFocusWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private static Process? FindController(int pid)
    {
        try { return Process.GetProcessById(pid); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
    }
}
