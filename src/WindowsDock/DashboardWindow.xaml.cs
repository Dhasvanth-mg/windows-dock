using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WindowsDock;

public partial class DashboardWindow : Window
{
    private readonly Dictionary<DashboardPage, Button> tabs = new();
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer agentTicker = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly int controllerPid;
    private readonly bool releaseWhenIdle;
    private readonly bool trayMode;
    private readonly DispatcherTimer idleExit = new() { Interval = TimeSpan.FromSeconds(20) };
    private SolidColorBrush subdued => AssistantSecondary;
    private SolidColorBrush muted => AssistantSecondary;
    private readonly SolidColorBrush accent = Brush("#9CC9FF");
    private readonly SolidColorBrush green = Brush("#A1EDC5");
    private readonly SolidColorBrush purple = Brush("#CAB4FF");
    private readonly SolidColorBrush amber = Brush("#FFD187");
    private readonly SolidColorBrush pink = Brush("#F4A6D4");
    private readonly TranslateTransform revealOffset = new();
    private DashboardPage currentPage;
    private Action? updatePage;
    private bool modalOpen;
    private bool closed;
    private bool positioning;
    private bool compactAgents;
    private bool pageInitialized;
    private long lastReveal;
    private bool WideDashboard => Width >= 840;

    public DashboardWindow() : this(0, null) { }

