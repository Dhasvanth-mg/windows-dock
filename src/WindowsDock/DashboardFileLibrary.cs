using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowFileSearch()
    {
        var (window, content, cancellation) = CreateDockPanel("File library", "", 640, 0xE721, accent, widget: true);
        BuildFileSearch(window, content, cancellation);
        window.Show();
    }

    private void BuildFileSearch(Window window, StackPanel content, CancellationToken cancellation)
    {
        var upload = AccentButton("Upload files…", 0xE898, accent);
        var stop = ActionButton("Cancel upload");
        stop.Visibility = Visibility.Collapsed;
        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 7), Children = { upload, stop } };
        var input = Field("Search library", 160);
        input.Tag = "Search uploaded files";
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var list = new StackPanel();
        var preview = new ContentControl { Margin = new Thickness(0, 6, 0, 7), Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var part in new UIElement[] { controls, input, status, preview, list })
            content.Children.Add(part);

        CancellationTokenSource? activeUpload = null;
        var searchVersion = 0;
        var previewVersion = 0;
        upload.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Upload to the dock file library", Multiselect = true, CheckFileExists = true,
                Filter = "Documents and images|*.txt;*.md;*.csv;*.docx;*.pdf;*.xlsx;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
            if (picker.ShowDialog(window) == true)
                await UploadAsync(picker.FileNames);
        };
        stop.Click += (_, _) => activeUpload?.Cancel();
        content.AllowDrop = true;
        content.PreviewDragOver += (_, args) =>
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            args.Effects = activeUpload == null ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        };
        content.PreviewDrop += async (_, args) =>
        {
            if (activeUpload == null && args.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                args.Handled = true;
                await UploadAsync(paths);
            }
        };
        input.TextChanged += (_, _) =>
        {
            ClosePreview();
            _ = FindAsync();
        };
        _ = FindAsync();

        async Task UploadAsync(string[] paths)
        {
            if (activeUpload != null || paths.Length == 0 || cancellation.IsCancellationRequested)
                return;
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            activeUpload = request;
            upload.IsEnabled = false;
            stop.Visibility = Visibility.Visible;
            ClosePreview();
            var indexed = 0;
            var repeats = 0;
            var issues = new List<string>();
            try
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    var path = paths[i];
                    status.Text = $"Indexing {i + 1} of {paths.Length} · {Path.GetFileName(path)}";
                    try
                    {
                        if (await Task.Run(() => DockFileLibrary.UploadAsync(path, request.Token), request.Token))
                            indexed++;
                        else
                            repeats++;
                    }
                    catch (ArgumentException error) { issues.Add($"{Path.GetFileName(path)}: {error.Message}"); }
                    catch (Exception error) when (IsLibraryError(error))
                    { issues.Add($"{Path.GetFileName(path)} could not be uploaded."); }
                }
                status.Text = $"{indexed} added." +
                    (repeats > 0 ? $" {repeats} already in your library." : "") +
                    (issues.Count > 0 ? $" {issues.Count} failed. {issues[0]}" : "");
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { status.Text = $"Upload stopped. {indexed} added."; }
            catch (OperationCanceledException) { }
            finally
            {
                activeUpload = null;
                if (!cancellation.IsCancellationRequested)
                {
                    stop.Visibility = Visibility.Collapsed;
                    upload.IsEnabled = true;
                    await FindAsync(debounce: false);
                }
            }
        }

        async Task FindAsync(bool debounce = true)
        {
            var version = ++searchVersion;
            var query = input.Text.Trim();
            try
            {
                if (debounce)
                    await Task.Delay(160, cancellation);
                if (version != searchVersion || cancellation.IsCancellationRequested)
                    return;
                var matches = await Task.Run(() => DockFileLibrary.Search(query, cancellation), cancellation);
                if (version != searchVersion || cancellation.IsCancellationRequested)
                    return;
                list.Children.Clear();
                foreach (var result in matches)
                {
                    var title = Text(result.Name, 13, AssistantInk, strong: true);
                    title.TextTrimming = TextTrimming.CharacterEllipsis;
                    var snippet = Text(result.Snippet, 11, AssistantSecondary);
                    snippet.TextWrapping = TextWrapping.Wrap;
                    snippet.MaxHeight = 38;
                    var open = MakeButton(new StackPanel { Children = { title, snippet } }, "Preview " + result.Name,
                        async () => await PreviewAsync(result));
                    open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    open.Margin = new Thickness(0, 0, 0, 5);
                    list.Children.Add(open);
                }
                if (matches.Length == 0)
                    list.Children.Add(Text(query.Length == 0 ? "Upload or drop files here to start." : "No matching uploads.",
                        12, AssistantSecondary));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IsLibraryError(error))
            {
                if (version == searchVersion)
                    status.Text = "Could not read the local file library.";
            }
        }

        async Task PreviewAsync(LibraryResult result)
        {
            var version = ++previewVersion;
            try
            {
                var document = await Task.Run(() => DockFileLibrary.Preview(result.Id), cancellation);
                if (document == null)
                    return;
                ImageSource? thumbnail = null;
                if (document.Image != null)
                {
                    try
                    {
                        if (version == previewVersion && !cancellation.IsCancellationRequested)
                            thumbnail = await LibraryImageAnalysis.PreviewAsync(document.Image, cancellation);
                    }
                    finally { CryptographicOperations.ZeroMemory(document.Image); }
                }
                if (version != previewVersion || cancellation.IsCancellationRequested)
                    return;
                var body = new StackPanel();
                var header = new DockPanel();
                var close = IconButton(0xE711, "Close library preview", ClosePreview);
                DockPanel.SetDock(close, Dock.Right);
                header.Children.Add(close);
                var title = Text(document.File.Name, 14, AssistantInk, strong: true);
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                header.Children.Add(title);
                body.Children.Add(header);
                var note = Text($"{document.File.Analysis} · {document.File.Size:N0} bytes", 11, AssistantSecondary);
                note.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(note);
                if (thumbnail != null)
                    body.Children.Add(new Image { Source = thumbnail, MaxHeight = 170, Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 4) });
                if (document.File.Tags.Length > 0)
                {
                    var tags = Text(document.File.Tags, 11, AssistantSecondary);
                    tags.TextWrapping = TextWrapping.Wrap;
                    body.Children.Add(tags);
                }
                if (document.File.Text.Length > 0)
                {
                    var text = Field("Indexed file text", 0);
                    text.Text = document.File.Text;
                    text.IsReadOnly = true;
                    text.TextWrapping = TextWrapping.Wrap;
                    text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    text.MaxHeight = 180;
                    text.Margin = new Thickness(0, 6, 0, 4);
                    body.Children.Add(text);
                }
                var remove = QuietButton("Remove from library", async () => await RemoveAsync(result));
                remove.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(remove);
                preview.Content = Card(body);
                preview.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IsLibraryError(error))
            { if (version == previewVersion) status.Text = "Could not preview this upload."; }
        }

        async Task RemoveAsync(LibraryResult result)
        {
            if (!await ConfirmAsync(window, $"Remove {result.Name} and its search terms from the library?", "Remove"))
                return;
            try
            {
                await Task.Run(() => DockFileLibrary.Remove(result.Id), cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
                ClosePreview();
                status.Text = "Upload removed.";
                await FindAsync(debounce: false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IsLibraryError(error))
            { status.Text = "Could not remove this upload."; }
        }

        void ClosePreview()
        {
            previewVersion++;
            preview.Content = null;
            preview.Visibility = Visibility.Collapsed;
        }
    }

    private static bool IsLibraryError(Exception error) => error is IOException or UnauthorizedAccessException or SqliteException or
        CryptographicException or JsonException or InvalidDataException or FormatException or InvalidOperationException;
}
