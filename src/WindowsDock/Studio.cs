using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace WindowsDock;

public partial class MainWindow
{
    private static readonly Brush StudioBackground = new SolidColorBrush(Color.FromRgb(19, 21, 26));
    private static readonly Brush StudioPanel = new SolidColorBrush(Color.FromRgb(32, 36, 43));
    private static readonly Brush StudioAccent = new SolidColorBrush(Color.FromRgb(86, 220, 255));

    private Window CreateStudio()
    {
        var frame = new DockPanel { Background = StudioBackground, LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(28, 22, 28, 16) };
        header.Children.Add(new TextBlock { Text = "WINDOWS DOCK", Foreground = StudioAccent, FontSize = 13, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Customization Studio", FontSize = 32, FontWeight = FontWeights.SemiBold });
        if (safeMode)
            header.Children.Add(new TextBlock { Text = "Safe Mode · Session changes only" });
        DockPanel.SetDock(header, Dock.Top);
        frame.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        frame.Children.Add(body);
        var navigation = new StackPanel { Margin = new Thickness(18, 4, 14, 20) };
        body.Children.Add(navigation);
        var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 24) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var card = new Border { Background = StudioPanel, CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 4, 24, 24), Child = scroll };
        Grid.SetColumn(card, 1);
        body.Children.Add(card);

        AddNavigation("Appearance", Appearance);
        AddNavigation("Behavior", Behavior);
        AddNavigation("Widgets", Widgets);
        AddNavigation("Applications", Applications);
        AddNavigation("Layout", () => EditLayout(panel));
        Appearance();

        return new Window
        {
            Title = "Windows Dock Customization Studio", Content = frame,
            Background = StudioBackground, Foreground = Brushes.White,
            Width = 820, Height = 630, MinWidth = 560, MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        void AddNavigation(string label, Action open)
        {
            var button = StudioButton(label, open);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            navigation.Children.Add(button);
        }

        void Page(string title, string description)
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = title, Foreground = StudioAccent, FontSize = 23, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 19) });
        }

        void Appearance()
        {
            Page("Appearance", "Changes appear immediately on the dock.");
            Slider("Icon size", 20, 64, settings.IconSize, value => settings with { IconSize = value });
            Slider("Icon spacing", 0, 10, settings.Spacing, value => settings with { Spacing = value });
            Slider("Background opacity", 25, 100, settings.Opacity, value => settings with { Opacity = value });
            Slider("Corner radius", 0, 36, settings.Radius, value => settings with { Radius = value });
            Slider("Elevation", 0, 80, settings.Elevation, value => settings with { Elevation = value });
            var tints = new ComboBox { ItemsSource = new[] { "Graphite", "Ocean", "Violet" }, SelectedItem = settings.Tint, Margin = new Thickness(0, 5, 0, 9) };
            AutomationProperties.SetName(tints, "Dock tint");
            tints.SelectionChanged += (_, _) => Apply(settings with { Tint = (string)tints.SelectedItem });
            panel.Children.Add(new TextBlock { Text = "Dock tint" });
            panel.Children.Add(tints);
            Toggle("Show system shortcuts", settings.SystemLinks, value => settings with { SystemLinks = value });
            Toggle("Show clock and date", settings.Clock, value => settings with { Clock = value });
            Toggle("Use 24-hour time", settings.Use24Hour, value => settings with { Use24Hour = value });
        }

        void Behavior()
        {
            Page("Behavior", hotkeyRegistered
                ? "Reveal from the bottom edge, with Ctrl+Alt+D, or from the Windows Dock tray icon."
                : "Reveal from the bottom edge or the Windows Dock tray icon. Ctrl+Alt+D is unavailable.");
            Toggle("Auto-hide when idle", settings.AutoHide, value => settings with { AutoHide = value });
            Slider("Hide delay (ms)", 200, 3000, settings.HideDelay, value => settings with { HideDelay = value }, 100);
            Slider("Reveal edge depth (px)", 2, 24, settings.EdgeDepth, value => settings with { EdgeDepth = value });
            panel.Children.Add(StudioButton("Reveal dock now", Reveal));
        }

        void Widgets()
        {
            Page("Widgets", "Add status readouts to the dock. Select a widget to open its related Windows page.");
            foreach (var name in AvailableWidgets)
                Toggle(name, settings.Widgets.Contains(name), value => settings with
                {
                    Widgets = value ? [.. settings.Widgets, name] : settings.Widgets.Where(widget => widget != name).ToArray()
                });
        }

        void Applications()
        {
            Page("Applications", "Choose what to pin and set the order of pinned apps.");
            panel.Children.Add(StudioButton("Pin an application…", () =>
            {
                if (settings.Pins.Length >= 24)
                {
                    MessageBox.Show(studio!, "The dock supports up to 24 pins.", "Windows Dock");
                    return;
                }
                var picker = new OpenFileDialog { Filter = "Applications and shortcuts|*.exe;*.lnk", CheckFileExists = true };
                if (picker.ShowDialog(studio) != true || settings.Pins.Any(pin => string.Equals(pin.Target, picker.FileName, StringComparison.OrdinalIgnoreCase)))
                    return;
                Apply(settings with { Pins = [.. settings.Pins, new Pin(System.IO.Path.GetFileNameWithoutExtension(picker.FileName), picker.FileName)] });
                Applications();
            }));
            for (var index = 0; index < settings.Pins.Length; index++)
            {
                var position = index;
                var pin = settings.Pins[index];
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                AddAction("Remove", () =>
                {
                    Apply(settings with { Pins = settings.Pins.Where(item => item != pin).ToArray() });
                    Applications();
                });
                if (position + 1 < settings.Pins.Length)
                    AddAction("Move down", () => MovePin(position, position + 1));
                if (position > 0)
                    AddAction("Move up", () => MovePin(position, position - 1));
                row.Children.Add(new TextBlock { Text = pin.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                panel.Children.Add(row);

                void AddAction(string label, Action action)
                {
                    var button = StudioButton(label, action);
                    AutomationProperties.SetName(button, label + " " + pin.Name);
                    DockPanel.SetDock(button, Dock.Right);
                    row.Children.Add(button);
                }
            }

            void MovePin(int from, int to)
            {
                var pins = settings.Pins.ToArray();
                (pins[from], pins[to]) = (pins[to], pins[from]);
                Apply(settings with { Pins = pins });
                Applications();
            }
        }

        void Toggle(string label, bool initial, Func<bool, DockSettings> change)
        {
            var toggle = new CheckBox { Content = label, IsChecked = initial, Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 7) };
            toggle.Checked += (_, _) => Apply(change(true));
            toggle.Unchecked += (_, _) => Apply(change(false));
            panel.Children.Add(toggle);
        }

        void Slider(string label, int min, int max, int initial, Func<int, DockSettings> change, int step = 1)
        {
            var text = new TextBlock { Text = $"{label} · {initial}" };
            var slider = new Slider { Minimum = min, Maximum = max, Value = initial,
                TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 13) };
            AutomationProperties.SetName(slider, label);
            slider.ValueChanged += (_, e) =>
            {
                var value = (int)e.NewValue;
                text.Text = $"{label} · {value}";
                Apply(change(value));
            };
            panel.Children.Add(text);
            panel.Children.Add(slider);
        }
    }

    private static Button StudioButton(string label, Action invoke)
    {
        var button = new Button
        {
            Content = label, Background = StudioPanel, Foreground = StudioAccent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(67, 77, 92)),
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 7)
        };
        button.Click += (_, _) => invoke();
        return button;
    }
}
