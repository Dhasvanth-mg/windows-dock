using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowFileSearch()
    {
        var (window, content, cancellation) = CreateDockPanel("File search", "", 580, 0xE721, accent, widget: true);
        BuildFileSearch(window, content, cancellation);
        window.Show();
    }

    private void BuildFileSearch(Window window, StackPanel content, CancellationToken cancellation)
    {
        var folders = new ComboBox { Margin = new Thickness(0, 0, 0, 5) };
        AutomationProperties.SetName(folders, "Indexed folders");
        var add = ActionButton("Add folder");
        var refresh = ActionButton("Update index");
        var remove = ActionButton("Remove folder");
        var stop = ActionButton("Cancel index update");
        stop.Visibility = Visibility.Collapsed;
        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 7), Children = { add, refresh, remove, stop } };
        var input = Field("Search indexed files", 120);
        input.Tag = "Search file names or contents";
        var notice = Text("Choose folders to search. Text excerpts are stored on this PC.", 11, AssistantSecondary);
        notice.TextWrapping = TextWrapping.Wrap;
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var share = ActionButton("Review selected excerpts");
        share.IsEnabled = false;
        var recipients = new ComboBox { ItemsSource = AssistantPersonas.All, DisplayMemberPath = nameof(AssistantPersona.Name),
            SelectedIndex = assistantRole };
        AutomationProperties.SetName(recipients, "Assistant for these excerpts");
        var excerpt = Field("Excerpts to attach", AssistantContext.TextLimit);
        excerpt.Height = 120;
        excerpt.AcceptsReturn = true;
        excerpt.TextWrapping = TextWrapping.Wrap;
        excerpt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var attach = ActionButton("Attach excerpts to draft");
        var disclosure = Text("Your provider receives these excerpts when you send the chat message.", 11, AssistantSecondary);
        disclosure.TextWrapping = TextWrapping.Wrap;
        var review = new StackPanel
        {
            Children = { disclosure, excerpt, recipients, attach },
            Visibility = Visibility.Collapsed
        };
        var reviewed = Card(review);
        reviewed.Visibility = Visibility.Collapsed;
        var selected = new HashSet<SearchDocument>();
        var list = new StackPanel();
        foreach (var element in new UIElement[] { notice, folders, controls, input, status, share, reviewed, list })
            content.Children.Add(element);
        CancellationTokenSource? indexing = null;
        var queryVersion = 0;
        add.Click += async (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "Choose a folder to index" };
            if (picker.ShowDialog(window) != true)
                return;
            try
            {
                var path = DockFileSearch.AddFolder(picker.FolderName);
                ReloadFolders(path);
                await ScanAsync(path);
            }
            catch (ArgumentException error) { status.Text = error.Message; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            { status.Text = "Could not add this folder."; }
        };
        refresh.Click += async (_, _) =>
        {
            if (folders.SelectedItem is string path)
                await ScanAsync(path);
        };
        remove.Click += (_, _) =>
        {
            if (folders.SelectedItem is not string path ||
                MessageBox.Show(window, "Remove this folder and its search excerpts from the index?", "File search",
                    MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                DockFileSearch.RemoveFolder(path);
                ReloadFolders();
                status.Text = "Folder removed from the index.";
                _ = FindAsync();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            { status.Text = "Could not remove this folder."; }
        };
        stop.Click += (_, _) => indexing?.Cancel();
        input.TextChanged += (_, _) => _ = FindAsync();
        share.Click += (_, _) =>
        {
            excerpt.Text = DockFileSearch.ExcerptText(selected);
            review.Visibility = reviewed.Visibility = Visibility.Visible;
            excerpt.Focus();
        };
        attach.Click += (_, _) =>
        {
            if (recipients.SelectedIndex is var slot && slot >= 0 && !string.IsNullOrWhiteSpace(excerpt.Text))
            {
                var conversation = assistantConversations[slot];
                if (conversation.Context != null && MessageBox.Show(window, "Replace the context already attached to this draft?",
                    "Attach excerpts", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                conversation.Context = new AssistantContext("Indexed document excerpts", excerpt.Text.Trim());
                OpenAssistantConversation(slot);
            }
        };
        ReloadFolders();

        void ReloadFolders(string? selected = null)
        {
            try
            {
                var entries = DockFileSearch.Folders();
                folders.ItemsSource = entries;
                folders.SelectedItem = selected ?? entries.FirstOrDefault();
                SetScanning(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            { status.Text = "Could not open the file index."; }
        }

        void SetScanning(bool active)
        {
            add.IsEnabled = !active;
            folders.IsEnabled = refresh.IsEnabled = remove.IsEnabled = !active && folders.SelectedItem != null;
            stop.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active)
            {
                ResetShare();
                list.Children.Clear();
            }
        }

        async Task ScanAsync(string folder)
        {
            if (indexing != null)
                return;
            using var scan = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            indexing = scan;
            SetScanning(true);
            status.Text = "Updating this folder…";
            try
            {
                var result = await Task.Run(() => DockFileSearch.Update(folder, scan.Token), scan.Token);
                if (cancellation.IsCancellationRequested)
                    return;
                status.Text = $"{result.Count} files indexed." +
                    (result.Limited ? " The folder scan reached its limit." : "") +
                    (result.Unreadable > 0 ? $" Could not read {result.Unreadable} files." : "");
                _ = FindAsync();
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { status.Text = "Index update canceled."; }
            catch (OperationCanceledException) { }
            catch (ArgumentException error) { status.Text = error.Message; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            { status.Text = "The folder index could not be updated."; }
            finally { indexing = null; SetScanning(false); }
        }

        async Task FindAsync()
        {
            var version = ++queryVersion;
            var query = input.Text.Trim();
            ResetShare();
            list.Children.Clear();
            if (query.Length == 0)
                return;
            try
            {
                await Task.Delay(150, cancellation);
                if (version != queryVersion || indexing != null)
                    return;
                var matches = await Task.Run(() => DockFileSearch.Search(query), cancellation);
                if (version != queryVersion || cancellation.IsCancellationRequested)
                    return;
                list.Children.Clear();
                foreach (var result in matches)
                {
                    var open = ActionButton(result.Name, () =>
                    {
                        if (File.Exists(result.Path))
                            OpenTarget(result.Path);
                        else
                            status.Text = "This file moved. Update its folder index.";
                    });
                    open.ToolTip = result.Path;
                    open.HorizontalAlignment = HorizontalAlignment.Left;
                    var detail = Text(result.Snippet, 11, AssistantSecondary);
                    detail.TextWrapping = TextWrapping.Wrap;
                    var pick = new CheckBox { Content = "Select excerpt", IsEnabled = !string.IsNullOrWhiteSpace(result.Snippet) };
                    AutomationProperties.SetName(pick, "Select excerpt from " + result.Name);
                    pick.Checked += (_, _) =>
                    {
                        if (selected.Count >= 5)
                        {
                            pick.IsChecked = false;
                            status.Text = "Select up to five excerpts.";
                            return;
                        }
                        selected.Add(result);
                        UpdateShare();
                    };
                    pick.Unchecked += (_, _) => { selected.Remove(result); UpdateShare(); };
                    var card = Card(new StackPanel { Children = { open, detail, pick } });
                    card.Margin = new Thickness(0, 5, 0, 0);
                    list.Children.Add(card);
                }
                if (matches.Length == 0)
                    list.Children.Add(Text("No matching files in the index.", 11, AssistantSecondary));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            { status.Text = "Could not search the file index."; }
        }

        void ResetShare()
        {
            selected.Clear();
            UpdateShare();
        }

        void UpdateShare()
        {
            share.IsEnabled = selected.Count > 0;
            review.Visibility = reviewed.Visibility = Visibility.Collapsed;
            excerpt.Clear();
        }
    }
}
