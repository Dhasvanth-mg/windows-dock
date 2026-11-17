using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    private TextBox? commandInput;
    private WrapPanel? homeFavorites;

    private void ShowWidgets() => OpenWidgetPage(DashboardPage.Widgets);

    private UIElement WidgetsPage()
    {
        var content = new StackPanel();
        var widgets = WidgetActions();
        var layout = DockWidgetLayout.Load();
        var pinPicker = new ComboBox { ItemsSource = widgets, DisplayMemberPath = nameof(WidgetAction.Label),
            SelectedItem = widgets.First(item => item.Id == layout.Pinned), MinWidth = 145 };
        AutomationProperties.SetName(pinPicker, "Pinned widget");
        var pinRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var expand = IconButton(0xE8A7, "Open pinned widget", () => (pinPicker.SelectedItem as WidgetAction)?.Action());
        expand.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(expand, Dock.Right);
        pinRow.Children.Add(expand);
        DockPanel.SetDock(pinPicker, Dock.Right);
        pinRow.Children.Add(pinPicker);
        var pinnedTitle = Text("Pinned widget", 13, AssistantInk, true);
        pinnedTitle.Margin = new Thickness(0);
        pinnedTitle.VerticalAlignment = VerticalAlignment.Center;
        pinRow.Children.Add(pinnedTitle);
        content.Children.Add(pinRow);
        var pinned = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 164,
            PanningMode = PanningMode.VerticalOnly };
        content.Children.Add(new Border { Child = pinned, Background = SystemParameters.HighContrast ? null : assistantInset,
            CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 14) });
        var favoritesHeader = new DockPanel();
        var saveLayout = IconButton(0xE74E, "Export widget layout");
        var loadLayout = IconButton(0xE8E5, "Import widget layout");
        DockPanel.SetDock(saveLayout, Dock.Right);
        DockPanel.SetDock(loadLayout, Dock.Right);
        favoritesHeader.Children.Add(saveLayout);
        favoritesHeader.Children.Add(loadLayout);
        favoritesHeader.Children.Add(Text("Favorites", 13, AssistantInk, true));
        content.Children.Add(favoritesHeader);
        var favorites = new ContentControl { Margin = new Thickness(0, 6, 0, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(favorites);
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 0, 0, 8);
        status.Visibility = Visibility.Collapsed;
        content.Children.Add(status);
        content.Children.Add(Text("All widgets", 13, AssistantInk, true));
        var catalog = new ContentControl { Margin = new Thickness(0, 4, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(catalog);

        CancellationTokenSource? mounted = null;
        saveLayout.Click += (_, _) => ExportLayout();
        loadLayout.Click += (_, _) => ImportLayout();
        content.Loaded += (_, _) => UpdateMount();
        content.Unloaded += (_, _) => ReleasePinned();
        content.IsVisibleChanged += (_, _) => UpdateMount();
        content.IsHitTestVisibleChanged += (_, _) => UpdateMount();
        pinPicker.SelectionChanged += (_, _) =>
        {
            if (pinPicker.SelectedItem is not WidgetAction selected || selected.Id == layout.Pinned)
                return;
            try
            {
                layout = DockWidgetLayout.Pin(selected.Id);
                ShowStatus("");
                ReleasePinned();
                UpdateMount();
                RenderShortcuts();
                RenderHomeFavorites();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                pinPicker.SelectedItem = widgets.First(item => item.Id == layout.Pinned);
                ShowStatus("Could not save the pinned widget.");
            }
        };

        RenderShortcuts();
        return content;

        void ReleasePinned()
        {
            if (mounted is not { } previous)
                return;
            mounted = null;
            previous.Cancel();
            previous.Dispose();
            pinned.Content = null;
        }

        void UpdateMount()
        {
            if (!content.IsLoaded || !content.IsVisible || !content.IsHitTestVisible)
            {
                ReleasePinned();
                return;
            }
            if (mounted != null)
                return;
            mounted = new();
            var body = new StackPanel();
            pinned.Content = body;
            BuildPinnedWidget(layout.Pinned, this, body, mounted.Token);
            pinned.ScrollToTop();
        }

        void ShowStatus(string message)
        {
            status.Text = message;
            status.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void ExportLayout()
        {
            var picker = new SaveFileDialog { Filter = "Widget layout|*.json", FileName = "Windows Dock widgets.json" };
            if (picker.ShowDialog(this) != true)
                return;
            try { File.WriteAllText(picker.FileName, DockWidgetLayout.Export()); ShowStatus("Widget layout exported."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { ShowStatus("Could not export the widget layout."); }
        }

        void ImportLayout()
        {
            var picker = new OpenFileDialog { Filter = "Widget layout|*.json", CheckFileExists = true };
            if (picker.ShowDialog(this) != true)
                return;
            try
            {
                layout = DockWidgetLayout.Import(picker.FileName);
                pinPicker.SelectedItem = widgets.First(item => item.Id == layout.Pinned);
                ReleasePinned();
                UpdateMount();
                RenderShortcuts();
                RenderHomeFavorites();
                ShowStatus("Widget layout imported.");
            }
            catch (ArgumentException error) { ShowStatus(error.Message); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { ShowStatus("Could not read the widget layout."); }
        }

        void RenderShortcuts(string? focused = null)
        {
            favorites.Content = layout.Favorites.Length == 0
                ? Text("Star a widget to add it here.", 12, AssistantSecondary)
                : Group(layout.Favorites.Select(id => widgets.First(item => item.Id == id)), true);
            catalog.Content = Group(widgets.Where(item => item.Id != layout.Pinned && !layout.Favorites.Contains(item.Id)), false);

            Grid Group(IEnumerable<WidgetAction> group, bool favorite)
            {
                var columnCount = Width >= 840 ? (favorite ? 4 : 3) : 2;
                var items = Columns(Enumerable.Repeat(1.0, columnCount).ToArray());
                var index = 0;
                foreach (var item in group)
                {
                    var button = favorite
                        ? MakeButton(IconLabel(item.Label, item.Icon, AssistantSecondary), item.Label, item.Action)
                        : WidgetTile(item, showArrow: false);
                    button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    button.Padding = new Thickness(8, favorite ? 7 : 5, 33, favorite ? 7 : 5);
                    if (!favorite)
                    {
                        button.Background = Brushes.Transparent;
                        button.BorderBrush = Brushes.Transparent;
                    }
                    ToolTipService.SetIsEnabled(button, false);
                    var star = IconButton(favorite ? 0xE735 : 0xE734,
                        (favorite ? "Remove favorite " : "Favorite ") + item.Label, () =>
                        {
                            try
                            {
                                layout = DockWidgetLayout.SetFavorite(item.Id, !favorite);
                                ShowStatus(!favorite && !layout.Favorites.Contains(item.Id)
                                    ? "Remove a favorite to make room." : "");
                                RenderShortcuts(item.Id);
                                RenderHomeFavorites();
                            }
                            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                            {
                                ShowStatus("Could not save favorites.");
                            }
                        });
                    ((TextBlock)star.Content).Foreground = favorite ? AssistantInk : AssistantFaint;
                    star.HorizontalAlignment = HorizontalAlignment.Right;
                    star.VerticalAlignment = VerticalAlignment.Center;
                    star.Margin = new Thickness(0, 0, 3, 0);
                    var row = new Grid();
                    row.Children.Add(button);
                    row.Children.Add(star);
                    if (favorite && layout.Favorites.Length > 1)
                    {
                        var menu = new ContextMenu();
                        var earlier = new MenuItem { Header = "Move earlier", IsEnabled = index > 0 };
                        var later = new MenuItem { Header = "Move later", IsEnabled = index < layout.Favorites.Length - 1 };
                        earlier.Click += (_, _) => MoveFavorite(item.Id, -1);
                        later.Click += (_, _) => MoveFavorite(item.Id, 1);
                        menu.Items.Add(earlier);
                        menu.Items.Add(later);
                        row.ContextMenu = menu;
                    }
                    if (item.Id == focused)
                        Dispatcher.BeginInvoke(() => star.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                    Put(items, row, index / columnCount, index % columnCount);
                    index++;
                }
                return items;
            }
        }

        void MoveFavorite(string id, int direction)
        {
            try
            {
                layout = DockWidgetLayout.MoveFavorite(id, direction);
                RenderShortcuts(id);
                RenderHomeFavorites();
                ShowStatus("");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { ShowStatus("Could not reorder the favorites."); }
        }
    }

    private void BuildPinnedWidget(string id, Window window, StackPanel content, System.Threading.CancellationToken cancellation)
    {
        switch (id)
        {
            case "audio": BuildPinnedAudio(content, cancellation, compact: true); break;
            case "audio-presets": BuildPinnedAudioPresets(content); break;
            case "meetings": BuildPinnedMeetings(window, content, cancellation); break;
            case "clipboard": BuildPinnedClipboard(content, cancellation); break;
            case "github": BuildPinnedGitHub(content, cancellation); break;
            case "workspaces": BuildPinnedWorkspaces(window, content); break;
            case "downloads": BuildPinnedDownloads(window, content, cancellation); break;
            case "focus": BuildPinnedFocus(content, cancellation); break;
            case "notifications": BuildPinnedNotifications(content, cancellation); break;
            case "activity": BuildPinnedActivity(window, content, cancellation); break;
            case "jobs": BuildPinnedJobs(content, cancellation); break;
            case "calculator": BuildCalculator(content); break;
            case "wifi": BuildWireless(content, Windows.Devices.Radios.RadioKind.WiFi, cancellation); break;
            case "bluetooth": BuildWireless(content, Windows.Devices.Radios.RadioKind.Bluetooth, cancellation); break;
            case "favorites": BuildPinnedFavorites(content); break;
            case "voice": BuildVoiceMemos(window, content, cancellation); break;
            case "file-search": BuildFileSearch(window, content, cancellation); break;
            case "assistant":
                var grid = Columns(1, 1);
                for (var index = 0; index < AssistantPersonas.All.Length; index++)
                {
                    var slot = index;
                    var persona = AssistantPersonas.All[slot];
                    var button = MakeButton(IconLabel(persona.Label, 0xEAB7, PersonaTint(persona)), persona.Label,
                        () => OpenAssistantConversation(slot));
                    button.HorizontalContentAlignment = HorizontalAlignment.Left;
                    Put(grid, button, slot / 2, slot % 2);
                }
                content.Children.Add(grid);
                break;
        }
    }

    private static string[] FavoriteWidgets() => DockWidgetLayout.Load().Favorites;

    private void RenderHomeFavorites()
    {
        if (homeFavorites == null)
            return;
        var favorites = FavoriteWidgets();
        homeFavorites.Children.Clear();
        var widgets = WidgetActions();
        foreach (var widget in favorites.Select(id => widgets.First(item => item.Id == id)))
        {
            var button = AccentButton(widget.Label, widget.Icon, widget.Color, widget.Action);
            button.Margin = new Thickness(0, 0, 5, 4);
            homeFavorites.Children.Add(button);
        }
        homeFavorites.Visibility = homeFavorites.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private WidgetAction[] WidgetActions() =>
    [
        new("Audio switchboard", "Outputs, app volumes and playback", "music media volume speakers headphones",
            0xE767, purple, ShowAudioWidget, "audio", Wide: true),
        new("Audio presets", "Save and recall mixes", "headphones speaker mixer audio output", 0xE9E9, purple, ShowAudioPresets, "audio-presets"),
        new("Meetings", "Calendar events and calls", "calendar schedule join events", 0xE787, accent, ShowMeetingWidget, "meetings"),
        new("Clipboard and files", "Pinned and recent items", "shelf snippets paste screenshots", 0xE77F, green, ShowClipboardWidget, "clipboard"),
        new("GitHub checks", "Builds and reviews", "CI pull requests review build", 0xE73E, purple, ShowGitHubWidget, "github"),
        new("Workspace shortcuts", "Open a project setup", "projects launch folder editor", 0xE8B7, amber, ShowWorkspaceWidget, "workspaces"),
        new("Downloads", "Files and transfers", "queue transfers files", 0xE896, accent, ShowDownloadWidget, "downloads"),
        new("Focus timer", "Sessions and breaks", "timer break do not disturb", 0xE916, green, ShowFocusWidget, "focus"),
        new("Notifications", "Review and summarize alerts", "alerts summary digest", 0xEA8F, pink, ShowNotificationWidget, "notifications"),
        new("Assistant", "Lark, Fern, Bolt or Orbit", "AI model provider message research planning coding casual", 0xEAB7, purple, ShowAssistantPicker, "assistant"),
        new("Activity inbox", "Replies, approvals, and limits", "unread status finished quota", 0xEA8F, pink, ShowActivityInbox, "activity"),
        new("Assistant jobs", "Running tasks and saved results", "background delegate coding writing Scribe research", 0xE823, amber, ShowAssistantJobs, "jobs"),
        new("Calculator", "Calculate, convert, and save results", "calc math percentages units history favorites", 0xE8EF, accent, ShowCalculator, "calculator"),
        new("Wi-Fi networks", "Signal, saved networks, and connection", "wifi wireless connections", 0xE701, accent, ShowWifiWidget, "wifi"),
        new("Bluetooth devices", "Paired devices and radio controls", "bluetooth pairing headphones", 0xE702, accent, ShowBluetoothWidget, "bluetooth"),
        new("Favorites", "Files, links, workspaces, and shortcuts", "favorites bookmarks pin", 0xE734, accent, ShowFavoritesWidget, "favorites"),
        new("Voice memos", "Record, play, and transcribe", "microphone audio speech dictation recording", 0xE720, accent, ShowVoiceMemos, "voice"),
        new("File search", "Find words in folders you choose", "files search folders index documents", 0xE721, accent, ShowFileSearch, "file-search")
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
        labels.Children.Add(Text(widget.Label, 13));
        var detail = Text(widget.Detail, 11, AssistantFaint);
        detail.Margin = new Thickness(0, 1, 0, 0);
        labels.Children.Add(detail);
        line.Children.Add(labels);
        var button = MakeButton(line, widget.Label, widget.Action);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(8, 5, 9, 5);
        return button;
    }

    private void OpenWidgetPage(DashboardPage page)
    {
        OpenMainDashboard();
        if (currentPage != page || !pageInitialized)
            ShowPage(page);
        if (!IsVisible)
            Show();
        Activate();
    }

    internal void ShowCommandPalette() => ShowCommandPalette(null);

    private void ShowCommandPalette(string? initialQuery)
    {
        if (commandInput is { } current)
        {
            if (openPanels.FirstOrDefault(panel => panel.Window == Window.GetWindow(current)) is { } panel)
                ShowTrackedPanel(panel);
            if (initialQuery != null)
                current.Text = initialQuery;
            current.Focus();
            current.SelectAll();
            return;
        }
        var (window, content, _) = CreateDockPanel("Search", "", 472, 0xE721, purple, widget: true);
        var search = Field("Search apps and actions", 150);
        commandInput = search;
        window.Closed += (_, _) => commandInput = null;
        search.Tag = "Search windows, chats, widgets and apps";
        search.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(search);
        var list = new StackPanel();
        content.Children.Add(list);
        var hint = Text("↑ ↓ to move, Enter to open. Start with window: to search open windows only.", 11, muted);
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
        // Things that used to be pages of their own still answer to their old names.
        foreach (var (label, where, words, page) in new[]
        {
            ("Calendar and reminders", "Today", "week agenda schedule date", DashboardPage.Home),
            ("Weather", "Today", "forecast temperature rain", DashboardPage.Home),
            ("Weather location and units", "Controls", "city celsius fahrenheit", DashboardPage.Controls),
            ("Sound mixer", "Controls", "audio volume output speakers headphones app lyrics media", DashboardPage.Controls),
            ("Wi-Fi and Bluetooth", "Controls", "network radio wireless", DashboardPage.Controls),
            ("Accent colour", "Controls", "color theme appearance", DashboardPage.Controls),
            ("Dock settings", "Controls", "start sign-in startup restore taskbar exit", DashboardPage.Controls),
            ("System and battery", "Desk", "stats cpu memory drive network power uptime lock", DashboardPage.Desk),
            ("Files", "Desk", "shelf drop drag", DashboardPage.Desk),
            ("Wallpapers", "Desk", "background image desktop", DashboardPage.Desk),
            ("Games", "Play", "snake copycat memory fun", DashboardPage.Play)
        })
            options.Add(new(label, where, words, 0xECAA, purple, () => OpenWidgetPage(page)));
        options.Add(new("Token usage", "Coding chats", "quota tokens reset Claude Codex", 0xE9D9, accent, ShowAgentUsage));
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[slot];
            options.Add(new(persona.Name, persona.Role, $"assistant {persona.Role}", 0xEAB7,
                PersonaTint(persona), () => OpenAssistantConversation(slot)));
        }
        foreach (var chat in RankedSessions(pendingApprovals).Take(40))
        {
            var provider = ProviderName(chat.Provider);
            var detail = $"{StateBadge(chat.State).Label}, {provider}" + (string.IsNullOrWhiteSpace(chat.Project) ? "" : " in " + chat.Project);
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
            options.Add(new(workspace.Name, "Workspace", "workspace open restore", 0xE8B7, amber, async () =>
            {
                hint.Text = "Restoring " + workspace.Name + "…";
                var failed = await LaunchWorkspaceAsync(workspace);
                hint.Text = failed == 0 ? "Workspace opened." : $"{failed} item(s) could not be restored.";
            }));
        foreach (var favorite in DockFavorites.Read())
            options.Add(new(favorite.Name, favorite.Folder.Length > 0 ? "Favorite · " + favorite.Folder : "Favorite",
                "favorite " + favorite.Target, 0xE734, accent, () => ActivateFavorite(favorite, hint)));
        var apps = new Lazy<WidgetAction[]>(() => InstalledAppShortcuts().Select(app =>
            new WidgetAction(app.Label, "App", "app", 0xECAA, green, () => { window.Close(); OpenTarget(app.Path); })).ToArray());
        var openWindows = new Lazy<WidgetAction[]>(() => ShellIntegration.Windows().Select(target =>
        {
            var app = Path.GetFileNameWithoutExtension(target.Path);
            return new WidgetAction(target.Title, app.Length == 0 ? "Open window" : $"Open window in {app}",
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
        search.Text = initialQuery ?? "";
        window.Loaded += (_, _) => Keyboard.Focus(search);
        window.Show();

        void Render()
        {
            var query = search.Text.Trim();
            var windowsOnly = query.StartsWith("window:", StringComparison.OrdinalIgnoreCase);
            if (windowsOnly)
                query = query[7..].Trim();
            hint.Text = "↑ ↓ to move, Enter to open. Start with window: to search open windows only.";
            hint.Foreground = muted;
            DockCalculation? calculation = null;
            string? calculationError = null;
            var calculated = !windowsOnly && DockCalculator.TryEvaluate(query, out calculation, out calculationError);
            if (calculated && calculation != null)
            {
                var result = calculation;
                matches =
                [
                    new(result.Result, result.Detail + " · Enter to copy", "", 0xE8EF, accent, () => CopyCalculation(result, hint)),
                    new("Save calculation", "Add this result to calculator favorites", "", 0xE734, accent, () => SaveCalculation(result, true, hint)),
                    new("Calculator", "History and favorites", "", 0xE8EF, accent, ShowCalculator)
                ];
            }
            else if (calculationError != null)
                matches = [new("Calculator", calculationError, "", 0xE8EF, accent, ShowCalculator)];
            else
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
                button.Margin = new Thickness(0, 0, 0, 1);
                button.Background = button.BorderBrush = Brushes.Transparent;
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.GotKeyboardFocus += (_, _) => Select(row);
                list.Children.Add(button);
            }
            selected = -1;
            if (matches.Length > 0)
                Select(0);
            if (matches.Length == 0)
                list.Children.Add(Text("Nothing matches that.", 12, muted));
        }

        void Select(int index)
        {
            if (selected >= 0)
            {
                var previous = (Button)list.Children[selected];
                previous.Background = Brushes.Transparent;
                previous.BorderBrush = Brushes.Transparent;
            }
            var button = (Button)list.Children[index];
            button.Background = controlSelected;
            button.BorderBrush = controlSelected;
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
        agentSlot = -1;
        ShowCodingWorkspace().Select(chat);
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
        BuildPinnedWorkspaces(window, content);
        window.Show();
    }

    private void BuildPinnedWorkspaces(Window window, StackPanel content)
    {
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

        void Render()
        {
            list.Children.Clear();
            foreach (var item in Workspaces())
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                var edit = IconButton(0xE70F, "Edit " + item.Name, () => Edit(item));
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
                var launch = MakeButton(IconLabel(item.Name, 0xE8B7, amber), item.Name, async () =>
                {
                    message.Text = "Restoring " + item.Name + "…";
                    var failures = await LaunchWorkspaceAsync(item);
                    message.Text = failures == 0 ? "Workspace opened." : $"{failures} item(s) could not be restored.";
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
                list.Children.Add(Text("Group a project folder, websites and apps so they open together.", 12, subdued));
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
            var windows = existing?.Windows ?? [];
            var captureStatus = Text("", 11, subdued);
            captureStatus.TextWrapping = TextWrapping.Wrap;
            var capture = ActionButton("Capture open windows", () =>
            {
                windows = DockWorkspaces.Capture();
                ShowCapture();
            });
            capture.HorizontalAlignment = HorizontalAlignment.Left;
            editor.Children.Add(capture);
            editor.Children.Add(captureStatus);
            editor.Children.Add(ActionButton("Clear captured windows", () => { windows = []; ShowCapture(); }));
            ShowCapture();
            var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            var save = ActionButton("Save workspace", () =>
            {
                var title = name.Text.Trim();
                var directory = folder.Text.Trim();
                var links = targets.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
                if (title.Length == 0 || directory.Length != 0 && !Directory.Exists(directory) ||
                    links.Length > 12 || links.Any(link => !WorkspaceTarget(link)) || directory.Length == 0 && links.Length == 0 && windows.Length == 0)
                {
                    message.Text = "Name the workspace and add a folder, link, app, or captured window.";
                    return;
                }
                if (existing == null && Workspaces().Count >= 30)
                {
                    message.Text = "Remove a workspace before adding another.";
                    return;
                }
                var item = new WorkspaceShortcut(title, directory, links, windows);
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

            void ShowCapture()
            {
                captureStatus.Text = windows.Length == 0 ? "No windows captured." :
                    $"{windows.Length} captured · " + string.Join(", ", windows.Select(item => Path.GetFileNameWithoutExtension(item.Path))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
            }
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
            try
            {
                WriteJson("workspaces.json", Workspaces());
                editor.Visibility = Visibility.Collapsed;
                message.Text = "";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                workspaceShortcuts = null;
                message.Text = "Windows Dock could not save the workspace.";
            }
            Render();
        }
    }

    private static bool WorkspaceTarget(string target) =>
        Path.IsPathFullyQualified(target) && (File.Exists(target) || Directory.Exists(target)) ||
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0;

    private bool restoringWorkspace;

    private async Task<int> LaunchWorkspaceAsync(WorkspaceShortcut shortcut)
    {
        if (restoringWorkspace)
            return 1;
        restoringWorkspace = true;
        var failures = 0;
        var windows = shortcut.Windows ?? [];
        try
        {
            foreach (var target in new[] { shortcut.Folder }.Concat(shortcut.Targets).Where(target => target.Length > 0))
            {
                if (windows.Any(window => window.Path.Equals(target, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!WorkspaceTarget(target))
                {
                    failures++;
                    continue;
                }
                try { ShellIntegration.Launch(target); }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException) { failures++; }
            }
            return failures + await DockWorkspaces.RestoreAsync(windows, lifetime.Token);
        }
        catch (OperationCanceledException) { return failures; }
        finally { restoringWorkspace = false; }
    }

    private sealed record WorkspaceShortcut(string Name, string Folder, string[] Targets, WorkspaceWindow[]? Windows = null);
}
