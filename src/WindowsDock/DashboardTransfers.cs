using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowDownloadWidget()
    {
        var (window, content, cancellation) = CreateAgentPanel("Downloads", "", 466);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var refresh = ActionButton("Refresh");
        refresh.Click += (_, _) => _ = LoadAsync();
        refresh.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(refresh);
        actions.Children.Add(ActionButton("Open Downloads folder", () => OpenTarget(DownloadsDirectory())));
        content.Children.Add(status);
        content.Children.Add(list);
        content.Children.Add(actions);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => { if (refresh.IsEnabled && window.IsActive) _ = LoadAsync(); };
        window.Closed += (_, _) => timer.Stop();
        window.Show();
        timer.Start();
        _ = LoadAsync();

        async System.Threading.Tasks.Task LoadAsync()
        {
            list.Children.Clear();
            status.Text = "Checking downloads…";
            refresh.IsEnabled = false;
            try
            {
                var folder = DownloadsDirectory();
                var readFiles = System.Threading.Tasks.Task.Run(() => ReadDownloads(folder), cancellation);
                var readJobs = ReadTransfersAsync();
                var files = await readFiles;
                var jobs = await readJobs;
                if (cancellation.IsCancellationRequested)
                    return;
                status.Text = "Recent files and Windows background transfers";
                if (files.Length == 0 && jobs.Length == 0)
                    status.Text = "No recent downloads or background transfers.";
                foreach (var job in jobs)
                    AddRow(job.Name, job.Status, null, folder);
                foreach (var file in files)
                {
                    var partial = file.Extension is ".crdownload" or ".part" or ".download";
                    AddRow(partial ? Path.GetFileNameWithoutExtension(file.Name) : file.Name,
                        (partial ? "In progress · " : "") + FormatBytes(file.Length), partial ? null : file.FullName, folder);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "The Downloads folder could not be read.";
            }
            finally { refresh.IsEnabled = true; }
        }

        void AddRow(string title, string detail, string? file, string folder)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var action = IconButton(0xE8B7, "Open folder for " + title, () => OpenTarget(folder));
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
            if (file != null)
            {
                var open = IconButton(0xE8A7, "Open " + title, () =>
                {
                    if (File.Exists(file))
                        OpenTarget(file);
                    else
                        status.Text = "This file has moved or is unavailable.";
                });
                DockPanel.SetDock(open, Dock.Right);
                row.Children.Add(open);
            }
            var labels = new StackPanel();
            var name = Text(title, 12, strong: true);
            name.ToolTip = title;
            labels.Children.Add(name);
            labels.Children.Add(Text(detail, 10, muted));
            row.Children.Add(labels);
            list.Children.Add(Card(row));
        }

        async System.Threading.Tasks.Task<Transfer[]> ReadTransfersAsync()
        {
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var output = await WidgetCommand.ReadAsync(powershell,
                ["-NoProfile", "-NonInteractive", "-Command",
                "@(Get-BitsTransfer -ErrorAction Stop | Where-Object TransferType -eq 'Download' | Select-Object -First 15 DisplayName,JobState,BytesTransferred,BytesTotal) | ConvertTo-Json -Compress"],
                cancellation);
            if (string.IsNullOrWhiteSpace(output))
                return [];
            try
            {
                using var doc = JsonDocument.Parse(output);
                var values = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : [doc.RootElement];
                return values.Select(job =>
                {
                    var bytes = AgentSessions.Count(job, "BytesTransferred") ?? 0;
                    var total = AgentSessions.Count(job, "BytesTotal");
                    var state = AgentSessions.String(job, "JobState") ?? (AgentSessions.Count(job, "JobState") switch
                    {
                        0 => "Queued", 1 => "Connecting", 2 => "Transferring", 3 => "Paused", 4 or 5 => "Error",
                        6 or 7 => "Transferred", 8 => "Canceled", _ => "Windows transfer"
                    });
                    var size = total is > 0 ? $"{Math.Clamp(100d * bytes / total.Value, 0, 100):0}% · {FormatBytes(bytes)} / {FormatBytes(total.Value)}" : FormatBytes(bytes);
                    return new Transfer(AgentSessions.String(job, "DisplayName") ?? "Background download", state + " · " + size);
                }).ToArray();
            }
            catch (JsonException) { return []; }
        }
    }

    private static FileInfo[] ReadDownloads(string folder) => Directory.Exists(folder)
        ? new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions
            { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint })
            .Where(file => file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)).OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(12).ToArray() : [];

    private static string FormatBytes(long bytes) => bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824d:0.0} GB" :
        bytes >= 1_048_576 ? $"{bytes / 1_048_576d:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024d:0} KB" : $"{bytes} B";

    private static string DownloadsDirectory()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(in id, 0, 0, out var value) != 0)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try { return Marshal.PtrToStringUni(value) ?? ""; }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    [DllImport("shell32", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

    private sealed record Transfer(string Name, string Status);
}
