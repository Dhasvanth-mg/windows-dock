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

    // Files: a short list you can drop things onto and drag things back out of.
    private FrameworkElement ShelfPane()
    {
        shelf ??= ReadJson<List<string>>("files.json") ?? [];
        var content = new DockPanel { LastChildFill = true };
        var heading = new DockPanel { Margin = new Thickness(0, -4, 0, 2) };
        var browse = IconButton(0xE710, "Add files", () => ChooseFiles(false));
        browse.Margin = new Thickness(0, 0, -6, 0);
        DockPanel.SetDock(browse, Dock.Right);
        heading.Children.Add(browse);
        var title = SectionTitle("Files");
        title.Margin = new Thickness(0);
        title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(title);
        DockPanel.SetDock(heading, Dock.Top);
        content.Children.Add(heading);

        if (shelf.Count == 0)
        {
            // A dashed outline is the one place a border earns its keep: it says "drop here".
            var outline = new System.Windows.Shapes.Rectangle
            {
                Stroke = AssistantFaint, StrokeThickness = 1, StrokeDashArray = [4, 4], RadiusX = 10, RadiusY = 10,
                SnapsToDevicePixels = true
            };
            var hint = Text("Drop files or folders here to keep them handy. Drag them back out into any app.", 12, subdued);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextAlignment = TextAlignment.Center;
            hint.Margin = new Thickness(22, 0, 22, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(new Grid { MinHeight = 150, Children = { outline, hint } });
        }
        else
        {
            var items = new StackPanel();
            foreach (var path in shelf)
            {
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
                var name = Path.GetFileName(trimmed);
                var folder = Directory.Exists(path);
                var line = new DockPanel();
                var glyph = DockGlyph(folder ? 0xE8B7 : 0xE7C3, 14, AssistantSecondary);
                glyph.Margin = new Thickness(0, 0, 10, 0);
                line.Children.Add(glyph);
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                labels.Children.Add(Text(string.IsNullOrEmpty(name) ? path : name, 13));
                labels.Children.Add(Text(Path.GetDirectoryName(trimmed) ?? "", 11, muted));
                line.Children.Add(labels);
                var open = PlainButton(line, "Open " + (string.IsNullOrEmpty(name) ? path : name), () =>
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        ShowOwnedMessage("This item has moved or is no longer there.");
                        return;
                    }
                    OpenTarget(path);
                });
                open.Margin = new Thickness(0);
                open.Padding = new Thickness(8, 5, 34, 5);
                open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                open.ToolTip = path;
                EnableDrag(open, () => File.Exists(path) || Directory.Exists(path)
                    ? new DataObject(DataFormats.FileDrop, new[] { path }) : null);
                var remove = IconButton(0xE711, "Remove " + name + " from the shelf", () =>
                {
                    shelf.Remove(path);
                    WriteJson("files.json", shelf);
                    ShowPage(DashboardPage.Desk);
                });
                remove.HorizontalAlignment = HorizontalAlignment.Right;
                remove.VerticalAlignment = VerticalAlignment.Center;
                remove.Margin = new Thickness(0, 0, 2, 0);
                ((TextBlock)remove.Content).FontSize = 10;
                ((TextBlock)remove.Content).Foreground = AssistantSecondary;
                items.Children.Add(new Grid { Margin = new Thickness(-8, 0, -8, 1), Children = { open, remove } });
            }
            content.Children.Add(new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 310 });
        }
        var pane = new Border { Child = content, Background = Brushes.Transparent, AllowDrop = true };
        pane.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        pane.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddFiles(paths);
        };
        return pane;
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
                ShowPage(DashboardPage.Desk);
            }
            else
                AddFiles(picker.FileNames);
        }
        finally { modalOpen = false; }
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        shelf ??= ReadJson<List<string>>("files.json") ?? [];
        shelf = shelf.Concat(paths.Where(path => File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        WriteJson("files.json", shelf);
        if (currentPage == DashboardPage.Desk)
            ShowPage(DashboardPage.Desk);
    }

    private FrameworkElement WallpaperPane()
    {
        wallpapers ??= ReadJson<List<string>>("wallpapers.json") ?? [];
        var available = wallpapers.Where(File.Exists).Take(30).ToArray();
        var pane = new StackPanel();
        pane.Children.Add(SectionTitle("Wallpaper"));
        if (available.Length == 0)
        {
            var help = Text("Add a few images and switch between them from here.", 12, subdued);
            help.TextWrapping = TextWrapping.Wrap;
            help.Margin = new Thickness(0, 4, 0, 12);
            pane.Children.Add(help);
            var first = ActionButton("Add images…", () => ChooseFiles(true));
            first.HorizontalAlignment = HorizontalAlignment.Left;
            pane.Children.Add(first);
            return pane;
        }

        wallpaperIndex = Math.Clamp(wallpaperIndex, 0, available.Length - 1);
        var selection = available[wallpaperIndex];
        var preview = new Border { CornerRadius = new CornerRadius(8), Background = ControlFill, Height = 156, Margin = new Thickness(0, 2, 0, 10) };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(selection);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 560;
            bitmap.EndInit();
            bitmap.Freeze();
            preview.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception error) when (error is IOException or NotSupportedException or FormatException or ArgumentException) { }
        pane.Children.Add(preview);

        var caption = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var position = Text($"{wallpaperIndex + 1} of {available.Length}", 11, muted);
        position.VerticalAlignment = VerticalAlignment.Center;
        position.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(position, Dock.Right);
        caption.Children.Add(position);
        var name = Text(Path.GetFileNameWithoutExtension(selection), 13);
        name.ToolTip = selection;
        caption.Children.Add(name);
        pane.Children.Add(caption);

        var actions = new DockPanel();
        var next = IconButton(0xE76C, "Next image", () => Step(1));
        var previous = IconButton(0xE76B, "Previous image", () => Step(-1));
        previous.IsEnabled = next.IsEnabled = available.Length > 1;
        next.Margin = new Thickness(0, 0, -6, 0);
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(previous, Dock.Right);
        actions.Children.Add(next);
        actions.Children.Add(previous);
        var apply = PrimaryButton("Set as wallpaper", () =>
        {
            wallpaperStatus = SetWallpaper(0x0014, 0, selection, 3) ? "Done. That is your wallpaper now." : "Windows would not use this image.";
            ShowPage(DashboardPage.Desk);
        });
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        actions.Children.Add(apply);
        pane.Children.Add(actions);
        if (wallpaperStatus != null)
        {
            var status = Text(wallpaperStatus, 12, subdued);
            status.Margin = new Thickness(0, 8, 0, 0);
            pane.Children.Add(status);
        }
        var add = QuietButton("Add more images…", () => ChooseFiles(true));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 10, 0, 0);
        pane.Children.Add(add);
        return pane;

        void Step(int direction)
        {
            wallpaperStatus = null;
            wallpaperIndex = (wallpaperIndex + direction + available.Length) % available.Length;
            ShowPage(DashboardPage.Desk);
        }
    }

    [DllImport("user32", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool SetWallpaper(uint action, uint parameter, string path, uint flags);

    [DllImport("user32", SetLastError = true)]
    private static extern bool LockWorkStation();
}
