using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace WindowsDock;

internal sealed record WorkspaceWindow(string Path, string Title, string Monitor, double Left, double Top,
    double Width, double Height, bool Maximized)
{
    internal Rectangle? BoundsFor(Rectangle work)
    {
        if (work.Width < 1 || work.Height < 1 || !double.IsFinite(Left) || !double.IsFinite(Top) ||
            !double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
            return null;
        var width = Math.Clamp((int)Math.Round(Math.Min(Width, 1) * work.Width), Math.Min(240, work.Width), work.Width);
        var height = Math.Clamp((int)Math.Round(Math.Min(Height, 1) * work.Height), Math.Min(160, work.Height), work.Height);
        var left = work.Left + Math.Clamp((int)Math.Round(Math.Clamp(Left, 0, 1) * work.Width), 0, work.Width - width);
        var top = work.Top + Math.Clamp((int)Math.Round(Math.Clamp(Top, 0, 1) * work.Height), 0, work.Height - height);
        return new Rectangle(left, top, width, height);
    }
}

internal sealed record WorkspaceSelection(AppWindow Window, WorkspaceWindow Placement);

internal enum WorkspaceTiling { SideBySide, Stacked, Grid }

internal static class DockWorkspaces
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] Hosts = ["explorer.exe", "applicationframehost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe"];

    internal static WorkspaceSelection[] AvailableWindows()
    {
        var items = new List<WorkspaceSelection>();
        foreach (var window in ShellIntegration.Windows())
        {
            if (items.Count >= 32)
                break;
            if (!IsLaunchable(window.Path) || IsIconic(window.Handle) || !GetWindowRect(window.Handle, out var bounds))
                continue;
            var screen = Forms.Screen.FromHandle(window.Handle);
            var area = screen.WorkingArea;
            var visible = Rectangle.Intersect(area, Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            if (visible.Width < 80 || visible.Height < 60 || area.Width <= 0 || area.Height <= 0)
                continue;
            var placement = new WorkspaceWindow(window.Path, window.Title, screen.DeviceName,
                (visible.Left - area.Left) / (double)area.Width, (visible.Top - area.Top) / (double)area.Height,
                visible.Width / (double)area.Width, visible.Height / (double)area.Height, IsZoomed(window.Handle));
            items.Add(new(window, placement));
        }
        return items.ToArray();
    }

    internal static WorkspaceSelection[] Arrange(WorkspaceSelection[] windows, WorkspaceTiling mode)
    {
        var position = new List<WorkspaceSelection>();
        foreach (var monitor in windows.GroupBy(item => item.Placement.Monitor, Paths))
        {
            var group = monitor.ToArray();
            var columns = mode switch
            {
                WorkspaceTiling.SideBySide => group.Length,
                WorkspaceTiling.Stacked => 1,
                _ => (int)Math.Ceiling(Math.Sqrt(group.Length))
            };
            var rows = (int)Math.Ceiling((double)group.Length / columns);
            for (var index = 0; index < group.Length; index++)
            {
                var row = index / columns;
                var inRow = Math.Min(columns, group.Length - row * columns);
                var saved = group[index];
                var tile = saved.Placement with
                {
                    Left = (index % columns) / (double)inRow,
                    Top = row / (double)rows,
                    Width = 1d / inRow,
                    Height = 1d / rows,
                    Maximized = false
                };
                position.Add(saved with { Placement = tile });
            }
        }
        return position.ToArray();
    }

    internal static bool FitsTiles(IEnumerable<WorkspaceSelection> windows)
    {
        var screens = Forms.Screen.AllScreens;
        return windows.All(item =>
        {
            var screen = screens.FirstOrDefault(screen => Paths.Equals(screen.DeviceName, item.Placement.Monitor));
            return screen != null && item.Placement.Width * screen.WorkingArea.Width >= 240 &&
                item.Placement.Height * screen.WorkingArea.Height >= 160;
        });
    }

    internal static int PlaceSelected(IEnumerable<WorkspaceSelection> windows)
    {
        var current = ShellIntegration.Windows().ToDictionary(WindowKey);
        return windows.Count(item => !current.TryGetValue(WindowKey(item.Window), out var live) ||
            !Paths.Equals(live.Path, item.Window.Path) || !Place(live, item.Placement));
    }

    internal static async Task<int> RestoreAsync(WorkspaceWindow[] layout, CancellationToken token,
        IReadOnlyList<AppWindow> previousWindows, bool openedTargets)
    {
        var pending = layout.Take(32).Where(item => IsLaunchable(item.Path)).ToList();
        var failures = Math.Min(layout.Length, 32) - pending.Count;
        var assigned = new HashSet<(nint Handle, uint ProcessId)>();
        var beforeLaunch = previousWindows.Select(WindowKey).ToHashSet();
        if (openedTargets && pending.Count > 0)
            await Task.Delay(300, token);
        var windows = ShellIntegration.Windows();
        var started = false;
        foreach (var path in pending.Select(item => item.Path).Distinct(Paths))
        {
            token.ThrowIfCancellationRequested();
            if (windows.Any(window => Paths.Equals(window.Path, path)))
                continue;
            try { ShellIntegration.Launch(path); started = true; }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
        }
        for (var round = 0; round <= (started || openedTargets ? 12 : 0) && pending.Count > 0; round++)
        {
            token.ThrowIfCancellationRequested();
            if (round > 0)
            {
                await Task.Delay(250, token);
                windows = ShellIntegration.Windows();
            }
            // Give the named windows first pick and only fall back to windows created during this restore.
            Match(exact: true);
            Match(exact: false);
        }
        return failures + pending.Count;

        void Match(bool exact)
        {
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var saved = pending[index];
                var match = FindWindow(windows, saved, assigned, beforeLaunch, exact);
                if (match == null)
                    continue;
                assigned.Add(WindowKey(match));
                pending.RemoveAt(index);
                if (!Place(match, saved))
                    failures++;
            }
        }
    }

    internal static AppWindow? FindWindow(IEnumerable<AppWindow> candidates, WorkspaceWindow saved,
        HashSet<(nint Handle, uint ProcessId)> assigned, HashSet<(nint Handle, uint ProcessId)> beforeLaunch, bool exact) =>
        candidates.FirstOrDefault(window => !assigned.Contains(WindowKey(window)) && Paths.Equals(window.Path, saved.Path) &&
            (exact ? window.Title.Equals(saved.Title, StringComparison.OrdinalIgnoreCase) : !beforeLaunch.Contains(WindowKey(window))));

    private static (nint Handle, uint ProcessId) WindowKey(AppWindow window) => (window.Handle, window.ProcessId);

    private static bool IsLaunchable(string path) => Path.IsPathFullyQualified(path) &&
        !path.StartsWith(@"\\", StringComparison.Ordinal) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
        !Hosts.Contains(Path.GetFileName(path), Paths) && File.Exists(path);

    private static bool Place(AppWindow window, WorkspaceWindow saved)
    {
        if (GetWindowThreadProcessId(window.Handle, out var process) == 0 || process != window.ProcessId)
            return false;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(item => Paths.Equals(item.DeviceName, saved.Monitor))
            ?? Forms.Screen.FromHandle(window.Handle);
        if (saved.BoundsFor(screen.WorkingArea) is not { } bounds)
            return false;
        if (IsIconic(window.Handle) || IsZoomed(window.Handle))
            ShowWindowAsync(window.Handle, 9);
        // Keep application focus and z order as they are while applying a chosen workspace.
        const uint noActivate = 0x0010, noZOrder = 0x0004, asyncPosition = 0x4000;
        if (!SetWindowPos(window.Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, noActivate | noZOrder | asyncPosition))
            return false;
        if (saved.Maximized)
            ShowWindowAsync(window.Handle, 3);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
