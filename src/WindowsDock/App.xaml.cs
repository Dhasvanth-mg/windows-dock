using System.Threading;
using System.Windows;

namespace WindowsDock;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        if (ownsInstance)
            instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
