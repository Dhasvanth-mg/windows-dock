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
    private readonly SolidColorBrush subdued = Brush("#C5BCD6");
    private readonly SolidColorBrush muted = Brush("#A69BB8");
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

    internal DashboardWindow(int controllerPid, string? initialPage, bool deferPage = false)
    {
        InitializeComponent();
        this.controllerPid = controllerPid;
        agentGrid = AgentGrid.Connect(controllerPid);
        ShowInTaskbar = controllerPid == 0;

        AddTab(DashboardPage.Home, 0xE80F, "Overview");
        AddTab(DashboardPage.Media, 0xE8D6, "Audio");
        AddTab(DashboardPage.Controls, 0xE9E9, "Controls");
        AddTab(DashboardPage.Calendar, 0xE787, "Calendar");
        AddTab(DashboardPage.Weather, 0xE753, "Weather");
        AddTab(DashboardPage.Agents, 0xE943, "Chats");
        AddTab(DashboardPage.Stats, 0xE9D9, "Stats");
        AddTab(DashboardPage.Shelf, 0xE8B7, "Files");
        AddTab(DashboardPage.Wallpapers, 0xE91B, "Wallpapers");
        AddTab(DashboardPage.Power, 0xE7E8, "Power");
        AddTab(DashboardPage.Settings, 0xE713, "Settings");
        HeaderActions.Children.Add(WidgetLauncherButton());
        HeaderActions.Children.Add(IconButton(0xE711, "Close dashboard", Dismiss));

        Shell.RenderTransform = revealOffset;
        ConfigureWidth();
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
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                revealOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(17, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            }
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
                Dismiss();
            }
        };
        PreviewMouseDown += (_, _) => agentPanel?.Close();
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
            foreach (var key in summaryKeys)
                key?.Dispose();
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
        LoadSavedWeather();
        if (!deferPage)
            ShowPage(page);
    }

    private void Dismiss()
    {
        agentPanel?.Close();
        if (controllerPid > 0)
            Hide();
        else
            Close();
    }

    private void AddTab(DashboardPage page, int icon, string label)
    {
        var button = MakeButton(IconLabel(label, icon), label, () => ShowPage(page));
        button.Padding = new Thickness(8, 4, 8, 4);
        button.Margin = new Thickness(0, 0, 3, 3);
        tabs[page] = button;
        DashboardTabs.Children.Add(button);
    }

    private void ShowPage(DashboardPage page)
    {
        if (currentPage != page)
            PageScroll.ScrollToTop();
        pageInitialized = true;
        currentPage = page;
        updatePage = null;
        foreach (var (key, button) in tabs)
        {
            button.Background = key == page ? Brush("#32C5A8FF") : Brushes.Transparent;
            button.BorderBrush = key == page ? Brush("#56C5A8FF") : Brushes.Transparent;
            button.Foreground = key == page ? purple : subdued;
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
        if (IsVisible && (currentPage is DashboardPage.Weather or DashboardPage.Home))
            _ = RefreshSavedWeatherAsync();
    }
    private void UpdateDashboard()
    {
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

    private void ConfigureWidth()
    {
        var screen = TaskbarScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(compactAgents ? 506 : 1060, screen.WorkingArea.Width / dpi.DpiScaleX - 24);
        MaxHeight = Math.Max(200, screen.Bounds.Height / dpi.DpiScaleY - 64);
        DashboardHeader.Visibility = compactAgents ? Visibility.Collapsed : Visibility.Visible;
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
            var bottom = screen.Bounds.Bottom - 51 * dpi.DpiScaleY;
            MoveWindowWithoutActivation(source.Handle, (int)x, (int)Math.Max(screen.Bounds.Top, bottom - height));
        }
        finally { positioning = false; }
    }

    internal void OpenMainDashboard()
    {
        agentPanel?.Close();
        if (!compactAgents && pageInitialized)
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

    private Border Card(UIElement child, SolidColorBrush? tint = null) => new()
    {
        Background = tint == null
            ? new LinearGradientBrush(Color.FromRgb(41, 35, 54), Color.FromRgb(29, 25, 40), 90)
            : new LinearGradientBrush(Color.FromArgb(33, tint.Color.R, tint.Color.G, tint.Color.B),
                Color.FromArgb(10, tint.Color.R, tint.Color.G, tint.Color.B), 65),
        BorderBrush = tint == null ? Brush("#28D4BDFE") : AgentArt.Wash(tint, 50), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(15), Padding = new Thickness(12), Child = child,
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
        var button = MakeButton(IconLabel(label, icon, tint), label, action);
        button.Background = AgentArt.Wash(tint, 35);
        button.BorderBrush = AgentArt.Wash(tint, 70);
        return button;
    }

    private Button IconButton(int icon, string label, Action? action = null)
    {
        var glyph = DockGlyph(icon, 13);
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
        var button = AccentButton("Widgets", 0xECAA, purple, ShowWidgets);
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
        var glyph = DockGlyph(icon, size * .5, tint);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * .29),
            Background = AgentArt.Wash(tint, 30), Child = glyph, VerticalAlignment = VerticalAlignment.Center
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
