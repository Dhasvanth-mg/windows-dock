using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private List<WorkspaceShortcut>? workspaceShortcuts;

    private void ShowWidgets()
    {
        var (window, content, _) = CreateAgentPanel("Widgets", "", 466);
        var search = WidgetButton("Search apps and actions", 0xE721, ShowCommandPalette);
        search.HorizontalAlignment = HorizontalAlignment.Stretch;
        search.Margin = new Thickness(0, 0, 0, 9);
        content.Children.Add(search);
        var items = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (label, _, icon, action) in WidgetActions(window))
        {
            var button = WidgetButton(label, icon, action);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.MinHeight = 34;
            button.Margin = new Thickness(2, 0, 2, 5);
            items.Children.Add(button);
        }
        content.Children.Add(items);
        window.Show();
    }

    private (string Label, string Keywords, int Icon, Action Action)[] WidgetActions(Window? from = null) =>
    [
        ("Audio switchboard", "music media volume speakers headphones", 0xE767, () => OpenWidgetPage(DashboardPage.Media, from)),
        ("Meetings", "calendar schedule join", 0xE787, ShowMeetingWidget),
        ("Clipboard and files", "shelf snippets paste screenshots", 0xE77F, ShowClipboardWidget),
        ("GitHub checks", "CI pull requests review build", 0xE73E, ShowGitHubWidget),
        ("Workspace shortcuts", "projects launch folder editor", 0xE8B7, ShowWorkspaceWidget),
        ("Downloads", "queue transfers files", 0xE896, ShowDownloadWidget),
        ("Focus timer", "timer break do not disturb", 0xE916, () => OpenWidgetPage(DashboardPage.Calendar, from)),
        ("Notifications", "alerts summary digest", 0xE7F4, ShowNotificationWidget)
    ];

    private Button WidgetButton(string label, int icon, Action action)
    {
        var line = new DockPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var symbol = Text(char.ConvertFromUtf32(icon), 13, accent);
        symbol.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        symbol.Width = 25;
        symbol.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(symbol);
        var name = Text(label, 12, strong: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(name);
        var button = MakeButton(line, label, action);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Background = Brush("#10FFFFFF");
        button.BorderBrush = Brush("#16FFFFFF");
        return button;
    }

    private void OpenWidgetPage(DashboardPage page, Window? from = null)
    {
        from?.Close();
        if (compactAgents)
            OpenMainDashboard();
        if (page == DashboardPage.Calendar)
            showReminders = false;
        if (page == DashboardPage.Media)
            lyricsOpen = false;
        ShowPage(page);
    }

    private void ShowCommandPalette()
    {
        var (window, content, _) = CreateAgentPanel("Search", "", 472);
        var search = Field("Search apps and actions", 150);
        search.Tag = "Apps, widgets, Windows settings…";
        search.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(search);
        var list = new StackPanel();
        content.Children.Add(list);
        var options = new List<(string Label, string Keywords, Action Action)>(
            WidgetActions(window).Select(item => (item.Label, item.Keywords, item.Action)));
        foreach (var (label, target) in new[]
        {
            ("Bluetooth settings", "ms-settings:bluetooth"), ("Wi-Fi settings", "ms-settings:network-wifi"),
            ("Display settings", "ms-settings:display"), ("Windows Focus and Do Not Disturb", "ms-settings:quiethours"),
            ("Taskbar settings", "ms-settings:taskbar"), ("Power settings", "ms-settings:powersleep")
        })
            options.Add((label, "windows", () => { window.Close(); OpenTarget(target); }));
        foreach (var app in InstalledAppShortcuts())
            options.Add((app.Label, "app", () => { window.Close(); OpenTarget(app.Path); }));
        foreach (var workspace in Workspaces())
            options.Add((workspace.Name, "workspace", () =>
            {
                window.Close();
                var failed = LaunchWorkspace(workspace);
                if (failed > 0)
                    ShowOwnedMessage($"{failed} workspace item(s) could not open.");
            }));
        (string Label, string Keywords, Action Action)[] matches = [];
        search.TextChanged += (_, _) => Render();
        search.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && matches.Length > 0)
            {
                args.Handled = true;
                matches[0].Action();
            }
            else if (args.Key == Key.Down && list.Children.Count > 0 && list.Children[0] is Button first)
            {
                args.Handled = true;
                first.Focus();
            }
        };
        Render();
        window.Loaded += (_, _) => search.Focus();
        window.Show();

        void Render()
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            matches = options.Where(item => words.All(word => item.Label.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                item.Keywords.Contains(word, StringComparison.OrdinalIgnoreCase))).Take(10).ToArray();
            list.Children.Clear();
            foreach (var item in matches)
            {
                var button = ActionButton(item.Label, item.Action);
                button.Margin = new Thickness(0, 0, 0, 4);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                list.Children.Add(button);
            }
            if (matches.Length == 0)
                list.Children.Add(Text("No matches.", 11, subdued));
        }
    }

    private static IEnumerable<(string Label, string Path)> InstalledAppShortcuts()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var path = Environment.GetFolderPath(folder);
            if (!Directory.Exists(path))
                continue;
            string[] files;
            try
            {
                files = Directory.GetFiles(path, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 4
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
                if (names.Add(Path.GetFileNameWithoutExtension(file)))
                    yield return (Path.GetFileNameWithoutExtension(file), file);
        }
    }

    private List<WorkspaceShortcut> Workspaces() => workspaceShortcuts ??= ReadJson<List<WorkspaceShortcut>>("workspaces.json") ?? [];

    private void ShowWorkspaceWidget()
    {
        var (window, content, _) = CreateAgentPanel("Workspaces", "", 470);
        var list = new StackPanel();
        var editor = new StackPanel { Visibility = Visibility.Collapsed };
        var message = Text("", 11, subdued);
        message.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(list);
        content.Children.Add(message);
        var add = ActionButton("Add workspace", () => Edit(null));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 6, 0, 4);
        content.Children.Add(add);
        content.Children.Add(editor);
        Render();
        window.Show();

        void Render()
        {
            list.Children.Clear();
            foreach (var item in Workspaces())
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                var edit = IconButton(0xE70F, "Edit " + item.Name, () => Edit(item));
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
                var launch = ActionButton(item.Name, () =>
                {
                    var failures = LaunchWorkspace(item);
                    message.Text = failures == 0 ? "Workspace opened." : $"{failures} workspace item(s) could not open.";
                });
                launch.HorizontalAlignment = HorizontalAlignment.Left;
                launch.ToolTip = item.Folder;
                row.Children.Add(launch);
                list.Children.Add(row);
            }
            if (Workspaces().Count == 0)
                list.Children.Add(Text("Group a project folder, websites, and apps into one shortcut.", 11, subdued));
        }

        void Edit(WorkspaceShortcut? existing)
        {
            editor.Children.Clear();
            editor.Visibility = Visibility.Visible;
            var name = AddField("Workspace name", existing?.Name, 70);
            var folder = AddField("Project folder", existing?.Folder, 512);
            var browse = ActionButton("Choose folder…", () =>
            {
                var dialog = new OpenFolderDialog();
                if (dialog.ShowDialog(window) == true)
                    folder.Text = dialog.FolderName;
            });
            browse.HorizontalAlignment = HorizontalAlignment.Left;
            browse.Margin = new Thickness(0, 4, 0, 0);
            editor.Children.Add(browse);
            var targets = AddField("Websites or app paths, one per line", existing == null ? "" : string.Join("\n", existing.Targets), 4096);
            targets.AcceptsReturn = true;
            targets.Height = 69;
            targets.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            var save = ActionButton("Save workspace", () =>
            {
                var title = name.Text.Trim();
                var directory = folder.Text.Trim();
                var links = targets.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
                if (title.Length == 0 || directory.Length != 0 && !Directory.Exists(directory) ||
                    links.Length > 12 || links.Any(link => !WorkspaceTarget(link)) || directory.Length == 0 && links.Length == 0)
                {
                    message.Text = "Enter a name and a valid folder, HTTPS link, or app path.";
                    return;
                }
                if (existing == null && Workspaces().Count >= 30)
                {
                    message.Text = "Remove a workspace before adding another.";
                    return;
                }
                var item = new WorkspaceShortcut(title, directory, links);
                if (existing == null)
                    Workspaces().Add(item);
                else
                    Workspaces()[Workspaces().IndexOf(existing)] = item;
                Persist();
            });
            save.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Add(save);
            if (existing != null)
                actions.Children.Add(ActionButton("Remove", () => { Workspaces().Remove(existing); Persist(); }));
            editor.Children.Add(actions);
            name.Focus();
        }

        TextBox AddField(string label, string? value, int limit)
        {
            var caption = Text(label, 10, subdued);
            caption.Margin = new Thickness(0, 6, 0, 2);
            editor.Children.Add(caption);
            var field = Field(label, limit);
            field.Text = value ?? "";
            editor.Children.Add(field);
            return field;
        }

        void Persist()
        {
            WriteJson("workspaces.json", Workspaces());
            editor.Visibility = Visibility.Collapsed;
            message.Text = "";
            Render();
        }
    }

    private static bool WorkspaceTarget(string target) =>
        Path.IsPathFullyQualified(target) && (File.Exists(target) || Directory.Exists(target)) ||
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0;

    private int LaunchWorkspace(WorkspaceShortcut shortcut)
    {
        var failures = 0;
        foreach (var target in new[] { shortcut.Folder }.Concat(shortcut.Targets).Where(target => target.Length > 0))
        {
            if (!WorkspaceTarget(target))
            {
                failures++;
                continue;
            }
            try { ShellIntegration.Launch(target); }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException) { failures++; }
        }
        return failures;
    }

    private sealed record WorkspaceShortcut(string Name, string Folder, string[] Targets);
}
