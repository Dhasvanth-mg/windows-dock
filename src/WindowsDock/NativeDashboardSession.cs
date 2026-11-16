using System.Diagnostics;
using System.Threading;

namespace WindowsDock;

internal sealed class NativeDashboardSession : IDisposable
{
    private readonly EventWaitHandle signal;
    private readonly EventWaitHandle? agentSignal;
    private readonly EventWaitHandle? assistantSignal;
    private readonly EventWaitHandle? launcherSignal;
    private readonly EventWaitHandle? commandSignal;
    private readonly RegisteredWaitHandle subscription;
    private readonly RegisteredWaitHandle? agentSubscription;
    private readonly RegisteredWaitHandle? assistantSubscription;
    private readonly RegisteredWaitHandle? launcherSubscription;
    private readonly RegisteredWaitHandle? commandSubscription;
    private readonly Process controller;
    private readonly Action exit;

    private NativeDashboardSession(EventWaitHandle signal, EventWaitHandle? agentSignal, EventWaitHandle? assistantSignal,
        EventWaitHandle? launcherSignal, EventWaitHandle? commandSignal, Process controller, Action show, Action showAgent,
        Action showAssistant, Action showLauncher, Action showCommand, Action exit)
    {
        this.signal = signal;
        this.agentSignal = agentSignal;
        this.assistantSignal = assistantSignal;
        this.launcherSignal = launcherSignal;
        this.commandSignal = commandSignal;
        this.controller = controller;
        this.exit = exit;
        controller.Exited += ControllerExited;
        controller.EnableRaisingEvents = true;
        subscription = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => show(),
            null, Timeout.Infinite, executeOnlyOnce: false);
        if (agentSignal != null)
            agentSubscription = ThreadPool.RegisterWaitForSingleObject(agentSignal, (_, _) => showAgent(),
                null, Timeout.Infinite, executeOnlyOnce: false);
        if (assistantSignal != null)
            assistantSubscription = ThreadPool.RegisterWaitForSingleObject(assistantSignal, (_, _) => showAssistant(),
                null, Timeout.Infinite, executeOnlyOnce: false);
        if (launcherSignal != null)
            launcherSubscription = ThreadPool.RegisterWaitForSingleObject(launcherSignal, (_, _) => showLauncher(),
                null, Timeout.Infinite, executeOnlyOnce: false);
        if (commandSignal != null)
            commandSubscription = ThreadPool.RegisterWaitForSingleObject(commandSignal, (_, _) => showCommand(),
                null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static NativeDashboardSession? Connect(int pid, Action show, Action showAgent, Action showAssistant,
        Action showLauncher, Action showCommand, Action exit)
    {
        if (pid <= 0 || !EventWaitHandle.TryOpenExisting(
            AgentGrid.SessionObject(pid, "ShowDashboard"), out var signal))
            return null;

        Process? controller = null;
        EventWaitHandle? agentSignal = null;
        EventWaitHandle? assistantSignal = null;
        EventWaitHandle? launcherSignal = null;
        EventWaitHandle? commandSignal = null;
        try
        {
            controller = Process.GetProcessById(pid);
            EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "ShowAgent"), out agentSignal);
            EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "ShowAssistant"), out assistantSignal);
            EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "ShowLauncher"), out launcherSignal);
            EventWaitHandle.TryOpenExisting(AgentGrid.SessionObject(pid, "ShowCommand"), out commandSignal);
            return new NativeDashboardSession(signal, agentSignal, assistantSignal, launcherSignal, commandSignal, controller, show,
                showAgent, showAssistant, showLauncher, showCommand, exit);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            controller?.Dispose();
            signal.Dispose();
            agentSignal?.Dispose();
            assistantSignal?.Dispose();
            launcherSignal?.Dispose();
            commandSignal?.Dispose();
            return null;
        }
    }

    private void ControllerExited(object? sender, EventArgs e) => exit();

    public void Dispose()
    {
        subscription.Unregister(null);
        agentSubscription?.Unregister(null);
        assistantSubscription?.Unregister(null);
        launcherSubscription?.Unregister(null);
        commandSubscription?.Unregister(null);
        controller.Exited -= ControllerExited;
        controller.Dispose();
        agentSignal?.Dispose();
        assistantSignal?.Dispose();
        launcherSignal?.Dispose();
        commandSignal?.Dispose();
        signal.Dispose();
    }
}
