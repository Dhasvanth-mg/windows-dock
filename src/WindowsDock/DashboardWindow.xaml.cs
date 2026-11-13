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
    private readonly SolidColorBrush subdued = Brush("#B1B3BC");
    private readonly SolidColorBrush muted = Brush("#898D99");
    private readonly SolidColorBrush accent = Brush("#6BB1FF");
    private readonly SolidColorBrush green = Brush("#58DA9B");
    private readonly SolidColorBrush purple = Brush("#A593FF");
    private readonly SolidColorBrush amber = Brush("#FFBA57");
    private readonly TranslateTransform revealOffset = new();
    private DashboardPage currentPage;
    private Action? updatePage;
    private bool modalOpen;
    private bool closed;
    private bool positioning;
    private bool compactAgents;
    private long lastReveal;

    public DashboardWindow() : this(0, null) { }

    internal DashboardWindow(int controllerPid, string? initialPage)
    {
        InitializeComponent();
        this.controllerPid = controllerPid;
        agentGrid = AgentGrid.Connect(controllerPid);
        ShowInTaskbar = controllerPid == 0;

        AddTab(PrimaryTabs, DashboardPage.Home, 0xE80F, "Overview");
        AddTab(PrimaryTabs, DashboardPage.Media, 0xE8D6, "Music and mixer");
        AddTab(PrimaryTabs, DashboardPage.Controls, 0xE9E9, "Quick controls");
        AddTab(PrimaryTabs, DashboardPage.Calendar, 0xE787, "Calendar and focus");
        AddTab(PrimaryTabs, DashboardPage.Weather, 0xE753, "Weather");
        AddTab(PrimaryTabs, DashboardPage.Agents, 0xE943, "Coding chats");
        AddTab(SecondaryTabs, DashboardPage.Stats, 0xE9D9, "System stats");
        AddTab(SecondaryTabs, DashboardPage.Shelf, 0xE8B7, "Files");
        AddTab(SecondaryTabs, DashboardPage.Wallpapers, 0xE91B, "Wallpapers");
        AddTab(SecondaryTabs, DashboardPage.Power, 0xE7E8, "Power");
        AddTab(SecondaryTabs, DashboardPage.Settings, 0xE713, "Dashboard settings");
        SecondaryTabs.Children.Add(IconButton(0xE711, "Close dashboard", Dismiss));

        Shell.RenderTransform = revealOffset;
        SourceInitialized += (_, _) => ConfigureWidth();
        Loaded += (_, _) =>
        {
            PositionAtTaskbar();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                ticker.Stop();
                return;
            }
            lastReveal = Environment.TickCount64;
            UpdateDashboard();
            if (currentPage is DashboardPage.Weather or DashboardPage.Home)
                _ = RefreshSavedWeatherAsync();
            ticker.Start();
            PositionAtTaskbar();
            if (SystemParameters.ClientAreaAnimation)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                revealOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(17, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            }
        };
        SizeChanged += (_, _) => PositionAtTaskbar();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { ConfigureWidth(); PositionAtTaskbar(); });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                Dismiss();
            }
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!closed && IsVisible && !IsActive && !modalOpen && Mouse.Captured == null &&
                Environment.TickCount64 - lastReveal > 200)
                Dismiss();
        });
        Closed += (_, _) =>
        {
            closed = true;
            ticker.Stop();
            agentTicker.Stop();
            lifetime.Cancel();
            lifetime.Dispose();
            agentGrid?.Dispose();
        };
        ticker.Tick += (_, _) => UpdateDashboard();
        if (agentGrid != null)
        {
            agentTicker.Tick += (_, _) => _ = RefreshAgentsAsync();
            agentTicker.Start();
            _ = RefreshAgentsAsync();
        }
        InitializeFocus();
        var page = Enum.TryParse<DashboardPage>(initialPage, true, out var initial) && Enum.IsDefined(initial)
            ? initial : DashboardPage.Home;
        ShowPage(page);
        LoadSavedWeather();
    }

    private void Dismiss()
    {
        if (controllerPid > 0)
            Hide();
        else
            Close();
    }

    private void AddTab(Panel strip, DashboardPage page, int icon, string label)
    {
        var button = IconButton(icon, label, () => ShowPage(page));
        tabs[page] = button;
        strip.Children.Add(button);
    }

    private void ShowPage(DashboardPage page)
    {
        currentPage = page;
        updatePage = null;
        foreach (var (key, button) in tabs)
        {
            button.Background = key == page ? Brush("#38FFFFFF") : Brushes.Transparent;
            button.Foreground = key == page ? Brushes.White : subdued;
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

        UpdateDashboard();
        if (IsVisible && currentPage is DashboardPage.Weather or DashboardPage.Home)
            _ = RefreshSavedWeatherAsync();
    }
    private void UpdateDashboard()
    {
        var now = DateTime.Now;
        HeaderClock.Text = now.ToString("t") + "   " + now.ToString("dddd, MMMM d");
        var pending = AgentApprovals.Read();
        pendingApprovals = pending;
        RefreshVisibleUsage();
        if (currentPage != DashboardPage.Agents)
            tabs[DashboardPage.Agents].Background = pending.Length > 0 ? Brush("#65431E") : Brushes.Transparent;
        updatePage?.Invoke();
    }

    private void ConfigureWidth()
    {
        var screen = TaskbarScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(compactAgents ? 506 : 1060, screen.WorkingArea.Width / dpi.DpiScaleX - 24);
        DashboardHeader.Visibility = compactAgents ? Visibility.Collapsed : Visibility.Visible;
        PanelLayout.RowDefinitions[0].Height = new GridLength(compactAgents ? 0 : 35);
        var narrow = Width < 760;
        HeaderClock.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
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
            var bottom = compactAgents ? screen.Bounds.Bottom - 51 * dpi.DpiScaleY : screen.Bounds.Bottom - 5 * dpi.DpiScaleY;
            MoveWindowWithoutActivation(source.Handle, (int)x, (int)Math.Max(screen.Bounds.Top, bottom - height));
        }
        finally { positioning = false; }
    }

    internal void OpenMainDashboard()
    {
        if (!compactAgents)
            return;
        compactAgents = false;
        ConfigureWidth();
        ShowPage(DashboardPage.Home);
    }

    private static Forms.Screen TaskbarScreen()
    {
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        return taskbar != 0 ? Forms.Screen.FromHandle(taskbar) : Forms.Screen.PrimaryScreen!;
    }

    private static void MoveWindowWithoutActivation(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, 0x15);

    private Border Card(UIElement child) => new()
    {
        Background = new LinearGradientBrush(Color.FromRgb(34, 35, 39), Color.FromRgb(23, 24, 27), 90),
        BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(13), Padding = new Thickness(12), Child = child,
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
        element.Margin = new Thickness(column == 0 ? 0 : 7, 0, 0, 0);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private Button ActionButton(string label, Action action) => MakeButton(label, label, action);

    private Button IconButton(int icon, string label, Action action)
    {
        var glyph = new TextBlock
        {
            Text = char.ConvertFromUtf32(icon), FontSize = 13,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
        };
        var button = MakeButton(glyph, label, action);
        button.Padding = new Thickness(0);
        button.Width = 28;
        button.Height = 26;
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Margin = new Thickness(1, 0, 1, 0);
        return button;
    }

    private Button MakeButton(object content, string name, Action action)
    {
        var button = new Button { Content = content, Style = (Style)FindResource("Chip"), ToolTip = name };
        AutomationProperties.SetName(button, name);
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
