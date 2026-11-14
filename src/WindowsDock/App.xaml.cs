using System.Threading;
using System.Windows;

namespace WindowsDock;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private NativeDashboardSession? dashboardSession;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--agent-status", _] or ["--agent-status", _, "--approval"])
        {
            try { AgentSessions.Receive(e.Args[1], Console.In, Console.Out, e.Args.Length == 3); }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--dashboard")
        {
            var controller = e.Args.Length >= 2 && int.TryParse(e.Args[1], out var pid) ? pid : 0;
            var initialPage = controller == 0 && e.Args.Length == 2 ? e.Args[1] : null;
            base.OnStartup(e);
            if (controller > 0)
            {
                dashboardSession = NativeDashboardSession.Connect(controller,
                    () => Dispatcher.BeginInvoke(() => ShowDashboard(false)),
                    () => Dispatcher.BeginInvoke(() => ShowDashboard(true)),
                    () => Dispatcher.InvokeAsync(() => Shutdown()));
                if (dashboardSession == null)
                {
                    Shutdown();
                    return;
                }
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            var hidden = e.Args.Length == 3 && e.Args[2] == "--hidden";
            MainWindow = new DashboardWindow(controller, initialPage, hidden);
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
