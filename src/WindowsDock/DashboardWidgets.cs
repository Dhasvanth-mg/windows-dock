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
    private HashSet<string>? favoriteWidgets;
    private TextBox? commandInput;
    private WrapPanel? homeFavorites;

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
        var tiles = new StackPanel();
        content.Children.Add(tiles);
        var widgets = WidgetActions();
        RenderWidgets();
        window.Loaded += (_, _) => search.Focus();
        window.Show();

        void RenderWidgets(string? focused = null)
        {
            tiles.Children.Clear();
            var favorites = FavoriteWidgets();
            var pinned = widgets.Where(item => favorites.Contains(item.Id)).ToArray();
            if (pinned.Length > 0)
                AddGroup(pinned, "Favorites");
            AddGroup(widgets.Where(item => !favorites.Contains(item.Id)).ToArray(), pinned.Length > 0 ? "Other widgets" : null);

            void AddGroup(WidgetAction[] group, string? title)
            {
                if (group.Length == 0)
                    return;
                if (title != null)
                {
                    var heading = Text(title, 11, subdued, true);
                    heading.Margin = new Thickness(1, 1, 0, 5);
                    tiles.Children.Add(heading);
                }
                var items = Columns(1, 1);
                var row = 0;
                var column = 0;
                foreach (var item in group)
                {
                    var isFavorite = favorites.Contains(item.Id);
                    var wide = item.Wide && !isFavorite;
                    if (wide && column != 0)
                    {
                        row++;
                        column = 0;
                    }
                    var button = WidgetTile(item, showArrow: false);
                    var star = IconButton(isFavorite ? 0xE735 : 0xE734,
                        (isFavorite ? "Remove favorite " : "Favorite ") + item.Label, () =>
                        {
                            if (!favorites.Remove(item.Id))
                                favorites.Add(item.Id);
                            WriteJson("widget-favorites.json", favorites);
                            RenderWidgets(item.Id);
                            if (currentPage == DashboardPage.Home)
                                RenderHomeFavorites();
                        });
                    star.Foreground = isFavorite ? amber : subdued;
                    star.Margin = new Thickness(0, 0, 4, 0);
                    var tile = new Grid();
                    tile.Children.Add(button);
                    star.HorizontalAlignment = HorizontalAlignment.Right;
                    tile.Children.Add(star);
                    button.Padding = new Thickness(8, 5, 33, 5);
                    Put(items, tile, row, column, wide ? 2 : 1);
                    if (item.Id == focused)
                        window.Dispatcher.BeginInvoke(() => star.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                    if (wide || column == 1)
                    {
                        row++;
                        column = 0;
                    }
                    else
                        column++;
                }
                items.Margin = new Thickness(0, 0, 0, 6);
                tiles.Children.Add(items);
            }
        }
    }

    private HashSet<string> FavoriteWidgets() => favoriteWidgets ??= ReadJson<HashSet<string>>("widget-favorites.json") ?? [];

    private void RenderHomeFavorites()
    {
        if (homeFavorites == null)
            return;
        var favorites = FavoriteWidgets();
        homeFavorites.Children.Clear();
        foreach (var widget in WidgetActions().Where(item => favorites.Contains(item.Id)))
        {
            var button = AccentButton(widget.Label, widget.Icon, widget.Color, widget.Action);
            button.Margin = new Thickness(0, 0, 5, 4);
            homeFavorites.Children.Add(button);
        }
        homeFavorites.Visibility = homeFavorites.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private WidgetAction[] WidgetActions() =>
    [
        new("Audio switchboard", "Outputs, app mixer, and playback", "music media volume speakers headphones",
            0xE767, purple, ShowAudioWidget, "audio", Wide: true),
        new("Audio presets", "Save and recall mixes", "headphones speaker mixer audio output", 0xE9E9, purple, ShowAudioPresets, "audio-presets"),
        new("Meetings", "Upcoming calls", "calendar schedule join", 0xE787, accent, ShowMeetingWidget, "meetings"),
        new("Clipboard and files", "Pinned and recent items", "shelf snippets paste screenshots", 0xE77F, green, ShowClipboardWidget, "clipboard"),
        new("GitHub checks", "Builds and reviews", "CI pull requests review build", 0xE73E, purple, ShowGitHubWidget, "github"),
        new("Workspace shortcuts", "Open a project setup", "projects launch folder editor", 0xE8B7, amber, ShowWorkspaceWidget, "workspaces"),
        new("Downloads", "Files and transfers", "queue transfers files", 0xE896, accent, ShowDownloadWidget, "downloads"),
        new("Focus timer", "Sessions and breaks", "timer break do not disturb", 0xE916, green, ShowFocusWidget, "focus"),
        new("Notifications", "Review and summarize alerts", "alerts summary digest", 0xEA8F, pink, ShowNotificationWidget, "notifications"),
        new("Assistant", "Choose a role and chat", "AI model provider message", 0xEAB7, purple, ShowAssistantWidget, "assistant"),
        new("Activity inbox", "Replies, approvals, and limits", "unread status finished quota", 0xEA8F, pink, ShowActivityInbox, "activity")
    ];

    private Button WidgetTile(WidgetAction widget, bool showArrow = true)
    {
        var line = new DockPanel();
        if (widget.Wide && showArrow)
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
        if (commandInput is { } current)
        {
            current.Focus();
            current.SelectAll();
            return;
        }
        var (window, content, _) = CreateDockPanel("Search", "", 472, 0xE721, purple, widget: true);
        var search = Field("Search apps and actions", 150);
        commandInput = search;
        window.Closed += (_, _) => commandInput = null;
        search.Tag = "Windows, chats, widgets, and apps…";
        search.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(search);
        var list = new StackPanel();
        content.Children.Add(list);
        var hint = Text("↑ ↓ Select   ·   Enter Open   ·   Esc Close", 10, subdued);
        hint.Margin = new Thickness(0, 7, 0, 0);
        content.Children.Add(hint);
        var favorites = FavoriteWidgets();
        var options = WidgetActions().OrderByDescending(item => favorites.Contains(item.Id)).ToList();
        var emptyResults = options.ToArray();
        foreach (var (page, button) in tabs)
        {
            var label = page == DashboardPage.Agents ? "Coding chats" : AutomationProperties.GetName(button);
            options.Add(new(label, "Dashboard", "dock page", 0xECAA, purple, () => OpenWidgetPage(page)));
        }
        options.Add(new("Token usage", "Coding chats", "quota tokens reset Claude Codex", 0xE9D9, accent, ShowAgentUsage));
        foreach (var chat in RankedSessions(pendingApprovals).Take(40))
        {
            var provider = ProviderName(chat.Provider);
            var detail = $"{provider} · {chat.Project} · {StateBadge(chat.State).Label}";
            options.Add(new(chat.Title, detail, $"chat {provider} {chat.Project}", 0xE943, green, () => OpenPaletteChat(chat)));
        }
        foreach (var (label, target) in new[]
        {
            ("Bluetooth settings", "ms-settings:bluetooth"), ("Wi-Fi settings", "ms-settings:network-wifi"),
            ("Display settings", "ms-settings:display"), ("Windows Focus and Do Not Disturb", "ms-settings:quiethours"),
            ("Taskbar settings", "ms-settings:taskbar"), ("Power settings", "ms-settings:powersleep")
        })
            options.Add(new(label, "Windows settings", "windows", 0xE713, accent, () => { window.Close(); OpenTarget(target); }));
        foreach (var workspace in Workspaces())
            options.Add(new(workspace.Name, "Workspace", "workspace", 0xE8B7, amber, () =>
            {
                window.Close();
                var failed = LaunchWorkspace(workspace);
                if (failed > 0)
                    ShowOwnedMessage($"{failed} workspace item(s) could not open.");
            }));
        var apps = new Lazy<WidgetAction[]>(() => InstalledAppShortcuts().Select(app =>
            new WidgetAction(app.Label, "App", "app", 0xECAA, green, () => { window.Close(); OpenTarget(app.Path); })).ToArray());
        var openWindows = new Lazy<WidgetAction[]>(() => ShellIntegration.Windows().Select(target =>
        {
            var app = Path.GetFileNameWithoutExtension(target.Path);
            return new WidgetAction(target.Title, app.Length == 0 ? "Open window" : $"Open window · {app}",
                "open window running switch " + app, 0xE737, accent, () =>
                {
                    if (ShellIntegration.Activate(target))
                        Dismiss();
                    else
                    {
                        hint.Text = "Could not switch to this window. Reopen search to refresh the list.";
                        hint.Foreground = amber;
                    }
                });
        }).ToArray());
        WidgetAction[] matches = [];
        var selected = -1;
        search.TextChanged += (_, _) => Render();
        window.PreviewKeyDown += (_, args) =>
        {
            var fromSearch = search.IsKeyboardFocusWithin;
            var fromResult = list.IsKeyboardFocusWithin;
            if (!fromSearch && !fromResult)
                return;
            if (args.Key == Key.Enter && selected >= 0)
            {
                args.Handled = true;
                matches[selected].Action();
            }
            else if ((args.Key is Key.Down or Key.Up) && selected >= 0)
            {
                args.Handled = true;
                var index = fromSearch && args.Key == Key.Down ? 0 :
                    Math.Clamp(selected + (args.Key == Key.Down ? 1 : -1), 0, matches.Length - 1);
                if (fromResult && selected == 0 && args.Key == Key.Up)
                    search.Focus();
                else if (list.Children[index] is Button result)
                {
                    result.Focus();
                    result.BringIntoView();
                }
            }
        };
        Render();
        window.Loaded += (_, _) => Keyboard.Focus(search);
        window.Show();

        void Render()
        {
            var query = search.Text.Trim();
            var windowsOnly = query.StartsWith("window:", StringComparison.OrdinalIgnoreCase);
            if (windowsOnly)
                query = query[7..].Trim();
            hint.Text = "↑ ↓ Select   ·   Enter Open   ·   Esc Close";
            hint.Foreground = subdued;
            matches = query.Length == 0
                ? windowsOnly ? openWindows.Value.Take(10).ToArray() : emptyResults
                : (windowsOnly ? openWindows.Value : openWindows.Value.Concat(options).Concat(apps.Value))
                .Select(item => (Item: item, Score: SearchScore(item, query)))
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score).Take(10).Select(match => match.Item).ToArray();
            list.Children.Clear();
            for (var index = 0; index < matches.Length; index++)
            {
                var row = index;
                var button = WidgetTile(matches[index], showArrow: false);
                button.Margin = new Thickness(0, 0, 0, 4);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.GotKeyboardFocus += (_, _) => Select(row);
                list.Children.Add(button);
            }
            selected = -1;
            if (matches.Length > 0)
                Select(0);
            if (matches.Length == 0)
                list.Children.Add(Text("No matches.", 11, subdued));
        }

        void Select(int index)
        {
            if (selected >= 0)
            {
                var previous = (Button)list.Children[selected];
                previous.Background = AgentArt.Wash(matches[selected].Color, 12);
                previous.BorderBrush = AgentArt.Wash(matches[selected].Color, 35);
            }
            var button = (Button)list.Children[index];
            button.Background = AgentArt.Wash(purple, 45);
            button.BorderBrush = AgentArt.Wash(purple, 100);
            selected = index;
        }
    }

    private static int SearchScore(WidgetAction item, string query)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (item.Label.Equals(query, comparison)) return 500;
        if (item.Label.StartsWith(query, comparison)) return 400;
        if (item.Label.Contains(query, comparison)) return 300;
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.All(word => item.Label.Contains(word, comparison))) return 220;
        var searchable = $"{item.Label} {item.Keywords} {item.Detail}";
        return words.All(word => searchable.Contains(word, comparison)) ? 100 : 0;
    }

    private void OpenPaletteChat(AgentSession chat)
    {
        agentPanel?.Close();
        selectedAgent = (chat.Provider, chat.Id);
        agentSetup = false;
        showAgentList = true;
        agentFilter = null;
        agentSlot = -1;
        RefreshAgentPage();
    }

    private sealed record WidgetAction(string Label, string Detail, string Keywords, int Icon,
        SolidColorBrush Color, Action Action, string Id = "", bool Wide = false);

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