    internal DashboardWindow(int controllerPid, string? initialPage, bool deferPage = false, bool releaseWhenIdle = false,
        bool trayMode = false)
    {
        InitializeComponent();
        ApplyDashboardResources(this);
        this.controllerPid = controllerPid;
        this.releaseWhenIdle = releaseWhenIdle;
        this.trayMode = trayMode;
        idleExit.Tick += (_, _) =>
        {
            idleExit.Stop();
            if (!IsVisible && !HasActiveWork)
                Application.Current.Shutdown();
        };
        if (releaseWhenIdle && deferPage)
            idleExit.Start();
        agentGrid = AgentGrid.Connect(controllerPid);
        ShowInTaskbar = controllerPid == 0;

        AddTab(DashboardPage.Home, "Overview");
        AddTab(DashboardPage.Media, "Audio");
        AddTab(DashboardPage.Controls, "Controls");
        AddTab(DashboardPage.Calendar, "Calendar");
        AddTab(DashboardPage.Weather, "Weather");
        AddTab(DashboardPage.Agents, "Chats");
        AddTab(DashboardPage.Stats, "Stats");
        AddTab(DashboardPage.Shelf, "Files");
        AddTab(DashboardPage.Wallpapers, "Wallpapers");
        AddTab(DashboardPage.Power, "Power");
        AddTab(DashboardPage.Settings, "Settings");
        HeaderMark.Content = DockTaskbar.IconBadge(30);
        HeaderActions.Children.Add(WidgetLauncherButton());
        dashboardPanelSwitcher = MakePanelSwitcher();
        HeaderActions.Children.Add(dashboardPanelSwitcher);
        HeaderActions.Children.Add(LockPanelButton("Dashboard", () => dashboardLocked, value => dashboardLocked = value));
        HeaderActions.Children.Add(IconButton(0xE711, "Close dashboard", Dismiss));

        Shell.RenderTransform = revealOffset;
        ConfigureWidth();
        SourceInitialized += (_, _) =>
        {
            ConfigureWidth();
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(HandleDashboardWheel);
        };
        Loaded += (_, _) =>
        {
            PositionAtTaskbar();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                codingWorkspace?.Suspend();
                ReportDockVisibility(false);
                ticker.Stop();
                agentTicker.Stop();
                if (releaseWhenIdle && !HasActiveWork)
                    idleExit.Start();
                return;
            }
            if (IsHitTestVisible && !compactAgents)
                dashboardOpen = true;
            idleExit.Stop();
            lastReveal = Environment.TickCount64;
            Dispatcher.InvokeAsync(() =>
            {
                if (closed || !IsVisible)
                    return;
                ReportDockVisibility(true);
                UpdateDashboard();
                if (agentGrid != null)
                    _ = RefreshAgentsAsync();
                if (currentPage is DashboardPage.Weather or DashboardPage.Home)
                    _ = RefreshSavedWeatherAsync();
            }, DispatcherPriority.ApplicationIdle);
            ticker.Start();
            if (agentGrid != null)
                agentTicker.Start();
            PositionAtTaskbar();
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(80)));
        };
        SizeChanged += (_, _) => PositionAtTaskbar();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            var wasWide = WideDashboard;
            ConfigureWidth();
            if (pageInitialized && !compactAgents && wasWide != WideDashboard)
                ShowPage(currentPage);
            PositionAtTaskbar();
        });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowCommandPalette();
                return;
            }
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                if (OpenComboBox(e.OriginalSource) is { } combo)
                    combo.IsDropDownOpen = false;
                else if (agentPanel != null)
                    agentPanel.Close();
                else if (codingWorkspace?.Back() == true)
                    return;
                else
                    Dismiss();
            }
        };
        PreviewMouseDown += (_, _) => agentPanel?.Close();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (controllerPid == 0 && !closed && IsVisible && !IsActive && !modalOpen && Mouse.Captured == null &&
                Environment.TickCount64 - lastReveal > 200)
                Dismiss();
        });
        Closing += (_, args) =>
        {
            if (closingHost)
                return;
            if (dashboardLocked || codingPanelLocked || openPanels.Any(panel => panel.Locked) || HasActiveWork)
            {
                args.Cancel = true;
                // The window manager is already closing this HWND. Resolve the logical close after it returns.
                Dispatcher.BeginInvoke(CloseDockSurface);
            }
            else
                closingHost = true;
        };
        Closed += (_, _) =>
        {
            closed = true;
            ReportDockVisibility(false);
            ticker.Stop();
            agentTicker.Stop();
            idleExit.Stop();
            codingWorkspace?.Dispose();
            lifetime.Cancel();
            lifetime.Dispose();
            agentGrid?.Dispose();
            foreach (var key in summaryKeys)
                key?.Dispose();
            foreach (var key in assistantKeys)
                key?.Dispose();
        };
        ticker.Tick += (_, _) => UpdateDashboard();
        if (agentGrid != null)
            agentTicker.Tick += (_, _) => _ = RefreshAgentsAsync();
        InitializeFocus();
        var page = Enum.TryParse<DashboardPage>(initialPage, true, out var initial) && Enum.IsDefined(initial)
            ? initial : DashboardPage.Home;
        LoadSavedWeather();
        if (!deferPage)
            ShowPage(page);
    }

    private void Dismiss() => CloseDockSurface();

    internal void ShowDockPanelHost()
    {
        SetDashboardSurface(false);
        if (!IsVisible)
            Show();
    }

    internal void ToggleDockDashboard()
    {
        if (DashboardReceivesGestures)
        {
            Dismiss();
            return;
        }
        ShowDockDashboard();
    }

    internal void ShowDockDashboard()
    {
        OpenMainDashboard();
        if (!IsVisible)
            Show();
        Activate();
    }

    private void SetDashboardSurface(bool visible)
    {
        Opacity = visible ? 1 : 0;
        IsHitTestVisible = visible;
    }

    private void ReportDockVisibility(bool visible)
    {
        if (controllerPid > 0)
            DockTaskbar.ReportVisibility(controllerPid, visible);
    }

    private void AddTab(DashboardPage page, string label)
    {
        var button = MakeButton(label, label, () => ShowPage(page));
        button.Style = (Style)FindResource("AssistantPickerButton");
        button.Padding = new Thickness(9, 5, 9, 6);
        button.Margin = new Thickness(0, 0, 3, 3);
        button.MinHeight = 30;
        button.FontSize = 13;
        ToolTipService.SetIsEnabled(button, false);
        tabs[page] = button;
        DashboardTabs.Children.Add(button);
    }

    private void ShowPage(DashboardPage page)
    {
        if (currentPage != page)
            PageScroll.ScrollToTop();
        pageInitialized = true;
        currentPage = page;
        if (page != DashboardPage.Agents && !codingPanelLocked)
        {
            codingWorkspace?.Dispose();
            codingWorkspace = null;
        }
        PageScroll.VerticalScrollBarVisibility = page == DashboardPage.Agents ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        PageScroll.PanningMode = page == DashboardPage.Agents ? PanningMode.None : PanningMode.VerticalOnly;
        if (!compactAgents)
            lastDashboardPage = page;
        updatePage = null;
        homeFavorites = null;
        foreach (var (key, button) in tabs)
        {
            button.Tag = key == page ? "Selected" : null;
            button.Foreground = key == page ? AssistantInk : AssistantSecondary;
            button.FontWeight = key == page ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(button, key == page ? "Current tab" : "");
        }

        PageHost.Content = page switch
        {
            DashboardPage.Home => HomePage(),
            DashboardPage.Media => MediaPage(),
            DashboardPage.Controls => ControlsPage(),
            DashboardPage.Calendar => CalendarPage(),
            DashboardPage.Weather => WeatherPage(),
            DashboardPage.Agents => AgentsPage(),
            DashboardPage.Stats => StatsPage(),
            DashboardPage.Shelf => ShelfPage(),
            DashboardPage.Wallpapers => WallpapersPage(),
            DashboardPage.Power => PowerPage(),
            _ => SettingsPage()
        };

        if (IsVisible)
        {
            UpdateDashboard();
            if (currentPage is DashboardPage.Weather or DashboardPage.Home)
                _ = RefreshSavedWeatherAsync();
        }
    }
    private void UpdateDashboard()
    {
        UpdateCompletionCue();
        RefreshFocusState();
        updateAudioWidget?.Invoke();
        updateFocusWidget?.Invoke();
        var now = DateTime.Now;
        HeaderTime.Text = now.ToString("t");
        HeaderDate.Text = now.ToString("ddd, MMM d");
        var pending = AgentApprovals.Read();
        pendingApprovals = pending;
        RefreshVisibleUsage();
        if (currentPage != DashboardPage.Agents)
        {
            var chats = tabs[DashboardPage.Agents];
            chats.Background = pending.Length > 0 ? AgentArt.Wash(amber, 46) : Brushes.Transparent;
            chats.BorderBrush = pending.Length > 0 ? AgentArt.Wash(amber, 75) : Brushes.Transparent;
            chats.ToolTip = pending.Length > 0 ? "Coding chats · approval waiting" : "Coding chats";
        }
        updatePage?.Invoke();
    }

    // Dashboard and Chats share one height, so switching between them only changes the width and the window never
    // jumps vertically. It is tall enough for Overview with its favorites row and live media and weather text, which
    // are filled in after layout and cannot be measured before the window is shown. Other pages scroll inside it.
    private const double PanelHeight = 454;

    private void ConfigureWidth()
    {
        var screen = TaskbarScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(compactAgents ? 764 : 896, screen.WorkingArea.Width / dpi.DpiScaleX - 24);
        MaxHeight = Math.Max(200, (DockTaskbar.AnchorTop(screen.Bounds, dpi.DpiScaleY) - screen.Bounds.Top) /
            dpi.DpiScaleY - 12);
        Height = Math.Min(PanelHeight, MaxHeight);
        DashboardHeader.Visibility = compactAgents ? Visibility.Collapsed : Visibility.Visible;
        if (compactAgents)
            DockNavigationHost.Content ??= DockNavigation(this, DockSection.Chats);
        DockNavigationHost.Visibility = compactAgents ? Visibility.Visible : Visibility.Collapsed;
        HeaderClock.Visibility = Width < 580 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PositionAtTaskbar()
    {
        if (positioning || PresentationSource.FromVisual(this) is not HwndSource source)
            return;

        positioning = true;
        try
        {
            var screen = TaskbarScreen();
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = ActualWidth * dpi.DpiScaleX;
            var height = ActualHeight * dpi.DpiScaleY;
            var x = screen.Bounds.Left + (screen.Bounds.Width - width) / 2;
            var bottom = DockTaskbar.AnchorTop(screen.Bounds, dpi.DpiScaleY) - 6 * dpi.DpiScaleY;
            MoveWindowWithoutActivation(source.Handle, (int)x, (int)Math.Max(screen.Bounds.Top, bottom - height));
        }
        finally { positioning = false; }
    }

    internal void OpenMainDashboard()
    {
        ParkToolPanel();
        dashboardOpen = true;
        SetDashboardSurface(true);
        UpdatePanelSwitchers();
        if (!compactAgents && pageInitialized)
        {
            if (currentPage == DashboardPage.Home)
                RenderHomeFavorites();
            return;
        }
        compactAgents = false;
        ConfigureWidth();
        ShowPage(lastDashboardPage);
    }

    private static Forms.Screen TaskbarScreen()
    {
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        return taskbar != 0 ? Forms.Screen.FromHandle(taskbar) : Forms.Screen.PrimaryScreen!;
    }

    private static void MoveWindowWithoutActivation(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, 0x15);

    private Border Card(UIElement child, SolidColorBrush? tint = null) => new()
    {
        Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : assistantInset,
        BorderBrush = tint == null || SystemParameters.HighContrast ? AssistantRule : AgentArt.Wash(tint, 34),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = child,
        ClipToBounds = true
    };

    private static Grid Columns(params double[] weights)
    {
        var grid = new Grid();
        foreach (var weight in weights)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
        return grid;
    }

    private static void Put(Grid grid, FrameworkElement element, int column)
    {
        Put(grid, element, 0, column);
    }

    private static void Put(Grid grid, FrameworkElement element, int row, int column, int span = 1)
    {
        while (row >= grid.RowDefinitions.Count && row > 0)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        element.Margin = new Thickness(column == 0 ? 0 : 7, row == 0 ? 0 : 7, 0, 0);
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        grid.Children.Add(element);
    }

    private Button ActionButton(string label, Action? action = null) => MakeButton(label, label, action);

    private Button AccentButton(string label, int icon, SolidColorBrush tint, Action? action = null)
    {
        return MakeButton(IconLabel(label, icon, SystemParameters.HighContrast ? AssistantInk : tint), label, action);
    }

    private Button IconButton(int icon, string label, Action? action = null)
    {
        var glyph = DockGlyph(icon, 13, AssistantInk);
        var button = MakeButton(glyph, label, action);
        button.Padding = new Thickness(0);
        button.Width = 28;
        button.Height = 26;
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Margin = new Thickness(1, 0, 1, 0);
        return button;
    }

    private Button WidgetLauncherButton()
    {
        var button = AccentButton("Widgets", 0xECAA, AssistantSecondary, ShowWidgets);
        button.ToolTip = "Open Widgets or scroll up at the top of Dashboard";
        button.Padding = new Thickness(8, 4, 8, 4);
        button.Margin = new Thickness(3, 0, 3, 0);
        return button;
    }

    private static TextBlock DockGlyph(int icon, double size, Brush? color = null)
    {
        var glyph = new TextBlock
        {
            Text = char.ConvertFromUtf32(icon), FontSize = size, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
        };
        if (color != null)
            glyph.Foreground = color;
        return glyph;
    }

    private static StackPanel IconLabel(string label, int icon, Brush? color = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = DockGlyph(icon, 12, color);
        glyph.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(glyph);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private Border GlyphBadge(int icon, SolidColorBrush tint, double size)
    {
        var glyph = DockGlyph(icon, size * .6, SystemParameters.HighContrast ? AssistantInk : tint);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Width = size * .7, Height = size * .72, Child = glyph, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private DockPanel CardHeading(string title, int icon, SolidColorBrush tint, UIElement? action = null, string? detail = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (action != null)
        {
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
        }
        var badge = GlyphBadge(icon, tint, 28);
        badge.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(badge);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(title, 13, strong: true));
        if (!string.IsNullOrEmpty(detail))
            labels.Children.Add(Text(detail, 11, subdued));
        row.Children.Add(labels);
        return row;
    }

    private Button MakeButton(object content, string name, Action? action)
    {
        var button = new Button { Content = content, Style = (Style)FindResource("Chip"), ToolTip = name };
        AutomationProperties.SetName(button, name);
        if (action != null)
            button.Click += (_, _) => action();
        return button;
    }

    private TextBlock Text(string value, double size = 12, Brush? color = null, bool strong = false) => new()
    {
        Text = value, FontSize = size, Foreground = color ?? Foreground,
        FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private Slider Range(string label, double maximum, double value)
    {
        var range = new Slider { Minimum = 0, Maximum = maximum, Value = value,
            Style = (Style)FindResource("ValueSlider") };
        AutomationProperties.SetName(range, label);
        return range;
    }

    private TextBox Field(string name, int maxLength)
    {
        var field = new TextBox { Style = (Style)FindResource("Field"), MaxLength = maxLength };
        AutomationProperties.SetName(field, name);
        return field;
    }

    private void OpenTarget(string target)
    {
        try { ShellIntegration.Launch(target); }
        catch (Win32Exception) { ShowOwnedMessage("Windows could not open this item."); }
    }

    private void ShowOwnedMessage(string message)
    {
        var wasModal = modalOpen;
        modalOpen = true;
        try { MessageBox.Show(this, message, "Windows Dock"); }
        finally { modalOpen = wasModal; }
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    private enum DashboardPage { Home, Media, Controls, Calendar, Weather, Agents, Stats, Shelf, Wallpapers, Power, Settings }
}
