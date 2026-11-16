using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WindowsDock;

public partial class DashboardWindow
{
    private sealed class OpenPanel(Window window)
    {
        internal Window Window { get; } = window;
        internal bool Locked { get; set; }
        internal Button? Switcher { get; set; }
    }

    private readonly List<OpenPanel> openPanels = [];
    private Button? dashboardPanelSwitcher;
    private bool dashboardOpen;
    private bool dashboardLocked;
    private bool codingPanelLocked;
    private bool closingHost;

    private OpenPanel TrackPanel(Window window)
    {
        var panel = new OpenPanel(window);
        openPanels.Add(panel);
        agentPanel = window;
        modalOpen = true;
        window.Closing += (_, args) =>
        {
            if (panel.Locked && !closingHost)
                args.Cancel = true;
        };
        return panel;
    }

    private void ParkToolPanel()
    {
        if (agentPanel is not { } window)
            return;
        agentPanel = null;
        modalOpen = false;
        var panel = openPanels.FirstOrDefault(item => item.Window == window);
        if (panel?.Locked == true)
            window.Hide();
        else
            window.Close();
    }

    private void OnToolPanelClosed(OpenPanel panel, FrameworkElement? returnFocus)
    {
        openPanels.Remove(panel);
        UpdatePanelSwitchers();
        if (agentPanel != panel.Window)
            return;
        agentPanel = null;
        modalOpen = false;
        Dispatcher.BeginInvoke(() =>
        {
            if (agentPanel != null || closed || closingHost || IsHitTestVisible)
                return;
            RestoreDockSurface();
            if (returnFocus?.IsVisible == true && agentPanel == null)
                returnFocus.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ShowTrackedPanel(OpenPanel panel)
    {
        if (agentPanel == panel.Window)
        {
            panel.Window.Activate();
            return;
        }
        ParkToolPanel();
        ShowDockPanelHost();
        agentPanel = panel.Window;
        modalOpen = true;
        panel.Window.Show();
        panel.Window.Activate();
        UpdatePanelSwitchers();
    }

    private void CloseDockSurface()
    {
        if (compactAgents)
        {
            if (codingPanelLocked)
                return;
            if (dashboardOpen)
            {
                ShowDockDashboard();
                return;
            }
            codingWorkspace?.Suspend();
            compactAgents = false;
            pageInitialized = false;
        }
        else
        {
            if (dashboardLocked)
                return;
            dashboardOpen = false;
            if (agentPanel is { } active && !openPanels.Any(panel => panel.Window == active && panel.Locked))
                ParkToolPanel();
            foreach (var item in openPanels.Where(panel => !panel.Locked).ToArray())
                item.Window.Close();
        }

        RestoreDockSurface();
    }

    private void RestoreDockSurface()
    {
        if (agentPanel != null)
            return;
        if (dashboardOpen || compactAgents)
        {
            SetDashboardSurface(true);
            if (!IsVisible)
                Show();
            Activate();
        }
        else if (openPanels.LastOrDefault(panel => panel.Locked) is { } previous)
            ShowTrackedPanel(previous);
        else if (codingPanelLocked)
        {
            ShowCodingWorkspace(compact: true);
            if (!IsVisible)
                Show();
            Activate();
        }
        else if (controllerPid > 0)
            Hide();
        else if (HasActiveWork)
            WindowState = WindowState.Minimized;
        else
        {
            closingHost = true;
            Close();
        }
    }

    private Button LockPanelButton(string title, Func<bool> locked, Action<bool> setLocked)
    {
        var button = IconButton(0xE72E, "Lock " + title);
        button.Click += (_, _) =>
        {
            setLocked(!locked());
            Update();
            UpdatePanelSwitchers();
        };
        Update();
        return button;

        void Update()
        {
            var active = locked();
            var label = (active ? "Unlock " : "Lock ") + title;
            ((TextBlock)button.Content).Text = char.ConvertFromUtf32(active ? 0xE72E : 0xE785);
            button.Foreground = active ? AssistantInk : AssistantSecondary;
            ((TextBlock)button.Content).Foreground = button.Foreground;
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetItemStatus(button, active ? "Locked" : "Unlocked");
        }
    }

    private Button MakePanelSwitcher()
    {
        var button = IconButton(0xE737, "Locked panels");
        button.Visibility = Visibility.Collapsed;
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu
            {
                PlacementTarget = button, Placement = PlacementMode.Top,
                Background = assistantInset, Foreground = AssistantInk
            };
            if (dashboardLocked && (agentPanel != null || compactAgents))
                Add("Dashboard", ShowDockDashboard);
            if (codingPanelLocked && (!compactAgents || agentPanel != null))
                Add("Coding chats", () => { ShowCodingWorkspace(compact: true); Activate(); });
            foreach (var panel in openPanels.Where(item => item.Locked && item.Window != agentPanel))
                Add(panel.Window.Title, () => ShowTrackedPanel(panel));
            if (menu.Items.Count > 0)
                menu.IsOpen = true;

            void Add(string title, Action show)
            {
                var item = new MenuItem { Header = title };
                item.Click += (_, _) => show();
                menu.Items.Add(item);
            }
        };
        return button;
    }

    private void UpdatePanelSwitchers()
    {
        var otherTool = openPanels.Any(panel => panel.Locked && panel.Window != agentPanel);
        if (dashboardPanelSwitcher != null)
            dashboardPanelSwitcher.Visibility = codingPanelLocked || otherTool ? Visibility.Visible : Visibility.Collapsed;
        codingWorkspace?.ShowLockedPanelSwitcher(dashboardLocked || otherTool);
        foreach (var panel in openPanels)
            if (panel.Switcher != null)
                panel.Switcher.Visibility = dashboardLocked || codingPanelLocked ||
                    openPanels.Any(other => other.Locked && other != panel) ? Visibility.Visible : Visibility.Collapsed;
    }
}
