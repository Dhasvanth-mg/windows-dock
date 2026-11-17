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
            var alarmFile = DockData.FilePath(DockAlarms.FileName);
            var nextScan = DateTime.UtcNow;
            var started = nextScan;
            var lastFocusWrite = DateTime.MinValue;
            var lastAlarmWrite = DateTime.MinValue;
            DateTimeOffset? focusEnds = null;
            DateTimeOffset? nextAlarm = null;
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
                        try
                        {
                            var completed = nextInbox.Any(item => item.State == AgentState.Finished && item.At >= started &&
                                !inbox.Any(previous => previous.Key == item.Key));
                            DockData.Save("activity-inbox.json", nextInbox);
                            inbox = nextInbox;
                            if (completed)
                                DockCompletion.Signal(controllerPid);
                        }
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
                    var alarmStamp = LastFocusWrite(alarmFile);
                    if (alarmStamp != lastAlarmWrite)
                    {
                        nextAlarm = DockAlarms.Next(DockAlarms.Load());
                        lastAlarmWrite = alarmStamp;
                    }
                }
                if (nextAlarm is { } alarmTime && alarmTime <= DateTimeOffset.UtcNow)
                {
                    try
                    {
                        foreach (var alarm in DockAlarms.TakeDue(DateTimeOffset.UtcNow))
                            Ring(alarm);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        DockLog.Write("alarm", "Could not read or update alarms", error);
                    }
                    nextAlarm = DockAlarms.Next(DockAlarms.Load());
                    lastAlarmWrite = LastFocusWrite(alarmFile);
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
                if (nextAlarm is { } upcoming && upcoming.UtcDateTime < wake)
                    wake = upcoming.UtcDateTime;
                if (stop.WaitOne(TimeSpan.FromMilliseconds(Math.Max(0, (wake - DateTime.UtcNow).TotalMilliseconds))))
                    return 0;
            } while (true);
        }
    }

    // An alarm has to get through even when nothing of the dock is on screen, so it plays a sound and puts a small
    // message on top of everything. The message runs on its own thread, so the monitor keeps working while it waits.
    internal static void Ring(DockAlarm alarm)
    {
        System.Media.SystemSounds.Exclamation.Play();
        var text = $"{alarm.Label}\n\n{alarm.At.ToLocalTime():t}";
        var thread = new Thread(() =>
        {
            const uint information = 0x40, topmost = 0x40000, foreground = 0x10000, systemModal = 0x1000;
            MessageBoxW(0, text, "Windows Dock alarm", information | topmost | foreground | systemModal);
        }) { IsBackground = true, Name = "Alarm" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);

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
