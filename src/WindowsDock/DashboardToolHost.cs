using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowsDock;

// Tool panels (timers, audio, downloads, jobs and the rest) used to open as their own window over the taskbar. Now
// they open inside the dashboard, in place of the page, under the dashboard's own tabs. Each panel still has a Window
// object, kept invisible and off screen, so everything built around panels keeps working unchanged: locking one,
// switching between locked ones, parking, closing and the Closed and IsVisible events. Only its contents move.
// The two canvas editors are wider than the dashboard and can go fullscreen, so they keep a window of their own.
public partial class DashboardWindow
{
    private const double HostedPanelLimit = 900;
    private readonly Dictionary<Window, FrameworkElement> hostedPanels = [];
    private Window? hostedWindow;

    private bool IsHosted(Window window) => hostedPanels.ContainsKey(window);

    /// <summary>Turns a new panel window into a stand-in for contents shown inside the dashboard.</summary>
    private void HostPanel(Window window, FrameworkElement card)
    {
        hostedPanels[window] = card;
        window.Width = window.Height = 1;
        window.MaxHeight = double.PositiveInfinity;
        window.Left = window.Top = -32000;
        window.Topmost = false;
        window.ShowActivated = false;
        window.IsVisibleChanged += (_, _) =>
        {
            if (window.IsVisible)
                MountPanel(window);
            else
                UnmountPanel(window);
        };
        // Keys now go to the dashboard, so pass them on to anything the panel listens for on its window.
        card.PreviewKeyDown += (_, args) =>
        {
            if (args.InputSource == null)
                return;
            var copy = new KeyEventArgs(args.KeyboardDevice, args.InputSource, args.Timestamp, args.Key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            window.RaiseEvent(copy);
            if (copy.Handled)
                args.Handled = true;
        };
    }

    private void MountPanel(Window window)
    {
        if (!hostedPanels.TryGetValue(window, out var card))
            return;
        hostedWindow = window;
        ToolHost.Content = card;
        ToolHost.Visibility = Visibility.Visible;
        PageScroll.Visibility = Visibility.Collapsed;
        SetDashboardSurface(true);
        if (!IsVisible)
            Show();
        Activate();
    }

    private void UnmountPanel(Window window)
    {
        if (hostedWindow != window)
            return;
        hostedWindow = null;
        ToolHost.Content = null;
        ToolHost.Visibility = Visibility.Collapsed;
        PageScroll.Visibility = Visibility.Visible;
        // What shows next (the page, another panel, or nothing) is decided once the panel has finished closing.
        SetDashboardSurface(false);
    }

    private void ForgetPanel(Window window)
    {
        UnmountPanel(window);
        hostedPanels.Remove(window);
    }

    /// <summary>The window a message box belongs to: the dashboard when the panel lives inside it.</summary>
    private Window DialogOwner(Window window) => IsHosted(window) ? this : window;

    private bool PanelActive(Window window) => IsHosted(window) ? IsActive && hostedWindow == window : window.IsActive;

    private void ActivatePanel(Window window)
    {
        if (IsHosted(window))
            Activate();
        else
            window.Activate();
    }
}
