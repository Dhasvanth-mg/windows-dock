using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
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

    internal static bool UseNativeMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return (key?.GetValue(Entry) as string)?.EndsWith(" --start-at-logon", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static void Set(bool enabled, bool trayMode)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled)
        {
            key.DeleteValue(Entry, throwOnMissingValue: false);
            return;
        }
        if (!File.Exists(Controller) || !File.Exists(Dashboard))
            throw new IOException("Keep the dashboard and native controller together before enabling startup.");
        key.SetValue(Entry, $"\"{Dashboard}\" --start-at-logon{(trayMode ? " --tray" : "")}", RegistryValueKind.String);
    }

    internal static int Launch(bool trayMode)
    {
        for (var attempt = 0; attempt < 40 && !DockTaskbar.ShellReady(); attempt++)
            Thread.Sleep(500);
        if (!DockTaskbar.ShellReady())
            return 1;
        if (DockTaskbar.ControllerRunning())
            return 0;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Controller, trayMode ? "--tray" : "")
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return process == null || process.WaitForExit(1500) && process.ExitCode != 0 ? 1 : 0;
        }
        catch (Win32Exception) { return 1; }
    }
}
