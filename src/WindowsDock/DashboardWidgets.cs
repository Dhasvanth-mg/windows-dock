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
        var (window, content, _) = CreateDockPanel("Widgets", "", 492, 0xECAA, purple);
        var searchRow = new DockPanel();
        var keyboard = new Border
        {
            Background = Brush("#25150E22"), BorderBrush = Brush("#46D9C3FF"), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4), Child = Text("Ctrl+K", 10, purple, true)
        };
        DockPanel.SetDock(keyboard, Dock.Right);
        searchRow.Children.Add(keyboard);
        searchRow.Children.Add(IconLabel("Search apps and actions", 0xE721, purple));
        var search = MakeButton(searchRow, "Search apps and actions", ShowCommandPalette);
        search.HorizontalAlignment = HorizontalAlignment.Stretch;
        search.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        search.Background = Brush("#202C2341");
        search.BorderBrush = Brush("#40CAB4FF");
        search.Margin = new Thickness(0, 0, 0, 10);
        content.Children.Add(search);
        var items = new Grid();
        items.ColumnDefinitions.Add(new ColumnDefinition());
        items.ColumnDefinitions.Add(new ColumnDefinition());
        var row = 0;
        var column = 0;
        foreach (var widget in WidgetActions())
        {
            if (widget.Wide && column != 0)
            {
                row++;
                column = 0;
            }
            if (column == 0)
                items.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var button = WidgetTile(widget);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            Grid.SetColumnSpan(button, widget.Wide ? 2 : 1);
            button.Margin = widget.Wide ? new Thickness(0, 0, 0, 6) :
                column == 0 ? new Thickness(0, 0, 3, 6) : new Thickness(3, 0, 0, 6);
            items.Children.Add(button);
            if (widget.Wide || column == 1)
            {
                row++;
                column = 0;
            }
            else
                column++;
        }
        content.Children.Add(items);
        window.Show();
    }

    private WidgetAction[] WidgetActions() =>
    [
        new("Audio switchboard", "Outputs, app mixer, and playback", "music media volume speakers headphones",
            0xE767, purple, ShowAudioWidget, Wide: true),
        new("Meetings", "Upcoming calls", "calendar schedule join", 0xE787, accent, ShowMeetingWidget),
        new("Clipboard and files", "Pinned and recent items", "shelf snippets paste screenshots", 0xE77F, green, ShowClipboardWidget),
        new("GitHub checks", "Builds and reviews", "CI pull requests review build", 0xE73E, purple, ShowGitHubWidget),
        new("Workspace shortcuts", "Open a project setup", "projects launch folder editor", 0xE8B7, amber, ShowWorkspaceWidget),
        new("Downloads", "Files and transfers", "queue transfers files", 0xE896, accent, ShowDownloadWidget),
        new("Focus timer", "Sessions and breaks", "timer break do not disturb", 0xE916, green, ShowFocusWidget),
        new("Notifications", "Review and summarize alerts", "alerts summary digest", 0xEA8F, pink, ShowNotificationWidget),
        new("Assistant", "Chat with Gemini or Groq", "AI model provider message", 0xEAB7, purple, ShowAssistantWidget)
    ];

    private Button WidgetTile(WidgetAction widget)
    {
        var line = new DockPanel();
        if (widget.Wide)
        {
            var arrow = DockGlyph(0xE76C, 12, widget.Color);
            DockPanel.SetDock(arrow, Dock.Right);
            line.Children.Add(arrow);
        }
        var badge = GlyphBadge(widget.Icon, widget.Color, 31);
        badge.Margin = new Thickness(0, 0, 8, 0);
        line.Children.Add(badge);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(widget.Label, 12, strong: true));
        var detail = Text(widget.Detail, 10.5, subdued);
        detail.Margin = new Thickness(0, 2, 0, 0);
        labels.Children.Add(detail);
        line.Children.Add(labels);
        var button = MakeButton(line, widget.Label, widget.Action);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Background = AgentArt.Wash(widget.Color, widget.Wide ? (byte)23 : (byte)12);
        button.BorderBrush = AgentArt.Wash(widget.Color, widget.Wide ? (byte)55 : (byte)35);
        button.Padding = new Thickness(8, 5, 9, 5);
        return button;
    }

    private void OpenWidgetPage(DashboardPage page)
    {
        agentPanel?.Close();
        if (compactAgents)
        {
            compactAgents = false;
            ConfigureWidth();
        }
        ShowPage(page);
    }

    private void ShowCommandPalette()
    {
        var (window, content, _) = CreateDockPanel("Search", "", 472, 0xE721, purple, widget: true);
        var search = Field("Search apps and actions", 150);
        search.Tag = "Apps, widgets, Windows settings…";
        search.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(search);
        var list = new StackPanel();
        content.Children.Add(list);
        var options = WidgetActions().ToList();
        foreach (var (label, target) in new[]
        {
            ("Bluetooth settings", "ms-settings:bluetooth"), ("Wi-Fi settings", "ms-settings:network-wifi"),
            ("Display settings", "ms-settings:display"), ("Windows Focus and Do Not Disturb", "ms-settings:quiethours"),
            ("Taskbar settings", "ms-settings:taskbar"), ("Power settings", "ms-settings:powersleep")
        })
            options.Add(new(label, "Windows settings", "windows", 0xE713, accent, () => { window.Close(); OpenTarget(target); }));
        foreach (var app in InstalledAppShortcuts())
            options.Add(new(app.Label, "App", "app", 0xECAA, green, () => { window.Close(); OpenTarget(app.Path); }));
        foreach (var workspace in Workspaces())
            options.Add(new(workspace.Name, "Workspace", "workspace", 0xE8B7, amber, () =>
            {
                window.Close();
                var failed = LaunchWorkspace(workspace);
                if (failed > 0)
                    ShowOwnedMessage($"{failed} workspace item(s) could not open.");
            }));
        WidgetAction[] matches = [];
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
                var button = WidgetTile(item);
                button.Margin = new Thickness(0, 0, 0, 4);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                list.Children.Add(button);
            }
            if (matches.Length == 0)
                list.Children.Add(Text("No matches.", 11, subdued));
        }
    }

    private sealed record WidgetAction(string Label, string Detail, string Keywords, int Icon,
        SolidColorBrush Color, Action Action, bool Wide = false);

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
        var (window, content, _) = CreateDockPanel("Workspaces", "", 470, 0xE8B7, amber, widget: true);
        var list = new StackPanel();
        var editor = new StackPanel { Visibility = Visibility.Collapsed };
        var message = Text("", 11, subdued);
        message.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(list);
        content.Children.Add(message);
        var add = AccentButton("Add workspace", 0xE710, amber, () => Edit(null));
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
                var launch = MakeButton(IconLabel(item.Name, 0xE8B7, amber), item.Name, () =>
                {
                    var failures = LaunchWorkspace(item);
                    message.Text = failures == 0 ? "Workspace opened." : $"{failures} workspace item(s) could not open.";
                });
                launch.HorizontalContentAlignment = HorizontalAlignment.Left;
                launch.ToolTip = item.Folder;
                row.Children.Add(launch);
                var card = Card(row, amber);
                card.Padding = new Thickness(5);
                card.Margin = new Thickness(0, 0, 0, 5);
                list.Children.Add(card);
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
