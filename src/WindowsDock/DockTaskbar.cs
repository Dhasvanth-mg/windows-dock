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
    internal static int Open()
    {
        if (SignalLauncher())
            return 0;

        var controller = Path.Combine(AppContext.BaseDirectory, "NativeTaskbar.exe");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(controller, "--tray --open")
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return process == null ? 1 : 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (SignalLauncher())
                return 0;
            MessageBox.Show("Keep NativeTaskbar.exe and WindowsDock.exe in the same folder.", "Windows Dock");
            return 1;
        }
    }

    private static bool SignalLauncher()
    {
        var window = FindWindowW("WindowsDockNativeController", null);
        if (window == 0 || GetWindowThreadProcessId(window, out var pid) == 0)
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
