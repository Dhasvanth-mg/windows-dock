using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace WindowsDock;

public partial class DashboardWindow
{
    private List<ClipItem>? clips;

    private void ShowClipboardWidget()
    {
        clips ??= ReadJson<List<ClipItem>>("clipboard-pins.json") ?? [];
        var (window, content, cancellation) = CreateAgentPanel("Clipboard and files", "", 472);
        var controls = new WrapPanel();
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        var historyRevision = 0;
        var pin = ActionButton("Pin current clipboard", PinClipboard);
        pin.Margin = new Thickness(0, 0, 5, 4);
        controls.Children.Add(pin);
        var files = ActionButton("File shelf", () => { window.Close(); ShowPage(DashboardPage.Shelf); });
        files.Margin = new Thickness(0, 0, 5, 4);
        controls.Children.Add(files);
        controls.Children.Add(ActionButton("Windows clipboard history", () => OpenTarget("ms-settings:clipboard")));
        content.Children.Add(controls);
        content.Children.Add(status);
        content.Children.Add(list);
        Render();
        window.Show();
        _ = AddHistoryAsync();

        async System.Threading.Tasks.Task AddHistoryAsync()
        {
            var revision = ++historyRevision;
            try
            {
                var history = await Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync().AsTask(cancellation);
                if (revision != historyRevision || history.Status != ClipboardHistoryItemsResultStatus.Success || history.Items.Count == 0)
                    return;
                list.Children.Add(Text("Recent clipboard", 11, muted, true));
                foreach (var item in history.Items.Take(8))
                {
                    var data = item.Content;
                    var preview = data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync().AsTask(cancellation)
                        : data.Contains(StandardDataFormats.Bitmap) ? "Image" : "Clipboard item";
                    if (revision != historyRevision || cancellation.IsCancellationRequested)
                        return;
                    var button = ActionButton(ShortLabel(preview), () =>
                    {
                        try
                        {
                            var result = Windows.ApplicationModel.DataTransfer.Clipboard.SetHistoryItemAsContent(item);
                            status.Text = result == SetHistoryItemAsContentStatus.Success ? "Copied." : "Clipboard item is no longer available.";
                        }
                        catch (COMException) { status.Text = "Windows could not restore this item."; }
                    });
                    button.HorizontalAlignment = HorizontalAlignment.Left;
                    button.Margin = new Thickness(0, 4, 0, 0);
                    list.Children.Add(button);
                }
            }
            catch (Exception error) when (error is COMException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException) { }
        }

        void PinClipboard()
        {
            try
            {
                ClipItem? item = null;
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var paths = System.Windows.Clipboard.GetFileDropList().Cast<string>()
                        .Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
                    if (paths.Length > 0)
                    {
                        AddFiles(paths);
                        status.Text = "Added to the file shelf.";
                        return;
                    }
                }
                if (System.Windows.Clipboard.ContainsImage() && System.Windows.Clipboard.GetImage() is { } bitmap &&
                    (long)bitmap.PixelWidth * bitmap.PixelHeight <= 16_000_000)
                {
                    var folder = DataPath("clipboard-images");
                    Directory.CreateDirectory(folder);
                    var file = Guid.NewGuid().ToString("N") + ".png";
                    using var stream = File.Create(Path.Combine(folder, file));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                    item = new("Image", null, file);
                }
                else if (System.Windows.Clipboard.ContainsText())
                {
                    var text = System.Windows.Clipboard.GetText();
                    if (text.Length is > 0 and <= 32_000)
                        item = new(ShortLabel(text), text, null);
                }
                if (item == null)
                {
                    status.Text = "Copy text, an image, or files first.";
                    return;
                }
                clips.RemoveAll(clip => item.Text != null && clip.Text == item.Text);
                clips.Insert(0, item);
                foreach (var excess in clips.Skip(20))
                    DeleteClipImage(excess);
                if (clips.Count > 20)
                    clips.RemoveRange(20, clips.Count - 20);
                WriteJson("clipboard-pins.json", clips);
                status.Text = "Pinned to this device.";
                Render();
                _ = AddHistoryAsync();
            }
            catch (Exception error) when (error is ExternalException or IOException or UnauthorizedAccessException)
            {
                status.Text = "The clipboard is busy or the item could not be saved.";
            }
        }

        void Render()
        {
            list.Children.Clear();
            list.Children.Add(Text(clips.Count == 0 ? "No pinned items." : "Pinned", 11, muted, true));
            foreach (var item in clips)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                var remove = IconButton(0xE711, "Unpin " + item.Label, () =>
                {
                    clips.Remove(item);
                    WriteJson("clipboard-pins.json", clips);
                    DeleteClipImage(item);
                    Render();
                    _ = AddHistoryAsync();
                });
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var copy = ActionButton(item.Label, () =>
                {
                    try
                    {
                        if (item.Text != null)
                            System.Windows.Clipboard.SetText(item.Text);
                        else if (ClipImagePath(item) is { } path)
                            System.Windows.Clipboard.SetImage(new BitmapImage(new Uri(path)));
                        status.Text = "Copied.";
                    }
                    catch (Exception error) when (error is ExternalException or IOException or NotSupportedException)
                    {
                        status.Text = "Could not copy this pin.";
                    }
                });
                copy.HorizontalAlignment = HorizontalAlignment.Left;
                copy.ToolTip = "Click to copy or drag into an app";
                EnableDrag(copy, () => item.Text is { } text ? new DataObject(DataFormats.UnicodeText, text) :
                    ClipImagePath(item) is { } path && File.Exists(path) ? new DataObject(DataFormats.FileDrop, new[] { path }) : null);
                row.Children.Add(copy);
                list.Children.Add(row);
            }
        }
    }

    private static void EnableDrag(UIElement element, Func<DataObject?> data)
    {
        Point start = default;
        element.PreviewMouseLeftButtonDown += (_, args) => start = args.GetPosition(element);
        element.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed || !element.IsMouseCaptured)
                return;
            var moved = args.GetPosition(element) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (data() is { } item)
            {
                args.Handled = true;
                DragDrop.DoDragDrop(element, item, DragDropEffects.Copy);
            }
        };
    }

    private static string ShortLabel(string value)
    {
        var line = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > 48 ? line[..47] + "…" : line;
    }

    private static string? ClipImagePath(ClipItem item) => item.Image is { } name &&
        Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _) && Path.GetFileName(name) == name
            ? DataPath(Path.Combine("clipboard-images", name)) : null;

    private static void DeleteClipImage(ClipItem item)
    {
        if (ClipImagePath(item) is not { } path)
            return;
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private sealed record ClipItem(string Label, string? Text, string? Image);
}
