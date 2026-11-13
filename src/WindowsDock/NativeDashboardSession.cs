using System.Diagnostics;
using System.Threading;

namespace WindowsDock;

internal sealed class NativeDashboardSession : IDisposable
{
    private readonly EventWaitHandle signal;
    private readonly RegisteredWaitHandle subscription;
    private readonly Process controller;
    private readonly Action exit;

    private NativeDashboardSession(EventWaitHandle signal, Process controller, Action show, Action exit)
    {
        this.signal = signal;
        this.controller = controller;
        this.exit = exit;
        controller.Exited += ControllerExited;
        controller.EnableRaisingEvents = true;
        subscription = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => show(),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static NativeDashboardSession? Connect(int pid, Action show, Action exit)
    {
        if (pid <= 0 || !EventWaitHandle.TryOpenExisting(
            @"Local\WindowsDock.NativeTaskbar." + pid + ".ShowDashboard", out var signal))
            return null;

        Process? controller = null;
        try
        {
            controller = Process.GetProcessById(pid);
            return new NativeDashboardSession(signal, controller, show, exit);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            controller?.Dispose();
            signal.Dispose();
            return null;
        }
    }

    private void ControllerExited(object? sender, EventArgs e) => exit();

    public void Dispose()
    {
        subscription.Unregister(null);
        controller.Exited -= ControllerExited;
        controller.Dispose();
        signal.Dispose();
    }
}
