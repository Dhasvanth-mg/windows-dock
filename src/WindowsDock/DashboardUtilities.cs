using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private List<string>? shelf;
    private List<string>? wallpapers;
    private int wallpaperIndex;
    private string? wallpaperStatus;

    private UIElement ShelfPage()
    {
        shelf ??= ReadJson<List<string>>("files.json") ?? [];
        var content = new StackPanel { MinHeight = 81 };
        var top = new DockPanel();
        var browse = ActionButton("Add files…", () => ChooseFiles(false));
        DockPanel.SetDock(browse, Dock.Right);
        top.Children.Add(browse);
        top.Children.Add(Text("File shelf", 13, strong: true));
        content.Children.Add(top);
        if (shelf.Count == 0)
        {
            var hint = Text("Drop files or folders here", 13, subdued);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 13, 0, 12);
            content.Children.Add(hint);
        }
        else
        {
            var items = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            foreach (var path in shelf)
            {
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                var open = ActionButton(string.IsNullOrEmpty(name) ? path : name, () =>
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        ShowOwnedMessage("This item has moved or is unavailable.");
                        return;
                    }
                    OpenTarget(path);
                });
                open.MaxWidth = 185;
                open.Margin = new Thickness(0, 0, 5, 5);
                open.ToolTip = path;
                EnableDrag(open, () => File.Exists(path) || Directory.Exists(path)
                    ? new DataObject(DataFormats.FileDrop, new[] { path }) : null);
                var menu = new ContextMenu();
                var remove = new MenuItem { Header = "Remove from shelf" };
                remove.Click += (_, _) =>
                {
                    shelf.Remove(path);
                    WriteJson("files.json", shelf);
                    ShowPage(DashboardPage.Shelf);
                };
                menu.Items.Add(remove);
                open.ContextMenu = menu;
                items.Children.Add(open);
            }
            content.Children.Add(new ScrollViewer { Content = items, MaxHeight = 115, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        var card = Card(content);
        card.AllowDrop = true;
        card.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        card.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddFiles(paths);
        };
        return card;
    }

    private void ChooseFiles(bool imageOnly)
    {
        var picker = new OpenFileDialog
        {
            Filter = imageOnly ? "Images|*.jpg;*.jpeg;*.png;*.bmp" : "All files|*.*",
            Multiselect = true, CheckFileExists = true
        };
        modalOpen = true;
        try
        {
            if (picker.ShowDialog(this) != true)
                return;
            if (imageOnly)
            {
                wallpapers ??= [];
                wallpapers = wallpapers.Concat(picker.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
                WriteJson("wallpapers.json", wallpapers);
                ShowPage(DashboardPage.Wallpapers);
            }
            else
                AddFiles(picker.FileNames);
        }
        finally { modalOpen = false; }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        shelf = (shelf ?? []).Concat(paths.Where(path => File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        WriteJson("files.json", shelf);
        ShowPage(DashboardPage.Shelf);
    }

    private UIElement WallpapersPage()
    {
        wallpapers ??= ReadJson<List<string>>("wallpapers.json") ?? [];
        var available = wallpapers.Where(File.Exists).Take(30).ToArray();
        var content = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var add = ActionButton("Add wallpapers…", () => ChooseFiles(true));
        DockPanel.SetDock(add, Dock.Right);
        top.Children.Add(add);
        top.Children.Add(Text("Wallpaper library", 13, strong: true));
        content.Children.Add(top);
        if (available.Length == 0)
        {
            content.Children.Add(Text("Add images to build your wallpaper library.", 13, subdued));
            return Card(content);
        }

        wallpaperIndex = Math.Clamp(wallpaperIndex, 0, available.Length - 1);
        var selection = available[wallpaperIndex];
        var image = new Image { Height = 127, Stretch = Stretch.UniformToFill };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(selection);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 640;
            bitmap.EndInit();
            image.Source = bitmap;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or FormatException or ArgumentException) { }
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(Text(Path.GetFileNameWithoutExtension(selection), 15, strong: true));
        controls.Children.Add(Text($"{wallpaperIndex + 1} of {available.Length}", 11, muted));
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var (name, action) in new (string, Action)[]
        {
            ("Previous", () => Step(-1)),
            ("Next", () => Step(1)),
            ("Apply wallpaper", () =>
            {
                wallpaperStatus = SetWallpaper(0x0014, 0, selection, 3) ? "Wallpaper applied." : "Windows could not apply this image.";
                ShowPage(DashboardPage.Wallpapers);
            })
        })
        {
            var button = ActionButton(name, action);
            button.Margin = new Thickness(0, 0, 4, 4);
            actions.Children.Add(button);
        }
        controls.Children.Add(actions);
        if (wallpaperStatus != null)
            controls.Children.Add(Text(wallpaperStatus, 11, subdued));
        var row = Columns(1.35, 1);
        Put(row, Card(image), 0);
        Put(row, Card(controls), 1);
        content.Children.Add(row);
        return content;

        void Step(int direction)
        {
            wallpaperStatus = null;
            wallpaperIndex = (wallpaperIndex + direction + available.Length) % available.Length;
            ShowPage(DashboardPage.Wallpapers);
        }
    }

    private UIElement SettingsPage()
    {
        var row = Columns(1.3, 1);
        var general = new StackPanel();
        general.Children.Add(Text("Windows taskbar", 14, strong: true));
        general.Children.Add(Text("App pins, tray icons, and auto-hide use Windows settings.", 12, subdued));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var windows = ActionButton("Taskbar settings", () => OpenTarget("ms-settings:taskbar"));
        windows.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(windows);
        if (controllerPid > 0)
        {
            actions.Children.Add(ActionButton("Restore taskbar appearance", () =>
            {
                if (System.Threading.EventWaitHandle.TryOpenExisting(
                    @"Local\WindowsDock.NativeTaskbar." + controllerPid + ".Stop", out var stop))
                {
                    using (stop)
                        stop.Set();
                }
                Close();
            }));
        }
        general.Children.Add(actions);
        Put(row, Card(general), 0);
        var power = new StackPanel();
        power.Children.Add(Text("Power and display", 14, strong: true));
        var battery = System.Windows.Forms.SystemInformation.PowerStatus;
        power.Children.Add(Text(battery.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            ? "Desktop power" : $"Battery  {battery.BatteryLifePercent:P0}", 12, subdued));
        var controls = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var powerOptions = ActionButton("Power settings", () => OpenTarget("ms-settings:powersleep"));
        powerOptions.Margin = new Thickness(0, 0, 6, 0);
        controls.Children.Add(powerOptions);
        controls.Children.Add(ActionButton("Display settings", () => OpenTarget("ms-settings:display")));
        power.Children.Add(controls);
        Put(row, Card(power), 1);
        return row;
    }

    private UIElement PowerPage()
    {
        var row = Columns(1, 1);
        var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var battery = System.Windows.Forms.SystemInformation.PowerStatus;
        var hasBattery = !battery.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
        var charge = Math.Clamp(battery.BatteryLifePercent, 0, 1);
        var charging = battery.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        var chargeBrush = !charging && charge < .2 ? amber : green;
        var badge = new Grid { Width = 38, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 7) };
        badge.Children.Add(new Border { CornerRadius = new CornerRadius(5), BorderBrush = subdued,
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 3, 0) });
        badge.Children.Add(new Border { CornerRadius = new CornerRadius(1), Background = subdued, Width = 2, Height = 7,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        if (hasBattery)
        {
            badge.Children.Add(new Border { Width = Math.Max(2, 29 * charge), CornerRadius = new CornerRadius(2), Background = chargeBrush,
                Margin = new Thickness(3, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left });
            summary.Children.Add(badge);
        }
        summary.Children.Add(Text(hasBattery ? charging ? "Connected to power" : "On battery" : "Desktop power", 11, subdued, true));
        summary.Children.Add(Text(hasBattery ? $"{charge:P0}" : "Plugged in", 28, strong: true));
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        summary.Children.Add(Text($"Uptime  {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m", 11, muted));
        Put(row, Card(summary), 0);

        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Text("System power", 12, strong: true));
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var power = ActionButton("Power settings", () => OpenTarget("ms-settings:powersleep"));
        power.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(power);
        buttons.Children.Add(ActionButton("Lock Windows", () =>
        {
            if (LockWorkStation())
                Dismiss();
        }));
        actions.Children.Add(buttons);
        Put(row, Card(actions), 1);
        return row;
    }

    [DllImport("user32", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool SetWallpaper(uint action, uint parameter, string path, uint flags);

    [DllImport("user32", SetLastError = true)]
    private static extern bool LockWorkStation();
}
