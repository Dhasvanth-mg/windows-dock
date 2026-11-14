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
            var focusFile = DockData.FilePath("focus.json");
            var nextScan = DateTime.UtcNow;
            var started = nextScan;
            var lastFocusWrite = DateTime.MinValue;
            DateTimeOffset? focusEnds = null;
            var inbox = DockData.Read<InboxItem[]>("activity-inbox.json") ?? [];
            DateTimeOffset? quotaSince = null;
            do
            {
                var now = DateTime.UtcNow;
                if (now >= nextScan)
                {
                    if (controller.HasExited)
                        return 0;
                    var chats = AgentSessions.Read();
                    var approvals = AgentApprovals.Read();
                    var quotaBlocked = grid.QuotaBlocked();
                    quotaSince = quotaBlocked ? quotaSince ?? DateTimeOffset.UtcNow : null;
                    var sessions = AgentSessions.Rank(chats, approvals, quotaBlocked);
                    grid.Publish(AgentSlots.Select(AgentSlots.Load(), sessions));
                    var nextInbox = AgentInbox.Merge(inbox, chats, approvals, quotaSince, DateTimeOffset.UtcNow);
                    if (!inbox.SequenceEqual(nextInbox))
                    {
                        try { DockData.Save("activity-inbox.json", nextInbox); inbox = nextInbox; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                    }
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
