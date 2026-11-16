using System.IO;
using Microsoft.Win32;

namespace WindowsDock;

internal static class DockStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Entry = "WindowsDock";
    private static string Controller => Path.Combine(AppContext.BaseDirectory, "NativeTaskbar.exe");
    private static string Dashboard => Path.Combine(AppContext.BaseDirectory, "WindowsDock.exe");

    internal static bool Enabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Entry) is string { Length: > 0 };
    }

    internal static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled)
        {
            key.DeleteValue(Entry, throwOnMissingValue: false);
            return;
        }
        if (!File.Exists(Controller) || !File.Exists(Dashboard))
            throw new IOException("Keep the dashboard and native controller together before enabling startup.");
        key.SetValue(Entry, $"\"{Dashboard}\" --start-at-logon", RegistryValueKind.String);
    }

    internal static int Launch() => DockTaskbar.StartController(openDashboard: false);
}
