using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WindowsDock;

// Starting the dashboard takes seconds, showing a live one takes milliseconds. So the taskbar controller starts it
// with the dock, it prepares its page and first frame out of sight, and from then on it waits hidden instead of
// exiting. While hidden it does no work, and it gives memory back when the PC is short of it; a page that has been out of sight for a while is
// rebuilt just before it shows, so nothing on it is out of date.
public partial class DashboardWindow
{
    private const long RestartAboveBytes = 900L * 1024 * 1024;
    private const uint BusyMemoryPercent = 75;
    private static readonly TimeSpan PageGoesStaleAfter = TimeSpan.FromSeconds(15);
    private bool preparing;
    private long hiddenAt;

    /// <summary>Builds the page the dashboard will open on and draws it once, without anything appearing.</summary>
    internal void PrepareHidden()
    {
        if (closed || preparing || IsVisible || pageInitialized)
            return;
        preparing = true;
        _ = Task.Run(WarmAudioReadings);
        SetDashboardSurface(false);
        ShowActivated = false;
        var scroll = RestoreViewState();
        ConfigureWidth();
        ShowPage(lastDashboardPage);
        // The first frame is what proves everything needed for showing is loaded. If it never comes, for example on
        // a locked screen, stop waiting for it.
        var giveUp = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        giveUp.Tick += (_, _) => EndPreparing();
        giveUp.Start();
        ContentRendered += Rendered;
        Show();
        RestoreScroll(scroll);

        void Rendered(object? sender, EventArgs args) => Dispatcher.BeginInvoke(EndPreparing, DispatcherPriority.Background);

        void EndPreparing()
        {
            giveUp.Stop();
            ContentRendered -= Rendered;
            this.EndPreparing();
        }
    }

    /// <summary>Finishes preparing at once, so a request to show the dashboard starts from a normal hidden window.</summary>
    internal void EndPreparing()
    {
        if (!preparing)
            return;
        Hide();
        preparing = false;
        ShowActivated = true;
        hiddenAt = Environment.TickCount64;
        if (releaseWhenIdle)
            idleExit.Start();
    }

    private static void WarmAudioReadings()
    {
        try { lastMixer = ReadMixer(); lastOutputPercent = lastMixer.Percent; }
        catch (Exception error) when (IsAudioError(error)) { }
    }

    // Reopening after a while: bring the page up to date before it is seen. Pages that hold something in progress
    // (a chat, a game, a coding conversation) stay exactly as they were.
    private void RefreshStalePage()
    {
        if (IsVisible || hiddenAt == 0 || Environment.TickCount64 - hiddenAt < PageGoesStaleAfter.TotalMilliseconds ||
            currentPage is DashboardPage.Agents or DashboardPage.Assistant or DashboardPage.Play)
        {
            if (currentPage == DashboardPage.Home)
                RenderHomeFavorites();
            return;
        }
        var offset = PageScroll.VerticalOffset;
        ShowPage(currentPage);
        RestoreScroll(offset);
    }

    private void ReleaseHiddenMemory()
    {
        if (closed || IsVisible || HasActiveWork)
            return;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var process = Process.GetCurrentProcess();
        // A long-lived process that has somehow grown large starts over; the controller brings it straight back,
        // and it reopens in the same place.
        if (process.PrivateMemorySize64 > RestartAboveBytes)
        {
            Application.Current.Shutdown();
            return;
        }
        // Handing memory back makes the next open about 40 ms slower, so it is only done when the PC could use it.
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory) && memory.Load >= BusyMemoryPercent)
            SetProcessWorkingSetSize(process.Handle, -1, -1);
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
}
