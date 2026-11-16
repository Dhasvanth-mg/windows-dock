using System.Threading;
using System.IO;
using System.Windows;

namespace WindowsDock;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private NativeDashboardSession? dashboardSession;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--dashboard")
        {
            var controller = e.Args.Length >= 2 && int.TryParse(e.Args[1], out var pid) ? pid : 0;
            var initialPage = controller == 0 && e.Args.Length == 2 ? e.Args[1] : null;
            base.OnStartup(e);
            if (controller > 0)
            {
                dashboardSession = NativeDashboardSession.Connect(controller,
                    () => Dispatcher.BeginInvoke(() => (MainWindow as DashboardWindow)?.ToggleDockDashboard()),
                    () => Dispatcher.BeginInvoke(() => ShowDashboard(true)),
                    () => Dispatcher.BeginInvoke(() => ShowAssistant()),
                    () => Dispatcher.BeginInvoke(() => (MainWindow as DashboardWindow)?.ShowDockDashboard()),
                    () => Dispatcher.BeginInvoke(() => (MainWindow as DashboardWindow)?.ShowCommandPalette()),
                    () => Dispatcher.InvokeAsync(() => Shutdown()));
                if (dashboardSession == null)
                {
                    Shutdown();
                    return;
                }
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            var trayMode = e.Args.Length == 3 && e.Args[2] == "--tray";
            var onDemand = trayMode || e.Args.Length == 3 && e.Args[2] == "--on-demand";
            var hidden = onDemand || e.Args.Length == 3 && e.Args[2] == "--hidden";
            MainWindow = new DashboardWindow(controller, initialPage, hidden, onDemand, trayMode);
            if (onDemand)
                MainWindow.Closed += (_, _) => Shutdown();
            if (!hidden)
                ShowDashboard(false);
            return;

            void ShowDashboard(bool agents)
            {
                if (MainWindow is DashboardWindow current)
                {
                    if (agents)
                        current.OpenAgentGrid();
                    else
                        current.OpenMainDashboard();
                    if (!current.IsVisible)
                        current.Show();
                    current.Activate();
                }
            }

            void ShowAssistant()
            {
                if (MainWindow is DashboardWindow current)
                {
                    if (!current.IsVisible)
                        current.ShowDockPanelHost();
                    current.ShowAssistantPicker();
                }
            }
        }

        instance = new Mutex(true, @"Local\WindowsDock.Companion", out ownsInstance);
        if (!ownsInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        dashboardSession?.Dispose();
        if (ownsInstance)
            instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}

internal static class DockEntryPoint
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DockLog.Write("crash", "Unhandled exception (" + (args.Length > 0 ? args[0] : "app") + ")",
                e.ExceptionObject as Exception, detail: true);
        if (args is ["--open"])
            return DockTaskbar.Open();
        if (args is ["--start-at-logon"] or ["--start-at-logon", "--tray"])
            return DockStartup.Launch();
        if (args is ["--monitor", var controller] && int.TryParse(controller, out var pid) && pid > 0)
            return DockMonitor.Run(pid);
        if (args is ["--planner-mcp"])
            return PlannerMcp.Run();
        if (args is ["--dock-mcp"] or ["--dock-mcp", "--tray"])
            return DockMcp.Run();
        if (args is ["--agent-status", _] or ["--agent-status", _, "--approval"])
        {
            try { AgentSessions.Receive(args[1], Console.In, Console.Out, args.Length == 3); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            return 0;
        }
        return RunApplication();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int RunApplication()
    {
        var app = new App();
        app.DispatcherUnhandledException += (_, e) => DockLog.Write("ui", "Unhandled UI exception", e.Exception, detail: true);
        app.InitializeComponent();
        return app.Run();
    }
}
