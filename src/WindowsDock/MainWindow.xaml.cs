using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WindowsDock;

public partial class MainWindow : Window
{
    private readonly bool safeMode = Environment.GetCommandLineArgs().Contains("--safe-mode");
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly ShellIntegration shell;
    private DockSettings settings;
    private IReadOnlyList<AppWindow> windows = [];
    private TextBlock? clock;
    private Window? studio;
    private nint priorForeground;
    private bool refreshPending;

    public MainWindow()
    {
        InitializeComponent();
        settings = DockSettings.Load(safeMode);
        shell = new ShellIntegration(RefreshWindows);
        Loaded += (_, _) => RefreshWindows();
        SizeChanged += (_, _) => PositionDock();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionDock);
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        PreviewMouseDown += (_, _) => priorForeground = ShellIntegration.GetForegroundWindow();
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
        InitializeDesktopControls();
        Closed += (_, _) =>
        {
            clockTimer.Stop();
            shell.Dispose();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            studio?.Close();
        };
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(PositionDock);
    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(PositionDock);

    private void PositionDock()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
            return;
        var area = Forms.Screen.FromHandle(source.Handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        MaxWidth = Math.Max(160, area.Width / dpi.DpiScaleX - 16);
        var x = area.Left + (area.Width - ActualWidth * dpi.DpiScaleX) / 2;
        var y = area.Bottom - (ActualHeight + settings.Elevation) * dpi.DpiScaleY;
        SetWindowPos(source.Handle, 0, (int)x, (int)y, 0, 0, 0x15);
    }

    private void RefreshWindows()
    {
        var next = ShellIntegration.Windows();
        if (windows.SequenceEqual(next) && Items.Children.Count > 0)
            return;
        windows = next;
        RequestRender();
    }

    private void RequestRender()
    {
        if (ContextMenuService.GetContextMenu(this)?.IsOpen == true || HasOpenMenu() ||
            Items.IsMouseOver || Items.IsKeyboardFocusWithin)
        {
            if (!refreshPending)
            {
                refreshPending = true;
                MouseLeave += RetryRender;
                LostKeyboardFocus += RetryRender;
            }
            return;
        }
        Render();
    }

