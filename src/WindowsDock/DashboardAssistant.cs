using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private const int AssistantMessageLimit = 8000;
    private sealed record AssistantSource(string Label, AssistantProvider Provider, string Model, string? Variable);
    private static readonly AssistantSource[] AssistantSources =
    [
        new("ChatGPT plan", AssistantProvider.ChatGptPlan, "", null),
        new("Claude API", AssistantProvider.ClaudeApi, "claude-sonnet-4-6", "ANTHROPIC_API_KEY"),
        new("Gemini", AssistantProvider.Gemini, TextModelClient.GeminiSummaryModel, "GEMINI_API_KEY"),
        new("Grok · xAI", AssistantProvider.Grok, "grok-4.7", "XAI_API_KEY"),
        new("Groq", AssistantProvider.Groq, TextModelClient.GroqSummaryModel, "GROQ_API_KEY"),
        new("Codex CLI", AssistantProvider.CodexCli, "", null),
        new("Claude Code CLI", AssistantProvider.ClaudeCode, "", null),
        new("Ollama", AssistantProvider.Ollama, "", null)
    ];
    private sealed class AssistantConversation
    {
        internal Guid Id { get; init; } = Guid.NewGuid();
        internal List<SavedTurn> Turns { get; } = [];
        internal List<PickedChatFile> Files { get; } = [];
        internal string Draft { get; set; } = "";
        internal int SourceIndex { get; set; }
        internal AssistantContext? Context { get; set; }
        internal DockAction[] Actions { get; set; } = [];
        internal PendingHandoff? Handoff { get; set; }
    }
    private sealed record PendingHandoff(DockHandoff Brief,
        Func<string, IReadOnlyList<(bool User, string Text)>, CancellationToken, Task<string>> Send);
    private readonly AssistantConversation[] assistantConversations = AssistantPersonas.All.Select(_ => new AssistantConversation()).ToArray();
    private readonly System.Security.SecureString?[] assistantKeys = new System.Security.SecureString?[AssistantSources.Length];
    private readonly string?[] assistantModels = new string?[AssistantSources.Length];
    private readonly Dictionary<string, DateTimeOffset> assistantPlanCooldowns = new();
    private readonly PlanSignIn planSignIn = new();
    private int assistantRole = 3;

    private bool HasActiveWork => runningJobs.Count != 0 || planSignIn.Active != null;

    private Task<PlanAccount> StartPlanSignIn(PlanAccount? account)
    {
        if (planSignIn.Active is { } active)
            return active;
        var attempt = planSignIn.Start(account, lifetime.Token, ChatGptPlan.ConnectAsync);
        _ = ResumeIdleAfterAsync(attempt);
        return attempt;

        async Task ResumeIdleAfterAsync(Task<PlanAccount> signIn)
        {
            try { await signIn; }
            catch (Exception error) when (error is PlanException or OperationCanceledException or HttpRequestException or
                IOException or JsonException or FormatException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception or System.Net.Sockets.SocketException) { }
            finally
            {
                if (releaseWhenIdle && !closed && !IsVisible && !HasActiveWork)
                    idleExit.Start();
            }
        }
    }

    private void OpenAssistantConversation(int slot)
    {
        assistantRole = slot;
        ShowAssistantWidget();
    }

    internal void ShowAssistantWidget()
    {
        var slot = assistantRole;
        var persona = AssistantPersonas.All[slot];
        var chat = assistantConversations[slot];
        var tint = AssistantInk;
        var figure = PersonaPortrait(persona, 38, lively: true);
        var layout = new Grid();
        var railColumn = new ColumnDefinition { Width = new GridLength(212) };
        layout.ColumnDefinitions.Add(railColumn);
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var (window, _, cancellation) = CreateDockPanel(persona.Name, persona.Role, 890, 0xEAB7, tint,
            backAction: ShowAssistantPicker, backLabel: "Back to assistants", portrait: figure, body: layout, height: 610);
        var rail = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var railLine = new Border
        {
            Child = rail, BorderBrush = AssistantRule, BorderThickness = new Thickness(0, 0, 1, 0),
            Margin = new Thickness(0, 0, 14, 0)
        };
        layout.Children.Add(railLine);
        var chatPane = new DockPanel();
        Grid.SetColumn(chatPane, 1);
        layout.Children.Add(chatPane);
        var content = new StackPanel();
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = Text("New chat", 15, AssistantInk, true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var historyToggle = IconButton(0xE8FD, "Toggle chat history", () =>
        {
            var shown = railLine.Visibility == Visibility.Visible;
            railLine.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            railColumn.Width = shown ? new GridLength(0) :
                new GridLength(window.Width < 650 ? 145 : 212);
        });
        historyToggle.Margin = new Thickness(0, 0, 6, 0);
        toolbar.Children.Add(historyToggle);
        title.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(title);
        DockPanel.SetDock(toolbar, Dock.Top);
        chatPane.Children.Add(toolbar);
        if (window.Width < 650)
        {
            railLine.Visibility = Visibility.Collapsed;
            railColumn.Width = new GridLength(0);
        }
        var intro = Text(persona.Greeting, 15, AssistantInk, strong: true);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Margin = new Thickness(0, 2, 0, 8);
        var toolRow = new WrapPanel();
        var setup = new StackPanel();
        var modelLabel = Text("Provider and model", 12, AssistantInk);
        modelLabel.MaxWidth = 225;
        modelLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        var chevron = DockGlyph(0xE70D, 10, AssistantSecondary);
        chevron.Margin = new Thickness(6, 0, 0, 0);
        var modelButtonContent = new StackPanel { Orientation = Orientation.Horizontal, Children = { modelLabel, chevron } };
        var setupToggle = MakeButton(modelButtonContent, "Provider and model", action: null);
        setupToggle.Margin = new Thickness(0, 0, 6, 0);
        var modelMenu = new Popup
        {
            PlacementTarget = setupToggle, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true,
            CustomPopupPlacementCallback = (popup, target, _) =>
            [
                new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 6), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 6), PopupPrimaryAxis.Horizontal)
            ],
            Child = new Border
            {
                Child = new ScrollViewer { Content = setup, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = Math.Max(140, window.Height - 110) },
                Background = assistantShell, BorderBrush = AssistantRule, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12),
                Width = Math.Min(380, window.Width - 32)
            }
        };
        setupToggle.Click += (_, _) => modelMenu.IsOpen = !modelMenu.IsOpen;
        var availableSources = AssistantConnections.For(persona.Id);
        if (availableSources.Length > 0)
        {
            var connections = QuietButton("Connections", () => ShowAssistantConnections(persona));
            AutomationProperties.SetName(connections, "MCP connections");
            toolRow.Children.Add(connections);
        }
        var jobs = QuietButton("Jobs", ShowAssistantJobs);
        toolRow.Children.Add(jobs);
        if (persona.Id is "coding" or "casual")
        {
            var quick = QuietButton(AssistantQuickLabel(persona), () => OpenAssistantQuickAction(slot));
            toolRow.Children.Add(quick);
        }
        content.Children.Add(toolRow);
        var starters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var providers = new ComboBox { ItemsSource = AssistantSources, DisplayMemberPath = nameof(AssistantSource.Label) };
        AutomationProperties.SetName(providers, "Assistant provider");
        var model = Field("Assistant model ID", 128);
        var planModels = new ComboBox { DisplayMemberPath = nameof(PlanModel.Name) };
        AutomationProperties.SetName(planModels, "ChatGPT model");
        var key = new PasswordBox { MaxLength = 512 };
        AutomationProperties.SetName(key, "Assistant API key");
        var settings = Columns(.85, 1.4);
        Put(settings, Labeled("Provider", providers), 0);
        var manualModel = Labeled("Model ID", model);
        var planModel = Labeled("Model", planModels);
        var localModels = new ComboBox();
        AutomationProperties.SetName(localModels, "Ollama model");
        var localModel = Labeled("Installed model", localModels);
        var refreshLocalModels = ActionButton("Refresh models");
        refreshLocalModels.HorizontalAlignment = HorizontalAlignment.Left;
        refreshLocalModels.Margin = new Thickness(0, 5, 0, 0);
        localModel.Children.Add(refreshLocalModels);
        var modelGroup = new StackPanel();
        modelGroup.Children.Add(manualModel);
        modelGroup.Children.Add(planModel);
        modelGroup.Children.Add(localModel);
        Put(settings, modelGroup, 1);
        content.Children.Add(settings);
        var accountUI = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        var accounts = new ComboBox { DisplayMemberPath = nameof(PlanAccount.Label) };
        AutomationProperties.SetName(accounts, "ChatGPT account");
        var accountField = Labeled("ChatGPT account", accounts);
        accountUI.Children.Add(accountField);
        var accountActions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var connect = ActionButton("Continue with ChatGPT");
        var addAccount = ActionButton("Add account");
        var disconnect = ActionButton("Disconnect");
        var manage = ActionButton("Manage usage", () => OpenTarget("https://chatgpt.com/settings/usage"));
        foreach (var action in new[] { connect, addAccount, disconnect, manage })
        {
            action.Margin = new Thickness(0, 0, 4, 4);
            accountActions.Children.Add(action);
        }
        accountUI.Children.Add(accountActions);
        content.Children.Add(accountUI);
        var credentials = Labeled("API key · saved on this Windows account", key);
        credentials.Margin = new Thickness(0, 7, 0, 0);
        content.Children.Add(credentials);

        var messages = new StackPanel();
        var thread = new StackPanel { Children = { intro, starters, messages } };
        var conversation = new ScrollViewer { Content = thread, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.VerticalOnly };
        var prompt = Field("Message", AssistantMessageLimit);
        prompt.AcceptsReturn = true;
        prompt.TextWrapping = TextWrapping.Wrap;
        prompt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        prompt.Height = 68;
        prompt.Tag = "Message " + persona.Name;
        prompt.Margin = new Thickness(0);
        prompt.Background = Brushes.Transparent;
        prompt.BorderThickness = new Thickness(0);
        content.Children.Add(prompt);
        var attachment = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        content.Children.Add(attachment);
        var preview = new ContentControl { Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var proposal = new StackPanel();
        content.Children.Add(proposal);
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 6);
        content.Children.Add(status);
        var fallback = new WrapPanel { Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
        content.Children.Add(fallback);

        var actions = new DockPanel { LastChildFill = false, Margin = new Thickness(2, 0, 2, 2) };
        var send = PrimaryButton("Send");
        AutomationProperties.SetName(send, "Send message");
        send.ToolTip = "Send message (Ctrl+Enter)";
        send.MinWidth = 68;
        send.Margin = new Thickness(0, 0, 6, 0);
        var clear = ActionButton("New chat");
        clear.Margin = new Thickness(0, 0, 0, 8);
        var findChat = Field("Search conversations", 100);
        findChat.Tag = "Search chats";
        findChat.Margin = new Thickness(0, 0, 0, 8);
        var history = new StackPanel();
        var folder = ActionButton("Chat storage folder");
        var openFolder = IconButton(0xE8B7, "Open chat storage folder");
        var autoSave = new CheckBox { Content = "Auto-save named files", IsChecked = DockChats.AutoSaveFiles,
            Margin = new Thickness(0, 4, 0, 4) };
        var storage = new StackPanel { Children = { folder, autoSave, openFolder }, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(storage, Dock.Bottom);
        rail.Children.Add(storage);
        var historyHeading = new StackPanel { Children = { clear, findChat } };
        DockPanel.SetDock(historyHeading, Dock.Top);
        rail.Children.Add(historyHeading);
        rail.Children.Add(new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var attachFiles = IconButton(0xE723, "Attach files or images");
        attachFiles.Margin = new Thickness(4, 0, 0, 0);
        var cancel = ActionButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 5, 0);
        cancel.Visibility = Visibility.Collapsed;
        CancellationTokenSource? pending = null;
        var watchingSignIn = false;
        var localModelLoad = 0;
        var localModelStatus = "";
        var updating = false;
        var busy = false;
        var changingFiles = false;
        var shownTurns = 40;
        SavedChat[] savedChats = [];
        var catalog = new Dictionary<string, PlanModel[]>();
        var cooldownTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        cooldownTimer.Tick += (_, _) => SetBusy(busy);
        var preferred = ChatGptPlan.Load().FirstOrDefault()?.ClientId;
        clear.Click += (_, _) =>
        {
            SwitchChat(new AssistantConversation { SourceIndex = chat.SourceIndex });
        };
        actions.Children.Add(attachFiles);
        foreach (var control in new UIElement[] { send, cancel, setupToggle })
        {
            DockPanel.SetDock(control, Dock.Right);
            actions.Children.Add(control);
        }
        actions.Children.Add(modelMenu);
        content.Children.Add(actions);
        foreach (var starter in persona.Starters)
        {
            var button = ActionButton(starter.Label, () =>
            {
                if (string.IsNullOrWhiteSpace(prompt.Text))
                    prompt.Text = starter.Draft;
                prompt.Focus();
                prompt.CaretIndex = prompt.Text.Length;
            });
            button.Padding = new Thickness(10, 5, 10, 5);
            button.Margin = new Thickness(0, 0, 6, 6);
            starters.Children.Add(button);
        }
        providers.SelectionChanged += (_, _) =>
        {
            if (!updating && providers.SelectedIndex >= 0)
            {
                chat.SourceIndex = providers.SelectedIndex;
                SetProvider();
                WatchActiveSignIn();
            }
        };
        accounts.SelectionChanged += (_, _) =>
        {
            if (!updating && !busy)
            {
                preferred = (accounts.SelectedItem as PlanAccount)?.ClientId;
                SetDisclosure();
                _ = LoadModelsAsync();
            }
        };
        connect.Click += async (_, _) => await WatchSignInAsync(StartPlanSignIn(accounts.SelectedItem as PlanAccount));
        addAccount.Click += async (_, _) => await WatchSignInAsync(StartPlanSignIn(null));
        refreshLocalModels.Click += async (_, _) => await LoadLocalModelsAsync();
        localModels.SelectionChanged += (_, _) => { UpdateModelLabel(); SetBusy(busy); };
        planModels.SelectionChanged += (_, _) => { UpdateModelLabel(); SetBusy(busy); };
        model.TextChanged += (_, _) => UpdateModelLabel();
        setup.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            args.Handled = true;
            if (OpenComboBox(args.OriginalSource) is { } combo)
                combo.IsDropDownOpen = false;
            else
                modelMenu.IsOpen = false;
        };
        disconnect.Click += async (_, _) => await DisconnectAsync();
        cancel.Click += (_, _) =>
        {
            if (watchingSignIn)
                planSignIn.Cancel();
            else
                pending?.Cancel();
        };
        send.Click += async (_, _) => await SendAsync();
        attachFiles.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Attach files or images", Multiselect = true,
                Filter = "Documents and images|*.txt;*.md;*.csv;*.json;*.pdf;*.docx;*.png;*.jpg;*.jpeg|All files|*.*"
            };
            if (dialog.ShowDialog(window) == true)
                await AddFilesAsync(dialog.FileNames);
        };
        layout.AllowDrop = true;
        layout.PreviewDragOver += (_, args) =>
        {
            args.Effects = !busy && !changingFiles && args.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        };
        layout.PreviewDrop += async (_, args) =>
        {
            if (!busy && !changingFiles && args.Data.GetData(DataFormats.FileDrop) is string[] paths)
                await AddFilesAsync(paths);
            args.Handled = true;
        };
        findChat.TextChanged += (_, _) => RenderHistory();
        folder.Click += async (_, _) => await ChooseFolderAsync();
        autoSave.Click += (_, _) =>
        {
            try { DockChats.SetAutoSaveFiles(autoSave.IsChecked == true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                autoSave.IsChecked = DockChats.AutoSaveFiles;
                status.Text = "Could not save the automatic file setting.";
            }
        };
        openFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(DockChats.Root);
                OpenTarget(DockChats.Root);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Could not open the chat storage folder."; }
        };
        prompt.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && send.IsEnabled)
            {
                args.Handled = true;
                await SendAsync();
            }
        };
        window.Loaded += async (_, _) => { prompt.Focus(); await ReloadHistoryAsync(); };
        window.Closed += (_, _) =>
        {
            modelMenu.IsOpen = false;
            chat.Draft = prompt.Text;
            key.Clear();
            cooldownTimer.Stop();
        };
        prompt.Text = chat.Draft;
        updating = true;
        providers.SelectedIndex = chat.SourceIndex;
        updating = false;
        RenderConversation();
        RenderAttachment();
        RenderProposal();
        SetProvider();
        SetBusy(false);
        content.Children.Clear();
        foreach (var part in new UIElement[] { settings, accountUI, credentials })
            setup.Children.Add(part);
        foreach (Button link in toolRow.Children)
            link.Margin = new Thickness(0, 0, 18, 0);
        var composerBox = new Border
        {
            Child = new StackPanel { Children = { prompt, actions } },
            Background = ControlFill, BorderBrush = AssistantRule, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(5), Margin = new Thickness(0, 7, 0, 0)
        };
        foreach (var part in new UIElement[] { proposal, preview, attachment, composerBox, status, fallback, toolRow })
            content.Children.Add(part);
        var composer = new ScrollViewer
        {
            Content = content, MaxHeight = Math.Max(150, window.Height * .55),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        DockPanel.SetDock(composer, Dock.Bottom);
        chatPane.Children.Add(composer);
        chatPane.Children.Add(conversation);
        window.Show();
        cooldownTimer.Start();
        WatchActiveSignIn();

        async Task ReloadHistoryAsync()
        {
            try
            {
                var loaded = await Task.Run(DockChats.Load, cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
                savedChats = loaded;
                folder.ToolTip = DockChats.Root;
                RenderHistory();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Could not read the chat library. Choose an accessible storage folder."; }
        }

        void RenderHistory()
        {
            history.Children.Clear();
            var query = findChat.Text.Trim();
            foreach (var entry in savedChats.Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Persona.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var targetSlot = Array.FindIndex(AssistantPersonas.All, item => item.Id == entry.Persona);
                if (targetSlot < 0)
                    continue;
                var name = Text(entry.Title, 12, AssistantInk);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                var details = Text($"{AssistantPersonas.All[targetSlot].Name} · {entry.Updated.LocalDateTime:d}", 11, AssistantSecondary);
                var row = new StackPanel { Children = { name, details } };
                var open = MakeButton(row, entry.Title, () => ResumeChat(entry, targetSlot));
                open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                open.Margin = new Thickness(0, 0, 0, 4);
                if (entry.Id == chat.Id)
                    open.Background = controlSelected;
                history.Children.Add(open);
            }
            if (history.Children.Count == 0)
                history.Children.Add(Text(query.Length == 0 ? "Conversations appear here after you send a message." :
                    "No matching conversations.", 12, AssistantSecondary));
        }

        void ResumeChat(SavedChat entry, int targetSlot)
        {
            if (entry.Id == chat.Id)
                return;
            var source = Array.FindIndex(AssistantSources, item => item.Provider == entry.Provider);
            var resumed = new AssistantConversation { Id = entry.Id, SourceIndex = Math.Max(0, source) };
            resumed.Turns.AddRange(entry.Turns);
            if (source >= 0 && entry.Model != null)
                assistantModels[source] = entry.Model;
            if (targetSlot == slot)
                SwitchChat(resumed);
            else
            {
                assistantConversations[targetSlot] = resumed;
                OpenAssistantConversation(targetSlot);
            }
        }

        void SwitchChat(AssistantConversation next)
        {
            chat.Draft = prompt.Text;
            chat = assistantConversations[slot] = next;
            shownTurns = 40;
            prompt.Text = chat.Draft;
            preview.Visibility = Visibility.Collapsed;
            preview.Content = null;
            updating = true;
            providers.SelectedIndex = chat.SourceIndex;
            updating = false;
            SetProvider();
            RenderConversation();
            RenderAttachment();
            RenderProposal();
            RenderHistory();
            prompt.Focus();
        }

        async Task ChooseFolderAsync()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choose where Windows Dock stores chats and files",
                InitialDirectory = Directory.Exists(DockChats.Root) ? DockChats.Root : ""
            };
            if (dialog.ShowDialog(window) != true)
                return;
            changingFiles = true;
            SetBusy(busy);
            status.Text = "Copying the chat library…";
            try
            {
                await Task.Run(() => DockChats.ChooseFolder(dialog.FolderName));
                await ReloadHistoryAsync();
                if (!cancellation.IsCancellationRequested)
                    status.Text = "Chat storage folder saved.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { status.Text = error.Message; }
            finally
            {
                changingFiles = false;
                if (!cancellation.IsCancellationRequested)
                    SetBusy(busy);
            }
        }

        async Task AddFilesAsync(string[] paths)
        {
            var remaining = ChatAttachments.FileLimit - chat.Files.Count;
            if (remaining <= 0)
            {
                status.Text = "Attach up to six files per message.";
                return;
            }
            changingFiles = true;
            SetBusy(busy);
            var current = chat;
            var picked = new List<PickedChatFile>();
            var errors = new List<string>();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var path in paths.Take(remaining))
                    {
                        try { picked.Add(ChatAttachments.Read(path)); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                            System.Xml.XmlException or ArgumentException)
                        { errors.Add($"{Path.GetFileName(path)}: {error.Message}"); }
                    }
                });
                if (cancellation.IsCancellationRequested)
                    return;
                if (paths.Length > remaining)
                    errors.Add("Attach up to six files per message.");
                var mediaBytes = current.Files.Where(item => item.File.MediaType != null).Sum(item => item.Bytes.Length);
                foreach (var item in picked)
                {
                    if (item.File.MediaType != null && mediaBytes + item.Bytes.Length > ChatAttachments.MediaLimit)
                    {
                        errors.Add($"{item.File.Name}: images and PDFs can total up to 10 MB.");
                        continue;
                    }
                    current.Files.Add(item);
                    if (item.File.MediaType != null)
                        mediaBytes += item.Bytes.Length;
                }
                RenderAttachment();
                status.Text = errors.Count > 0 ? string.Join("\n", errors.Take(2)) :
                    "Files attached. They will be sent with your next message.";
            }
            finally
            {
                changingFiles = false;
                if (!cancellation.IsCancellationRequested)
                    SetBusy(busy);
            }
        }

        void ReviewFile(ChatFile file, byte[]? bytes = null)
        {
            var details = new StackPanel();
            var heading = new DockPanel();
            var dismiss = IconButton(0xE711, "Close file preview", () =>
            {
                preview.Visibility = Visibility.Collapsed;
                preview.Content = null;
            });
            DockPanel.SetDock(dismiss, Dock.Right);
            heading.Children.Add(dismiss);
            var name = Text(file.Name, 12, AssistantInk, true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            heading.Children.Add(name);
            details.Children.Add(heading);
            try
            {
                if (file.MediaType is "image/png" or "image/jpeg")
                {
                    using var data = new MemoryStream(bytes ?? DockChats.ReadMedia(chat.Id, file).Bytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400;
                    bitmap.StreamSource = data;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    details.Children.Add(new Image { Source = bitmap, MaxHeight = 120, Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left });
                }
                else
                {
                    var text = Field("Attachment preview", 0);
                    text.IsReadOnly = true;
                    text.TextWrapping = TextWrapping.Wrap;
                    text.MaxHeight = 110;
                    text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    text.Text = file.Text ?? "PDF document";
                    details.Children.Add(text);
                }
                preview.Content = Card(details);
                preview.Visibility = Visibility.Visible;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException)
            { status.Text = "Could not preview this attachment."; }
        }

        async Task SaveTranscriptAsync(AssistantProvider provider, string modelId, IReadOnlyList<PickedChatFile>? files = null)
        {
            var turns = chat.Turns.ToArray();
            var snapshot = new SavedChat(chat.Id, persona.Id, DockChats.Title(turns), DateTimeOffset.UtcNow, turns, provider, modelId);
            await Task.Run(() => DockChats.Save(snapshot, files));
            if (!cancellation.IsCancellationRequested)
            {
                savedChats = savedChats.Where(item => item.Id != snapshot.Id).Prepend(snapshot).ToArray();
                if (chat.Id == snapshot.Id)
                    title.Text = snapshot.Title;
                RenderHistory();
            }
        }

        void SaveReply(string name, string text)
        {
            try
            {
                var output = DockChats.SaveFile(chat.Id, name, text);
                status.Text = $"Saved {Path.GetFileName(output)} in this chat’s files folder.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { status.Text = error.Message; }
        }

        StackPanel Labeled(string label, Control control)
        {
            var group = new StackPanel();
            group.Children.Add(Text(label, 11, AssistantSecondary));
            control.Margin = new Thickness(0, 3, 0, 0);
            group.Children.Add(control);
            return group;
        }

        void SetProvider()
        {
            var index = chat.SourceIndex;
            var source = AssistantSources[index];
            var plan = source.Provider == AssistantProvider.ChatGptPlan;
            var cli = source.Provider is AssistantProvider.CodexCli or AssistantProvider.ClaudeCode;
            var local = source.Provider == AssistantProvider.Ollama;
            localModelLoad++;
            model.Text = assistantModels[index] ?? source.Model;
            model.Tag = cli ? "CLI default" : "";
            key.Clear();
            accountUI.Visibility = plan ? Visibility.Visible : Visibility.Collapsed;
            planModel.Visibility = plan ? Visibility.Visible : Visibility.Collapsed;
            localModel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
            manualModel.Visibility = plan || local ? Visibility.Collapsed : Visibility.Visible;
            credentials.Visibility = source.Variable != null ? Visibility.Visible : Visibility.Collapsed;
            fallback.Visibility = Visibility.Collapsed;
            if (plan)
            {
                RefreshAccounts();
                _ = LoadModelsAsync();
            }
            UpdateModelLabel();
            SetDisclosure();
            SetBusy(busy);
            if (local)
                _ = LoadLocalModelsAsync();
        }

        void UpdateModelLabel()
        {
            var source = AssistantSources[chat.SourceIndex];
            var name = source.Provider switch
            {
                AssistantProvider.ChatGptPlan => (planModels.SelectedItem as PlanModel)?.Name,
                AssistantProvider.Ollama => localModels.SelectedItem as string,
                _ => model.Text.Trim()
            };
            modelLabel.Text = string.IsNullOrWhiteSpace(name) ? source.Label : $"{source.Label} · {name}";
            AutomationProperties.SetHelpText(setupToggle, modelLabel.Text);
        }

        void SetDisclosure()
        {
            var source = AssistantSources[chat.SourceIndex];
            status.Text = source.Provider == AssistantProvider.ChatGptPlan
                ? (accounts.SelectedItem as PlanAccount)?.Refresh.Length > 0
                    ? "Using ChatGPT plan. Send shares the current role’s conversation with ChatGPT."
                    : "Connect an eligible ChatGPT account to use its plan allowance."
                : source.Provider == AssistantProvider.Ollama ? localModelStatus
                : source.Provider == AssistantProvider.ClaudeApi
                    ? "Send shares this conversation with Claude API. Claude Console bills API usage separately."
                    : source.Variable == null
                        ? $"Uses your local {source.Label} sign-in."
                        : $"Send shares this conversation with {source.Label} using your API key.";
        }

        void SetBusy(bool busy)
        {
            // The character wobbles while it works and hops when the answer lands.
            var wasThinking = figure.Thinking;
            figure.Thinking = busy;
            if (wasThinking && !busy)
                figure.Hop();
            var provider = AssistantSources[chat.SourceIndex].Provider;
            var isPlan = provider == AssistantProvider.ChatGptPlan;
            var account = accounts.SelectedItem as PlanAccount;
            send.IsEnabled = !busy && !changingFiles && (!isPlan || planModels.SelectedItem != null && account != null &&
                assistantPlanCooldowns.GetValueOrDefault(account.ClientId) <= DateTimeOffset.UtcNow) &&
                (provider != AssistantProvider.Ollama || localModels.SelectedItem is string);
            providers.IsEnabled = model.IsEnabled = key.IsEnabled = accounts.IsEnabled = planModels.IsEnabled = !busy;
            localModels.IsEnabled = refreshLocalModels.IsEnabled = !busy;
            addAccount.IsEnabled = connect.IsEnabled = !busy;
            disconnect.IsEnabled = !busy && (accounts.SelectedItem as PlanAccount)?.Refresh.Length > 0;
            foreach (Button button in starters.Children)
                button.IsEnabled = !busy;
            toolRow.IsEnabled = attachment.IsEnabled = proposal.IsEnabled = !busy;
            rail.IsEnabled = attachFiles.IsEnabled = !busy && !changingFiles;
            setupToggle.IsEnabled = !busy && !changingFiles;
            if (busy)
                modelMenu.IsOpen = false;
            prompt.IsReadOnly = busy || changingFiles;
            cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        void RefreshAccounts()
        {
            updating = true;
            var list = ChatGptPlan.Load();
            accounts.ItemsSource = list;
            accounts.SelectedItem = list.FirstOrDefault(item => item.ClientId == preferred) ?? list.FirstOrDefault();
            accountField.Visibility = addAccount.Visibility = disconnect.Visibility = list.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            updating = false;
        }

        async Task LoadLocalModelsAsync()
        {
            var index = chat.SourceIndex;
            var generation = ++localModelLoad;
            var previousModel = localModels.SelectedItem as string ?? assistantModels[index];
            localModels.ItemsSource = null;
            localModelStatus = "Finding models on this PC…";
            SetDisclosure();
            try
            {
                var models = await OllamaTextModel.Local.ModelsAsync(cancellation);
                if (cancellation.IsCancellationRequested || generation != localModelLoad)
                    return;
                localModels.ItemsSource = models;
                localModels.SelectedItem = models.FirstOrDefault(name => name == previousModel) ?? models.FirstOrDefault();
                localModelStatus = models.Length > 0 ? "Uses Ollama on this PC."
                    : "Add a local model in Ollama, then refresh the list.";
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or IOException or OperationCanceledException)
            {
                if (cancellation.IsCancellationRequested || generation != localModelLoad)
                    return;
                localModelStatus = "Start Ollama on this PC and refresh the model list.";
            }
            SetDisclosure();
            SetBusy(busy);
        }

        async Task LoadModelsAsync()
        {
            var account = accounts.SelectedItem as PlanAccount;
            planModels.ItemsSource = null;
            if (account?.Refresh.Length > 0)
            {
                try
                {
                    if (!catalog.TryGetValue(account.ClientId, out var modelsForAccount))
                        catalog[account.ClientId] = modelsForAccount = await ChatGptPlan.ModelsAsync(account, cancellation);
                    if (cancellation.IsCancellationRequested || !Equals(accounts.SelectedItem, account))
                        return;
                    planModels.ItemsSource = modelsForAccount;
                    planModels.SelectedItem = modelsForAccount.FirstOrDefault(item => item.Id == assistantModels[0])
                        ?? modelsForAccount.FirstOrDefault();
                    if (modelsForAccount.Length == 0)
                        status.Text = "No ChatGPT models are available for this account.";
                }
                catch (PlanException error) { if (!cancellation.IsCancellationRequested) status.Text = error.Message; }
                catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
                { if (!cancellation.IsCancellationRequested) status.Text = "Could not load the models for this account."; }
            }
            if (!cancellation.IsCancellationRequested)
                SetBusy(busy);
        }

        void WatchActiveSignIn()
        {
            if (!busy && AssistantSources[chat.SourceIndex].Provider == AssistantProvider.ChatGptPlan &&
                planSignIn.Active is { } attempt)
                _ = WatchSignInAsync(attempt);
        }

        async Task WatchSignInAsync(Task<PlanAccount> attempt)
        {
            watchingSignIn = busy = true;
            SetBusy(true);
            var firstConnection = ChatGptPlan.Load().Length == 0;
            status.Text = "Complete sign-in and approve plan usage in your browser.";
            try
            {
                var connected = await attempt.WaitAsync(cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
                preferred = connected.ClientId;
                assistantPlanCooldowns.Remove(connected.ClientId);
                catalog.Remove(connected.ClientId);
                RefreshAccounts();
                SetDisclosure();
                await LoadModelsAsync();
                if (firstConnection && planModels.SelectedItem != null)
                    status.Text = "You’re using your ChatGPT plan. Eligible messages use your allowance or credits, as configured under Manage usage.";
            }
            catch (PlanException error) { status.Text = error.Message; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (OperationCanceledException) { status.Text = "Sign-in canceled."; }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or FormatException or
                UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception
                or System.Net.Sockets.SocketException)
            { status.Text = "Could not connect your ChatGPT account."; }
            finally
            {
                watchingSignIn = false;
                FinishRequest();
            }
        }

        async Task DisconnectAsync()
        {
            if (accounts.SelectedItem is not PlanAccount account)
                return;
            using var request = BeginRequest();
            try
            {
                var revoked = await ChatGptPlan.DisconnectAsync(account, request.Token);
                RefreshAccounts();
                planModels.ItemsSource = null;
                catalog.Remove(account.ClientId);
                status.Text = revoked ? "ChatGPT account disconnected." :
                    "Signed out locally. Check ChatGPT settings to confirm remote access was removed.";
            }
            catch (OperationCanceledException) { status.Text = "Disconnection canceled."; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            { status.Text = "Could not update the saved account."; }
            finally { FinishRequest(); }
        }

        void RenderConversation(bool toEnd = true)
        {
            var history = chat.Turns;
            title.Text = DockChats.Title(history);
            intro.Visibility = starters.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            messages.Children.Clear();
            var first = Math.Max(0, history.Count - shownTurns);
            if (first > 0)
            {
                var older = ActionButton("Show earlier messages", () =>
                {
                    shownTurns += 40;
                    RenderConversation(toEnd: false);
                });
                older.HorizontalAlignment = HorizontalAlignment.Left;
                messages.Children.Add(older);
            }
            for (var index = first; index < history.Count; index++)
            {
                var turn = history[index];
                var (user, text) = (turn.User, turn.Text);
                var number = index + 1;
                var line = new StackPanel();
                var name = user ? "You" : persona.Name;
                var heading = new DockPanel();
                if (!user)
                {
                    var save = IconButton(0xE74E, "Save reply as Markdown", () => SaveReply($"reply-{number}.md", text));
                    DockPanel.SetDock(save, Dock.Right);
                    heading.Children.Add(save);
                    var copy = IconButton(0xE8C8, "Copy reply", () =>
                    {
                        try
                        {
                            Clipboard.SetText(text);
                            status.Text = "Reply copied.";
                        }
                        catch (ExternalException) { status.Text = "Clipboard is busy. Try again."; }
                    });
                    copy.Padding = new Thickness(4, 2, 4, 2);
                    DockPanel.SetDock(copy, Dock.Right);
                    heading.Children.Add(copy);
                }
                var author = Text(name, 11, AssistantSecondary);
                author.VerticalAlignment = VerticalAlignment.Center;
                heading.Children.Add(author);
                line.Children.Add(heading);
                if (user)
                {
                    var message = Field(name + " message", 0);
                    message.Text = text;
                    message.IsReadOnly = true;
                    message.TextWrapping = TextWrapping.Wrap;
                    message.Background = Brushes.Transparent;
                    message.BorderBrush = Brushes.Transparent;
                    message.Padding = new Thickness(0, 3, 0, 0);
                    line.Children.Add(message);
                }
                else
                {
                    line.Children.Add(FormatChatReply(text, number, SaveReply));
                    if (turn.OutputFiles is { Length: > 0 } outputs)
                    {
                        var saved = Text("Saved files · " + string.Join(", ", outputs), 11, AssistantSecondary);
                        saved.TextWrapping = TextWrapping.Wrap;
                        line.Children.Add(saved);
                    }
                }
                if (turn.Files is { Length: > 0 } files)
                {
                    var attached = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                    foreach (var file in files)
                    {
                        var chip = ActionButton(file.Name, () => ReviewFile(file));
                        chip.Margin = new Thickness(0, 0, 4, 4);
                        attached.Children.Add(chip);
                    }
                    line.Children.Add(attached);
                }
                var card = Card(line);
                card.Background = user ? ControlFill : System.Windows.Media.Brushes.Transparent;
                card.BorderBrush = System.Windows.Media.Brushes.Transparent;
                card.Padding = user ? new Thickness(11, 7, 11, 8) : new Thickness(0, 2, 0, 4);
                card.Margin = new Thickness(user ? 56 : 0, 0, 0, 8);
                messages.Children.Add(card);
            }
            if (toEnd)
                conversation.ScrollToEnd();
            else
                conversation.ScrollToTop();
        }

        void RenderAttachment()
        {
            attachment.Children.Clear();
            foreach (var item in chat.Files)
            {
                var chip = ActionButton(item.File.Name, () => ReviewFile(item.File, item.Bytes));
                var remove = IconButton(0xE711, "Remove " + item.File.Name, () =>
                {
                    chat.Files.Remove(item);
                    preview.Visibility = Visibility.Collapsed;
                    preview.Content = null;
                    RenderAttachment();
                });
                var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
                group.Children.Add(chip);
                group.Children.Add(remove);
                attachment.Children.Add(group);
            }
            if (chat.Context is not { } context)
                return;
            var review = ActionButton("Attached: " + context.Source, () => ReviewFile(new(Guid.Empty, context.Source, null, context.Text)));
            attachment.Children.Add(review);
            var removeContext = IconButton(0xE711, "Remove attached context", () =>
            {
                chat.Context = null;
                preview.Visibility = Visibility.Collapsed;
                preview.Content = null;
                RenderAttachment();
            });
            attachment.Children.Add(removeContext);
        }

        async Task SendAsync()
        {
            if (busy || changingFiles)
                return;
            var index = chat.SourceIndex;
            var source = AssistantSources[index];
            var isPlan = source.Provider == AssistantProvider.ChatGptPlan;
            var isCli = source.Provider is AssistantProvider.CodexCli or AssistantProvider.ClaudeCode;
            var isLocal = source.Provider == AssistantProvider.Ollama;
            var message = prompt.Text.Trim();
            var modelId = isPlan ? (planModels.SelectedItem as PlanModel)?.Id ?? "" :
                isLocal ? localModels.SelectedItem as string ?? "" : model.Text.Trim();
            var account = accounts.SelectedItem as PlanAccount;
            var typed = key.Password.Trim();
            var apiKey = source.Variable == null ? null : typed.Length == 0 ? AssistantKey(index) : typed;
            if (message.Length == 0 || !isCli && modelId.Length == 0 || isPlan && account == null ||
                source.Variable != null && string.IsNullOrWhiteSpace(apiKey))
            {
                if (message.Length > 0)
                    modelMenu.IsOpen = true;
                status.Text = isPlan ? "Choose a connected account and model, then enter a message."
                    : isLocal ? "Choose an installed Ollama model and enter a message."
                    : isCli ? "Enter a message." : "Enter a message, model ID, and API key.";
                return;
            }
            if ((apiKey?.Any(char.IsWhiteSpace) ?? false) || modelId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '/' or ':')))
            {
                status.Text = "Check the model ID and API key.";
                return;
            }
            var picked = chat.Files.ToArray();
            if (picked.Any(item => item.File.MediaType != null) && !ChatMediaRequest.Supports(source.Provider))
            {
                status.Text = "Choose ChatGPT plan, Claude API, or Gemini to send images and PDFs.";
                return;
            }
            var outgoing = new SavedTurn(true, message, picked.Select(item => item.File).ToArray(), chat.Context);
            var recent = chat.Turns.TakeLast(8).Append(outgoing).ToArray();
            var requestTurns = recent.Select(turn => (turn.User, AttachmentMessage(turn))).ToArray();
            var media = new Dictionary<int, ChatMedia[]>();
            assistantModels[index] = modelId;
            fallback.Visibility = Visibility.Collapsed;
            using var request = BeginRequest();
            try
            {
                if (ChatMediaRequest.Supports(source.Provider))
                {
                    var latest = picked.Where(item => item.File.MediaType != null).Select(item =>
                        new ChatMedia(item.File.Name, item.File.MediaType!, item.Bytes)).ToArray();
                    if (latest.Length > 0)
                        media[recent.Length - 1] = latest;
                    else
                    {
                        for (var turnIndex = recent.Length - 2; turnIndex >= 0; turnIndex--)
                        {
                            var documents = recent[turnIndex].Files?.Where(file => file.MediaType != null).ToArray();
                            if (documents is not { Length: > 0 })
                                continue;
                            media[turnIndex] = await Task.Run(() => documents.Select(file => DockChats.ReadMedia(chat.Id, file)).ToArray(),
                                request.Token);
                            break;
                        }
                    }
                }
                chat.Turns.Add(outgoing);
                try { await SaveTranscriptAsync(source.Provider, modelId, picked); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    chat.Turns.Remove(outgoing);
                    status.Text = "Could not save the message. Check the chat storage folder and try again.";
                    return;
                }
                chat.Files.Clear();
                chat.Context = null;
                prompt.Clear();
                preview.Visibility = Visibility.Collapsed;
                preview.Content = null;
                RenderConversation();
                RenderAttachment();
                status.Text = $"Waiting for {source.Label}…";
                var response = await DockAssistantTurn.RunAsync(persona.Id, persona.Instructions, requestTurns,
                    (instructions, messages, token) => SendToProvider(instructions, messages, token, media),
                    progress => status.Text = progress, request.Token, allowHandoff: persona.Id == "casual");
                if (cancellation.IsCancellationRequested)
                    return;
                if (string.IsNullOrWhiteSpace(response.Text))
                {
                    status.Text = "The provider returned an empty reply.";
                    return;
                }
                if (typed.Length > 0)
                {
                    assistantKeys[index]?.Dispose();
                    assistantKeys[index] = key.SecurePassword;
                    try { DockCredentials.Save(source.Provider, typed); }
                    catch (Exception error) when (DockActions.IsActionError(error) || error is System.Security.Cryptography.CryptographicException) { }
                    key.Clear();
                }
                var reply = response.Text[..Math.Min(response.Text.Length, 40_000)];
                var outputs = new List<string>();
                string? fileIssue = null;
                if (autoSave.IsChecked == true)
                {
                    var generated = ChatMarkdown.Blocks(reply).Where(block => block.Kind == MdKind.Code)
                        .Select(block => (Name: ChatMarkdown.NamedFile(block.Label), block.Text))
                        .Where(item => item.Name != null).ToArray();
                    if (generated.Length > 0)
                    {
                        try
                        {
                            var id = chat.Id;
                            await Task.Run(() =>
                            {
                                foreach (var item in generated)
                                    outputs.Add(Path.GetFileName(DockChats.SaveFile(id, item.Name!, item.Text)));
                            });
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { fileIssue = "Some generated files could not be saved. Use the Save buttons to retry."; }
                    }
                }
                chat.Turns.Add(new(false, reply, OutputFiles: outputs.ToArray()));
                chat.Actions = response.Actions;
                chat.Handoff = response.Handoff is { } handoff
                    ? new(handoff, (instructions, messages, token) => SendToProvider(instructions, messages, token)) : null;
                RenderConversation();
                RenderProposal();
                SetDisclosure();
                if (fileIssue != null)
                    status.Text = fileIssue;
                try { await SaveTranscriptAsync(source.Provider, modelId); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { status.Text = "The reply is visible but could not be saved. Check the chat storage folder."; }
            }
            catch (PlanException error)
            {
                status.Text = error.Message;
                if (error.CanFallback)
                {
                    if (account != null)
                        assistantPlanCooldowns[account.ClientId] = DateTimeOffset.UtcNow.AddMinutes(1);
                    OfferFallback();
                }
            }
            catch (HttpRequestException error)
            {
                status.Text = isLocal ? "Could not get a reply from Ollama. Check that it is running and refresh the model list."
                    : error.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Check that your API key can access this model.",
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound => "Check the model ID and provider settings.",
                    HttpStatusCode.TooManyRequests => "The provider is rate limiting requests. Try again later.",
                    _ => "Could not reach the provider. Try again."
                };
                if (!isLocal && error.StatusCode is (HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired))
                    OfferFallback();
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                status.Text = request.IsCancellationRequested ? "Request canceled." : "The provider took too long to respond.";
            }
            catch (OperationCanceledException) { }
            catch (ArgumentException error)
            {
                status.Text = error.Message;
            }
            catch (CodingSummaryException error)
            {
                status.Text = error.Message;
            }
            catch (IOException error) when (!cancellation.IsCancellationRequested)
            {
                status.Text = error.Message;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or IOException or
                UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                status.Text = "The provider did not return a readable reply.";
            }
            finally
            {
                FinishRequest();
            }

            Task<string> SendToProvider(string instructions, IReadOnlyList<(bool User, string Text)> messages, CancellationToken token,
                IReadOnlyDictionary<int, ChatMedia[]>? attachments = null) =>
                isPlan ? ChatGptPlan.SendAsync(account!, modelId, instructions, messages.ToArray(), token, attachments) :
                    isLocal ? OllamaTextModel.Local.ChatAsync(modelId, instructions, messages, token) :
                    isCli ? CodingSummaryClient.ChatAsync(source.Provider, modelId, instructions, messages, token) :
                    TextModelClient.SendChat(source.Provider, modelId, apiKey!, messages.ToArray(), token, instructions, attachments);
        }

        static string AttachmentMessage(SavedTurn turn)
        {
            if (turn.Files is not { Length: > 0 } && turn.Context == null)
                return turn.Text;
            var remaining = ChatAttachments.TextLimit;
            var files = new List<object>();
            foreach (var file in turn.Files ?? [])
            {
                var excerpt = file.Text ?? "";
                var take = Math.Min(excerpt.Length, remaining);
                files.Add(new { file.Name, file.MediaType, Text = excerpt[..take] });
                remaining -= take;
            }
            return JsonSerializer.Serialize(new { message = turn.Text, attachedFiles = files, toolContext = turn.Context });
        }

        void RenderProposal()
        {
            proposal.Children.Clear();
            if (chat.Handoff is { } handoff)
            {
                RenderHandoff(handoff);
                return;
            }
            if (chat.Actions.Length == 0)
                return;
            var checklist = new StackPanel();
            checklist.Children.Add(Text("Proposed dock changes", 12, AssistantInk, true));
            foreach (var action in chat.Actions)
            {
                var line = Text(action.Preview, 12, AssistantSecondary);
                line.TextWrapping = TextWrapping.Wrap;
                line.Margin = new Thickness(0, 3, 0, 0);
                checklist.Children.Add(line);
            }
            var choices = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            var apply = AccentButton("Apply changes", 0xE73E, AssistantInk);
            apply.Margin = new Thickness(0, 0, 5, 0);
            apply.Click += async (_, _) => await ApplyProposalAsync();
            choices.Children.Add(apply);
            choices.Children.Add(ActionButton("Discard", () =>
            {
                chat.Actions = [];
                chat.Turns.Add(new(false, "The proposed dock changes were discarded."));
                RenderProposal();
                RenderConversation();
            }));
            checklist.Children.Add(choices);
            proposal.Children.Add(Card(checklist));
        }

        void RenderHandoff(PendingHandoff pendingHandoff)
        {
            var handoff = pendingHandoff.Brief;
            var body = new StackPanel();
            body.Children.Add(Text($"Send to {handoff.Name}", 12, AssistantInk, true));
            var brief = Text(handoff.Task, 12, AssistantSecondary);
            brief.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(new ScrollViewer { Content = brief, MaxHeight = 94, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var choices = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            var start = AccentButton(handoff.Role == "coding" ? "Choose project and start" : "Start in background", 0xE768, AssistantInk, () =>
            {
                var workspace = handoff.Role == "coding" ? PickCodingWorkspace(window) : null;
                if (handoff.Role == "coding" && workspace == null)
                    return;
                try
                {
                    QueueAssistantJob(handoff, workspace, pendingHandoff.Send);
                    chat.Handoff = null;
                    chat.Turns.Add(new(false, $"{handoff.Name} accepted the job. Track it under Jobs."));
                    RenderProposal();
                    RenderConversation();
                    status.Text = "The job can continue after you close this panel.";
                }
                catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
            });
            start.Margin = new Thickness(0, 0, 5, 0);
            choices.Children.Add(start);
            choices.Children.Add(ActionButton("Discard", () =>
            {
                chat.Handoff = null;
                RenderProposal();
            }));
            body.Children.Add(choices);
            proposal.Children.Add(Card(body));
        }

        async Task ApplyProposalAsync()
        {
            var approved = chat.Actions;
            chat.Actions = [];
            RenderProposal();
            using var request = BeginRequest();
            status.Text = "Applying dock changes…";
            try
            {
                chat.Turns.Add(new(false, await RunDockChangesAsync(approved, request.Token)));
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    RefreshFocusState();
                    ShowPage(currentPage);
                    RenderConversation();
                    SetDisclosure();
                }
                FinishRequest();
            }
        }

        CancellationTokenSource BeginRequest()
        {
            pending = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            busy = true;
            SetBusy(true);
            return pending;
        }

        void FinishRequest()
        {
            pending = null;
            busy = false;
            if (!cancellation.IsCancellationRequested)
            {
                SetBusy(false);
                prompt.Focus();
            }
        }

        void OfferFallback()
        {
            fallback.Children.Clear();
            for (var index = 0; index < AssistantSources.Length; index++)
            {
                var alternate = index;
                if (alternate == chat.SourceIndex ||
                    AssistantSources[alternate].Provider is not (AssistantProvider.Gemini or AssistantProvider.Grok))
                    continue;
                var button = ActionButton("Switch to " + AssistantSources[alternate].Label, () =>
                {
                    providers.SelectedIndex = alternate;
                    prompt.Focus();
                });
                button.Margin = new Thickness(0, 0, 5, 2);
                fallback.Children.Add(button);
            }
            fallback.Visibility = Visibility.Visible;
        }
    }

    private string? AssistantKey(int index)
    {
        if (DockCredentials.Read(AssistantSources[index].Provider) is { } saved)
            return saved;
        if (assistantKeys[index] is { } key)
        {
            var buffer = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(key);
            try { return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(buffer).Trim(); }
            finally { System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(buffer); }
        }
        var source = AssistantSources[index];
        var summaryIndex = Array.FindIndex(SummarySources, item => item.Provider == source.Provider);
        return (summaryIndex >= 0 ? ReadSessionKey(summaryIndex) : null) ??
            (source.Variable is { } name ? EnvironmentKey(name) : null);
    }
}
