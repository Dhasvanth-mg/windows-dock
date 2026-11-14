using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace WindowsDock;

internal sealed record AppWindow(nint Handle, string Title, string Path, uint ProcessId = 0);

internal sealed class ShellIntegration : IDisposable
{
    private const uint ForegroundEvent = 3;
    private const uint ObjectCreate = 0x8000;
    private const uint ObjectHide = 0x8003;
    private const uint ObjectNameChange = 0x800C;
    private readonly DispatcherTimer refresh;
    private readonly WinEvent callback;
    private readonly nint[] hooks;

    public ShellIntegration(Action changed)
    {
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        refresh.Tick += (_, _) =>
        {
            refresh.Stop();
            changed();
        };

        callback = (_, eventType, window, objectId, childId, _, _) =>
        {
            if (window != 0 && objectId == 0 && childId == 0 &&
                (eventType is ForegroundEvent or ObjectNameChange or >= ObjectCreate and <= ObjectHide))
                refresh.Start();
        };
        const uint skipOwnProcess = 2;
        hooks = [
            SetWinEventHook(ForegroundEvent, ForegroundEvent, 0, callback, 0, 0, skipOwnProcess),
            SetWinEventHook(ObjectCreate, ObjectHide, 0, callback, 0, 0, skipOwnProcess),
            SetWinEventHook(ObjectNameChange, ObjectNameChange, 0, callback, 0, 0, skipOwnProcess)
        ];
    }

    public static IReadOnlyList<AppWindow> Windows()
    {
        var windows = new List<AppWindow>();
        var processes = new Dictionary<uint, string>();
        EnumWindows((window, _) =>
        {
            const uint owner = 4;
            const int extendedStyle = -20;
            const int toolWindow = 0x80;
            const int appWindow = 0x40000;
            const uint cloakedAttribute = 14;
            var style = GetWindowLong(window, extendedStyle);
            if (!IsWindowVisible(window) ||
                ((style & appWindow) == 0 && ((style & toolWindow) != 0 || GetWindow(window, owner) != 0)) ||
                (DwmGetWindowAttribute(window, cloakedAttribute, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
                return true;

            GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId)
                return true;

            var title = new StringBuilder(512);
            if (GetWindowText(window, title, title.Capacity) == 0)
                return true;

            var name = title.ToString();
            if (name is not ("Program Manager" or "Windows Input Experience"))
            {
                if (!processes.TryGetValue(processId, out var path))
                    processes[processId] = path = ProcessPath(processId);
                windows.Add(new AppWindow(window, name, path, processId));
            }
            return true;
        }, 0);
        return windows;
    }

    internal static string ProcessPath(uint processId)
    {
        const uint queryLimitedInformation = 0x1000;
        var process = OpenProcess(queryLimitedInformation, false, processId);
        if (process == 0)
            return "";

        try
        {
            var path = new StringBuilder(32768);
            var length = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static bool Switch(nint window, nint priorForeground)
    {
        if (!IsWindow(window))
            return false;

        if (priorForeground == window && !IsIconic(window))
            return ShowWindowAsync(window, 6);

        if (IsIconic(window))
            ShowWindowAsync(window, 9);
        return SetForegroundWindow(window);
    }

    internal static bool Activate(AppWindow target)
    {
        if (!IsWindowVisible(target.Handle) ||
            GetWindowThreadProcessId(target.Handle, out var process) == 0 || process != target.ProcessId)
            return false;
        return Switch(target.Handle, 0);
    }

    public static void Launch(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public void Dispose()
    {
        refresh.Stop();
        foreach (var hook in hooks)
            if (hook != 0)
                UnhookWinEvent(hook);
    }

    private delegate void WinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    private delegate bool EnumWindow(nint window, nint parameter);

    [DllImport("user32")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32")] private static extern bool IsWindow(nint window);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] private static extern bool IsIconic(nint window);
    [DllImport("user32")] internal static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder title, int length);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, uint size);
    [DllImport("kernel32")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
}