    private void RetryRender(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(RequestRender);

    private int RenderedIconSize => Math.Max(14, settings.IconSize - 12);

    private void Render()
    {
        refreshPending = false;
        MouseLeave -= RetryRender;
        LostKeyboardFocus -= RetryRender;
        Items.Children.Clear();
        clock = null;
        widgetReadouts.Clear();
        Surface.CornerRadius = new CornerRadius(settings.Radius * 0.55);
        Surface.Background = new LinearGradientBrush(
            Color.FromArgb((byte)(settings.Opacity * 255 / 100), settings.Tint == "Ocean" ? (byte)16 : settings.Tint == "Violet" ? (byte)43 : (byte)24, 29, 29),
            Color.FromArgb((byte)(settings.Opacity * 255 / 100), 13, 17, 18), 90);

        AddButton("Start", StartMark(), OpenStart);
        AddButton("Search files", Glyph(0xE721), () => Launch("search-ms:"));
        RenderLayout("Before pins");
        foreach (var pin in settings.Pins)
        {
            AddApplication(pin.Name, pin.Target,
                windows.Where(window => string.Equals(window.Path, pin.Target, StringComparison.OrdinalIgnoreCase)).ToArray(), pin);
        }
        RenderLayout("After pins");
        foreach (var group in windows.Where(window => !settings.Pins.Any(pin =>
                     string.Equals(pin.Target, window.Path, StringComparison.OrdinalIgnoreCase)))
                     .GroupBy(window => string.IsNullOrEmpty(window.Path) ? window.Handle.ToString() : window.Path,
                         StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            AddApplication(first.Title, first.Path, group.ToArray(), null);
        }
        AddDivider();
        AddOverflow();
        RenderLayout("Before widgets");
        RenderWidgets();
        if (settings.SystemLinks)
        {
            AddStatusGroup();
        }
        RenderLayout("Before clock");
        if (settings.Clock)
        {
            AddDivider();
            clock = new TextBlock { TextAlignment = TextAlignment.Center, FontSize = 10 };
            AddButton("Date and time settings", clock, () => Launch("ms-settings:dateandtime"));
            UpdateClock();
        }
        DockActions.Children.Clear();
        var options = AddButton("Dock settings", Glyph(0xE713), ShowStudio, DockActions);
        var menu = new ContextMenu();
        AddMenuItem(menu, "Exit Windows Dock", Close);
        AttachMenu(options, menu);
        PositionDock();
    }

    private void UpdateClock()
    {
        if (clock != null)
        {
            var now = DateTime.Now;
            clock.Text = now.ToString(settings.Use24Hour ? "HH:mm" : "t") + "\n" + now.ToString("M/d/yyyy");
        }
        UpdateWidgets();
    }

    private void AddDivider() => Items.Children.Add(new Border
    {
        Width = 1, Height = RenderedIconSize, Margin = new Thickness(4, 0, 4, 0),
        Background = new SolidColorBrush(Color.FromArgb(88, 220, 230, 249))
    });

    private Button AddButton(string label, object content, Action action, Panel? destination = null)
    {
        var button = new Button
        {
            Content = content, ToolTip = label, MinWidth = RenderedIconSize + 8,
            Height = RenderedIconSize + 12, Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(settings.Spacing, 0, settings.Spacing, 0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, Foreground = Brushes.White,
            FontSize = content is string ? 16 : 12, Cursor = Cursors.Hand
        };
        AutomationProperties.SetName(button, label);
        var template = new ControlTemplate(typeof(Button));
        var background = new FrameworkElementFactory(typeof(Border));
        background.Name = "Tile";
        background.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        background.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        background.AppendChild(presenter);
        template.VisualTree = background;
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var highlight = new Trigger { Property = property, Value = true };
            highlight.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(60, 204, 222, 252)), "Tile"));
            template.Triggers.Add(highlight);
        }
        button.Template = template;
        button.Click += (_, _) => action();
        (destination ?? Items).Children.Add(button);
        return button;
    }

    private void AddApplication(string name, string target, IReadOnlyList<AppWindow> group, Pin? pin)
    {
        var tile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var icon = GetIcon(target);
        tile.Children.Add(icon is null
            ? new TextBlock { Text = name[..1].ToUpperInvariant(), FontSize = RenderedIconSize * 0.72,
                Height = RenderedIconSize, TextAlignment = TextAlignment.Center }
            : new Image { Source = icon, Width = RenderedIconSize, Height = RenderedIconSize });
        tile.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Height = 2, Width = group.Count > 1 ? 8 : 4, Margin = new Thickness(0, 2, 0, 0),
            Fill = group.Count > 0 ? Brushes.White : Brushes.Transparent
        });
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var button = AddButton(name, tile, () =>
        {
            if (group.Count > 1)
                menu.IsOpen = true;
            else if (group.Count == 1)
                Switch(group[0]);
            else
                Launch(target);
        });
        foreach (var window in group)
            AddMenuItem(menu, window.Title, () => Switch(window));
        if (File.Exists(target))
            AddMenuItem(menu, "Launch new window", () => Launch(target));
        if (pin != null)
            AddMenuItem(menu, "Unpin", () => Apply(settings with { Pins = settings.Pins.Where(item => item != pin).ToArray() }));
        else if (File.Exists(target) && settings.Pins.Length < 24)
            AddMenuItem(menu, "Pin to dock", () => Apply(settings with
            {
                Pins = [.. settings.Pins, new Pin(Path.GetFileNameWithoutExtension(target), target)]
            }));
        menu.PlacementTarget = button;
        AttachMenu(button, menu);
    }

    private void AttachMenu(Button button, ContextMenu menu)
    {
        ConfigureCenteredMenu(menu);
        button.ContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (refreshPending)
                Dispatcher.BeginInvoke(RequestRender);
        };
    }

    private static void AddMenuItem(ContextMenu menu, string label, Action action)
    {
        var item = new MenuItem { Header = label.Replace("_", "__") };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private ImageSource? GetIcon(string target)
    {
        if (string.IsNullOrEmpty(target))
            return null;
        if (icons.TryGetValue(target, out var existing))
            return existing;
        ImageSource? source = null;
        try
        {
            if (File.Exists(target))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(target);
                if (icon != null)
                {
                    source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                }
            }
        }
        catch (Exception error) when (error is IOException or ArgumentException or Win32Exception) { }
        if (icons.Count >= 128)
            icons.Clear();
        icons[target] = source;
        return source;
    }

    private void Switch(AppWindow window)
    {
        if (!ShellIntegration.Switch(window.Handle, priorForeground))
            MessageBox.Show("Windows could not activate this window.", "Windows Dock");
        priorForeground = 0;
    }

    private static void Launch(string target)
    {
        try { ShellIntegration.Launch(target); }
        catch (Win32Exception error) { MessageBox.Show(error.Message, "Windows Dock"); }
    }

    private void Apply(DockSettings next)
    {
        if (!safeMode)
        {
            try { next.Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(error.Message, "Windows Dock could not save settings");
                return;
            }
        }
        settings = next;
        Render();
    }

    private void ShowStudio()
    {
        if (studio != null)
        {
            studio.Activate();
            return;
        }

        studio = CreateStudio();
        studio.Closed += (_, _) => studio = null;
        studio.Show();
    }

    [DllImport("user32")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
