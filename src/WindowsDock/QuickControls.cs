using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Forms = System.Windows.Forms;

namespace WindowsDock;

public partial class MainWindow
{
    private Popup? quickControls;

    private static TextBlock Glyph(int code, Brush? brush = null, double size = 14) => new()
    {
        Text = char.ConvertFromUtf32(code),
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        Foreground = brush ?? Brushes.White,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center
    };

    private void AddOverflow() => AddButton("Dock overflow", Glyph(0xE70E), ToggleQuickControls);

    private void AddStatusGroup()
    {
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var code in new[] { 0xE701, 0xE767, 0xE83F })
        {
            var icon = Glyph(code, size: 12);
            icon.Margin = new Thickness(4, 0, 4, 0);
            icons.Children.Add(icon);
        }
        var group = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(90, 86, 92, 90)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2, 4, 2, 4),
            Child = icons
        };
        AddButton("Quick controls", group, ToggleQuickControls);
    }

    private void ToggleQuickControls()
    {
        if (quickControls?.IsOpen == true)
        {
            quickControls.IsOpen = false;
            return;
        }

        var content = new StackPanel();
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 8) };
        content.Children.Add(tiles);
        AddTile("Wi-Fi", 0xE701, "ms-settings:network-wifi");
        AddTile("Airplane mode", 0xE709, "ms-settings:network-airplanemode");
        AddTile("Bluetooth", 0xE702, "ms-settings:bluetooth");
        AddTile("Energy saver", 0xE8BE, "ms-settings:powersleep");
        AddTile("Night light", 0xE706, "ms-settings:nightlight");
        AddTile("Accessibility", 0xE776, "ms-settings:easeofaccess");

        var volumeRow = new DockPanel { Margin = new Thickness(0, 8, 0, 12) };
        var volumeIcon = Glyph(0xE767);
        volumeIcon.Margin = new Thickness(0, 0, 12, 0);
        volumeRow.Children.Add(volumeIcon);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var volume = device.AudioEndpointVolume;
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = volume.MasterVolumeLevelScalar * 100,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 139, 113)) };
            AutomationProperties.SetName(slider, "Output volume");
            slider.ValueChanged += (_, e) =>
            {
                try
                {
                    WithDevice(output =>
                    {
                        using var endpoint = output.AudioEndpointVolume;
                        endpoint.MasterVolumeLevelScalar = (float)(e.NewValue / 100);
                    });
                }
                catch (COMException) { slider.IsEnabled = false; }
            };
            volumeRow.Children.Add(slider);
        }
        catch (COMException)
        {
            volumeRow.Children.Add(new TextBlock { Text = "Audio output unavailable", Foreground = Brushes.White });
        }
        content.Children.Add(volumeRow);

        var footer = new DockPanel();
        var settingsButton = TileButton(Glyph(0xE713), "Dock settings", ShowStudio);
        DockPanel.SetDock(settingsButton, Dock.Right);
        footer.Children.Add(settingsButton);
        var power = Forms.SystemInformation.PowerStatus;
        var hasBattery = !power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery);
        footer.Children.Add(new TextBlock
        {
            Text = hasBattery ? $"Battery  {power.BatteryLifePercent:P0}" : "Power",
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(footer);

        var panel = new Border
        {
            Width = 350, Padding = new Thickness(16, 15, 16, 11),
            Background = new SolidColorBrush(Color.FromArgb(250, 36, 35, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(78, 75, 71)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Child = content
        };
        var popup = new Popup
        {
            AllowsTransparency = true, StaysOpen = false, Child = panel,
            PlacementTarget = Surface, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (size, target, _) =>
                [new CustomPopupPlacement(new Point((target.Width - size.Width) / 2, -size.Height - 10), PopupPrimaryAxis.Horizontal)]
        };
        quickControls = popup;
        panel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                popup.IsOpen = false;
        };
        popup.Opened += (_, _) => tiles.Children.OfType<StackPanel>().FirstOrDefault()?.MoveFocus(
            new TraversalRequest(FocusNavigationDirection.First));
        popup.Closed += (_, _) =>
        {
            quickControls = null;
            if (refreshPending)
                Dispatcher.BeginInvoke(RequestRender);
        };
        popup.IsOpen = true;

        void AddTile(string name, int code, string target)
        {
            var tile = new StackPanel { Margin = new Thickness(4, 0, 4, 10) };
            var button = TileButton(Glyph(code, size: 16), name, () => Launch(target));
            button.Height = 44;
            tile.Children.Add(button);
            tile.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 11,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            tiles.Children.Add(tile);
        }

        Button TileButton(object visual, string name, Action action)
        {
            var button = new Button { Content = visual, Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromRgb(59, 56, 53)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(82, 77, 73)), BorderThickness = new Thickness(1) };
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                if (quickControls is { } current)
                    current.IsOpen = false;
                action();
            };
            return button;
        }
    }

    private static UniformGrid StartMark()
    {
        var mark = new UniformGrid { Rows = 2, Columns = 2, Width = 17, Height = 17 };
        var blue = new SolidColorBrush(Color.FromRgb(44, 180, 252));
        for (var pane = 0; pane < 4; pane++)
            mark.Children.Add(new Border { Background = blue, Margin = new Thickness(0.45) });
        return mark;
    }

    private static void OpenStart()
    {
        keybd_event(0x5B, 0, 0, 0);
        keybd_event(0x5B, 0, 2, 0);
    }
}
