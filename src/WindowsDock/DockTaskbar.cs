using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WindowsDock;

internal static class DockTaskbar
{
    private const string ControllerClass = "WindowsDockNativeController";
    private const string ControllerMutex = @"Local\WindowsDock.NativeTaskbar.Controller";

    internal static bool ShellReady() => FindWindowW("Shell_TrayWnd", null) != 0;

    internal static int Open()
    {
        var result = StartController(trayMode: !DockStartup.UseNativeMode(), openDashboard: true);
        if (result != 0)
            MessageBox.Show("Windows Dock could not start. Check that both executables are in the same folder.", "Windows Dock");
        return result;
    }

    internal static int StartController(bool trayMode, bool openDashboard)
    {
        if (Ready())
            return 0;

        var controller = Path.Combine(AppContext.BaseDirectory, "NativeTaskbar.exe");
        Process? process = null;
        try
        {
            var attempted = false;
            var deadline = Environment.TickCount64 + 12_000;
            while (Environment.TickCount64 < deadline)
            {
                if (Ready())
                    return 0;
                if ((process == null || process.HasExited) && !ControllerPending())
                {
                    if (attempted)
                        break;
                    attempted = true;
                    process = Process.Start(new ProcessStartInfo(controller, trayMode ? "--tray" : "")
                    {
                        WorkingDirectory = AppContext.BaseDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                Thread.Sleep(35);
            }
            if (Ready())
                return 0;
            DockLog.Write("startup", "The controller did not publish a ready entry point. See native.log.");
            return 1;
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            if (Ready())
                return 0;
            DockLog.Write("startup", "The dock controller could not be started.", error, detail: true);
            return 1;
        }
        finally { process?.Dispose(); }

        bool Ready() => openDashboard ? SignalLauncher() : ReadyController() != 0;
    }

    private static bool ControllerPending()
    {
        if (!Mutex.TryOpenExisting(ControllerMutex, out var controller))
            return false;
        controller.Dispose();
        return true;
    }

    private static int ReadyController()
    {
        var window = FindWindowW(ControllerClass, null);
        if (window == 0 || GetWindowThreadProcessId(window, out var pid) == 0)
            return 0;
        if (!EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "Stop"), out var stop))
            return 0;
        using (stop)
            return stop.WaitOne(0) ? 0 : pid;
    }

    private static bool SignalLauncher()
    {
        var pid = ReadyController();
        if (pid == 0)
            return false;
        if (!EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "OpenLauncher"), out var launch) &&
            !EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "Dashboard"), out launch))
            return false;
        using (launch)
        {
            AllowSetForegroundWindow(pid);
            return launch.Set();
        }
    }

    internal static void ReportVisibility(int pid, bool visible)
    {
        if (!EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "PanelVisible"), out var state))
            return;
        using (state)
        {
            if (visible)
                state.Set();
            else
                state.Reset();
        }
        if (EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "PanelVisibilityChanged"), out var change))
            using (change) change.Set();
    }

    internal static double AnchorTop(Rectangle screen, double scale)
    {
        var pos = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(5, ref pos) != 0 && pos.Edge == 3 && pos.Rect.Top >= screen.Top &&
            pos.Rect.Top < screen.Bottom && pos.Rect.Right > screen.Left && pos.Rect.Left < screen.Right)
            return pos.Rect.Top;
        return screen.Bottom - 48 * scale;
    }

    internal static Border IconBadge(double size = 35)
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/WindowsDock;component/DockIcon.ico"));
        using var stream = resource!.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var icon = decoder.Frames.OrderBy(frame => Math.Abs(frame.PixelWidth - 48)).First();
        return new Border { Width = size, Height = size, Child = new System.Windows.Controls.Image { Source = icon } };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        internal uint Size;
        internal nint Window;
        internal uint Callback, Edge;
        internal WindowRect Rect;
        internal nint Parameter;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string className, string? title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("shell32.dll")]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
}
