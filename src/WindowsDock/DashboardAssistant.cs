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
        internal SavedChat? Saved { get; set; }
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

    private bool HasActiveWork => runningJobs.Count != 0 || planSignIn.Active != null || activeCodingRequests != 0;

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

    private bool TryOpenOrbitWithDraft(string addition)
    {
        var slot = Array.FindIndex(AssistantPersonas.All, persona => persona.Id == "casual");
        var chat = assistantConversations[slot];
        var draft = string.IsNullOrWhiteSpace(chat.Draft) ? addition : chat.Draft + "\n\n" + addition;
        if (draft.Length > AssistantMessageLimit)
            return false;
        chat.Draft = draft;
        OpenAssistantConversation(slot);
        return true;
    }

    // The chat lives on a page of the dashboard rather than in a window of its own. Every entry point that used to
    // open the chat window lands here.
    internal void ShowAssistantWidget()
    {
        OpenMainDashboard();
        ShowPage(DashboardPage.Assistant);
        if (!IsVisible)
            Show();
        Activate();
    }

    // The page for one assistant is built once and kept, so a reply keeps streaming while you look at another page
    // and the conversation is exactly where you left it when you come back.
    private (int Slot, FrameworkElement Page, CancellationTokenSource Lifetime)? assistantPage;
    private const string LearningFile = "assistant-learning.json";

    private FrameworkElement AssistantPageHost()
    {
        if (assistantPage is { } kept && kept.Slot == assistantRole)
            return kept.Page;
        DiscardAssistantPage();
        var pageLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var page = BuildAssistantPage(pageLifetime.Token);
        assistantPage = (assistantRole, page, pageLifetime);
        return page;
    }

    private void DiscardAssistantPage()
    {
        if (assistantPage is not { } kept)
            return;
        assistantPage = null;
        kept.Lifetime.Cancel();
        kept.Lifetime.Dispose();
    }

    private FrameworkElement BuildAssistantPage(CancellationToken cancellation)
    {
        var slot = assistantRole;
        var persona = AssistantPersonas.All[slot];
        var chat = assistantConversations[slot];
        var tint = AssistantInk;
        Window window = this;
        var layout = new Grid();
        var railColumn = new ColumnDefinition { Width = new GridLength(196) };
        layout.ColumnDefinitions.Add(railColumn);
        layout.ColumnDefinitions.Add(new ColumnDefinition());
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
        var toolbar = new DockPanel { Margin = new Thickness(0, -6, 0, 6) };
        var title = Text("New chat", 15, AssistantInk, true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var historyToggle = IconButton(0xE8FD, "Toggle chat history", () =>
        {
            var shown = railLine.Visibility == Visibility.Visible;
            railLine.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
            railColumn.Width = shown ? new GridLength(0) :
                new GridLength(window.Width < 650 ? 145 : 196);
        });
        historyToggle.Margin = new Thickness(0, 0, 6, 0);
        toolbar.Children.Add(historyToggle);
        var chatFiles = IconButton(0xE8B7, "Open this chat’s files", () =>
        {
            try { OpenTarget(DockChats.FilesFolder(chat.Id)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { ShowOwnedMessage("Could not open this conversation’s files folder."); }
        });
        DockPanel.SetDock(chatFiles, Dock.Right);
        toolbar.Children.Add(chatFiles);
        var organize = IconButton(0xE70F, "Organize this chat");
        DockPanel.SetDock(organize, Dock.Right);
        toolbar.Children.Add(organize);
        title.VerticalAlignment = VerticalAlignment.Center;
        // Who you are talking to, then what about, the way the chat sites title a conversation.
        var badge = PersonaPortrait(persona, 24, lively: false);
        badge.Margin = new Thickness(2, 0, 7, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(badge);
        var figure = badge;
        var who = Text(persona.Name, 15, AssistantInk, true);
        who.VerticalAlignment = VerticalAlignment.Center;
        who.Margin = new Thickness(0, 0, 10, 0);
        who.ToolTip = persona.Bio;
        toolbar.Children.Add(who);
        title.FontSize = 13;
        title.FontWeight = FontWeights.Normal;
        title.Foreground = AssistantSecondary;
        toolbar.Children.Add(title);
        DockPanel.SetDock(toolbar, Dock.Top);
        chatPane.Children.Add(toolbar);
        var chatTitle = Field("Chat title", 100);
        var chatFolder = Field("Chat group", 48);
        var pinChat = new CheckBox { Content = "Pin conversation", Margin = new Thickness(0, 8, 0, 8) };
        var saveDetails = ActionButton("Save chat details");
        var chatDetails = new StackPanel
        {
            Children = { Labeled("Title", chatTitle), Labeled("Group", chatFolder), pinChat, saveDetails }
        };
        var detailsCard = Card(chatDetails);
        detailsCard.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(detailsCard, Dock.Top);
        chatPane.Children.Add(detailsCard);
        if (window.Width < 650)
        {
            railLine.Visibility = Visibility.Collapsed;
            railColumn.Width = new GridLength(0);
        }
        // An empty chat opens on a centred welcome, like a new chat on the chat sites: the character, its question,
        // and a couple of ways to start.
        var intro = Text(persona.Greeting, 20, AssistantInk, strong: true);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.TextAlignment = TextAlignment.Center;
        intro.HorizontalAlignment = HorizontalAlignment.Center;
        intro.Margin = new Thickness(0, 8, 0, 14);
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
        // Each assistant's own quick action (web search, schedule, logs, idea jar) sits under its message box.
        if (persona.Id is "coding" or "casual" or "research" or "housekeeping")
        {
            var quick = QuietButton(AssistantQuickLabel(persona), () => OpenAssistantQuickAction(slot));
            toolRow.Children.Add(quick);
        }
        content.Children.Add(toolRow);
        var starters = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
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
        // While a reply streams in, the character sits beside it and wobbles; the text grows underneath.
        var liveText = Text("", 13.5, AssistantInk);
        liveText.TextWrapping = TextWrapping.Wrap;
        liveText.LineHeight = 20;
        var liveFigure = PersonaPortrait(persona, 22, lively: true);
        liveFigure.Thinking = true;
        liveFigure.HorizontalAlignment = HorizontalAlignment.Left;
        liveFigure.Margin = new Thickness(-2, 0, 0, 4);
        var live = new StackPanel { Margin = new Thickness(0, 6, 0, 12), Visibility = Visibility.Collapsed,
            Children = { liveFigure, liveText } };
        var welcomeFigure = PersonaPortrait(persona, 64, lively: true);
        var welcomeBio = Text(persona.Bio, 12.5, AssistantSecondary);
        welcomeBio.TextWrapping = TextWrapping.Wrap;
        welcomeBio.TextAlignment = TextAlignment.Center;
        welcomeBio.HorizontalAlignment = HorizontalAlignment.Center;
        welcomeBio.Margin = new Thickness(0, -8, 0, 16);
        var welcome = new StackPanel { Margin = new Thickness(24, 26, 24, 8), Children = { welcomeFigure, intro, welcomeBio, starters } };
        var thread = new StackPanel { Margin = new Thickness(4, 0, 14, 4), Children = { welcome, messages, live } };
        var conversation = new ScrollViewer { Content = thread, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.VerticalOnly };
        var prompt = Field("Message", AssistantMessageLimit);
        prompt.AcceptsReturn = true;
        prompt.TextWrapping = TextWrapping.Wrap;
        prompt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // The box starts one line tall and grows with what you type, like the chat sites.
        prompt.MinHeight = 26;
        prompt.MaxHeight = 118;
        prompt.FontSize = 13.5;
        prompt.Tag = "Message " + persona.Name;
        prompt.Margin = new Thickness(0);
        prompt.Padding = new Thickness(4, 4, 4, 2);
        prompt.Background = Brushes.Transparent;
        prompt.BorderThickness = new Thickness(0);
        // The rounded box around it is the field; the text box itself draws no border, focused or not.
        prompt.BorderBrush = Brushes.Transparent;
        prompt.Resources["InputFocus"] = Brushes.Transparent;
        prompt.Resources["FieldStroke"] = Brushes.Transparent;
        content.Children.Add(prompt);
        var attachment = new WrapPanel { Margin = new Thickness(2, 0, 0, 2) };
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

        var actions = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
        var send = RoundIconButton(0xE74A, "Send message");
        send.ToolTip = "Send (Enter). Shift+Enter starts a new line.";
        send.Margin = new Thickness(6, 0, 0, 0);
        var clear = MakeButton(IconLabel("New chat", 0xE70F, AssistantSecondary), "New chat", action: null);
        clear.Style = (Style)FindResource("AssistantPickerButton");
        clear.Foreground = AssistantInk;
        clear.HorizontalContentAlignment = HorizontalAlignment.Left;
        clear.Padding = new Thickness(8, 7, 8, 7);
        clear.Margin = new Thickness(-2, 0, 0, 6);
        var findChat = Field("Search conversations", 100);
        findChat.Tag = "Search chats";
        findChat.Margin = new Thickness(0, 0, 0, 10);
        var history = new StackPanel();
        var folder = ActionButton("Change folder");
        var openFolder = ActionButton("Open folder");
        var autoSave = new CheckBox { Content = "Save named code blocks", IsChecked = DockChats.AutoSaveFiles,
            Margin = new Thickness(0, 6, 0, 2) };
        var memoryButton = QuietButton("Memory");
        var memoryInput = Field("What this assistant should remember", AssistantLearning.MemoryLimit);
        memoryInput.AcceptsReturn = true;
        memoryInput.TextWrapping = TextWrapping.Wrap;
        memoryInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        memoryInput.Height = 96;
        var memoryUsage = Text($"What {persona.Name} keeps about you. It stays on this PC, encrypted, and goes with each message " +
            "to the provider you chat with. Edit or clear it any time.", 11, AssistantSecondary);
        memoryUsage.TextWrapping = TextWrapping.Wrap;
        memoryUsage.Margin = new Thickness(0, 0, 0, 6);
        var learning = new CheckBox { Content = "Learn from my chats", IsChecked = DockData.Read<bool?>(LearningFile) ?? true,
            Margin = new Thickness(0, 6, 0, 6) };
        learning.ToolTip = "Lets assistants add lasting preferences you mention, such as how you like answers. Lines they add start with a dash.";
        var saveMemory = ActionButton("Save memory");
        var memory = new StackPanel
        {
            Children = { memoryUsage, memoryInput, learning, saveMemory },
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 4)
        };
        var filesButton = QuietButton("Chat files");
        var filesPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        var memoryFile = $"assistant-memory-{persona.Id}.dat";
        var savedMemory = DockCredentials.ReadPrivateText(memoryFile) ?? "";
        var fallbackFile = $"assistant-fallback-{persona.Id}.json";
        var fallbackOptions = new ComboBox { ItemsSource = new[] { "Off", "Gemini", "Grok" } };
        fallbackOptions.SelectedIndex = Math.Clamp(DockData.Read<int>(fallbackFile), 0, 2);
        AutomationProperties.SetName(fallbackOptions, "Usage-limit fallback");
        var fallbackNotice = Text("On a usage limit, the selected fallback receives this request and can use your API credits.",
            11, AssistantSecondary);
        fallbackNotice.TextWrapping = TextWrapping.Wrap;
        // The bottom of the history rail holds the two things you set once: memory and where chats are kept.
        var folderRow = new WrapPanel { Children = { folder, openFolder } };
        folder.Margin = new Thickness(0, 0, 6, 0);
        filesPanel.Children.Add(folderRow);
        filesPanel.Children.Add(autoSave);
        var railLinks = new StackPanel { Orientation = Orientation.Horizontal, Children = { memoryButton, filesButton } };
        memoryButton.Margin = new Thickness(0, 0, 16, 0);
        var storage = new StackPanel { Children = { memory, filesPanel, railLinks }, Margin = new Thickness(0, 8, 0, 0) };
        storage.Children.Insert(0, Rule(0, 6));
        DockPanel.SetDock(storage, Dock.Bottom);
        rail.Children.Add(storage);
        var historyHeading = new StackPanel { Children = { clear, findChat } };
        DockPanel.SetDock(historyHeading, Dock.Top);
        rail.Children.Add(historyHeading);
        rail.Children.Add(new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var attachFiles = IconButton(0xE710, "Attach files or images");
        attachFiles.ToolTip = "Attach files or images. You can also drop them here or paste a screenshot.";
        attachFiles.Margin = new Thickness(-4, 0, 0, 0);
        var retryReply = ActionButton("Retry");
        retryReply.Padding = new Thickness(10, 4, 10, 4);
        retryReply.Margin = new Thickness(6, 0, 0, 0);
        retryReply.Visibility = Visibility.Collapsed;
        // Stop replaces Send while a reply is coming, as on the chat sites.
        var cancel = RoundIconButton(0xE71A, "Stop");
        cancel.Margin = new Thickness(6, 0, 0, 0);
        cancel.Visibility = Visibility.Collapsed;
        CancellationTokenSource? pending = null;
        var watchingSignIn = false;
        var localModelLoad = 0;
        var localModelStatus = "";
        var updating = false;
        var busy = false;
        var sendReady = false;
        var changingFiles = false;
        var shownTurns = 40;
        SavedChat[] savedChats = [];
        var showHiddenChats = false;
        Guid? renamingChat = null, deletingChat = null;
        var catalog = new Dictionary<string, PlanModel[]>();
        var cooldownTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        cooldownTimer.Tick += (_, _) => SetBusy(busy);
        var preferred = ChatGptPlan.Load().FirstOrDefault()?.ClientId;
        clear.Click += (_, _) =>
        {
            SwitchChat(new AssistantConversation { SourceIndex = chat.SourceIndex });
        };
        actions.Children.Add(attachFiles);
        actions.Children.Add(retryReply);
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
        retryReply.Click += async (_, _) => await SendAsync(retry: true);
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
        organize.Click += (_, _) =>
        {
            if (detailsCard.Visibility == Visibility.Visible)
            {
                detailsCard.Visibility = Visibility.Collapsed;
                return;
            }
            chatTitle.Text = chat.Saved?.Title ?? DockChats.Title(chat.Turns);
            chatFolder.Text = chat.Saved?.Folder ?? "";
            pinChat.IsChecked = chat.Saved?.Pinned == true;
            detailsCard.Visibility = Visibility.Visible;
            chatTitle.Focus();
            chatTitle.SelectAll();
        };
        saveDetails.Click += async (_, _) => await SaveDetailsAsync();
        fallbackOptions.SelectionChanged += (_, _) =>
        {
            try { DockData.Save(fallbackFile, fallbackOptions.SelectedIndex); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Could not save fallback preference."; }
        };
        memoryButton.Click += (_, _) =>
        {
            filesPanel.Visibility = Visibility.Collapsed;
            if (memory.Visibility == Visibility.Visible)
                memory.Visibility = Visibility.Collapsed;
            else
            {
                memoryInput.Text = savedMemory;
                memory.Visibility = Visibility.Visible;
                memoryInput.Focus();
            }
        };
        filesButton.Click += (_, _) =>
        {
            memory.Visibility = Visibility.Collapsed;
            filesPanel.Visibility = filesPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        };
        learning.Click += (_, _) =>
        {
            try { DockData.Save(LearningFile, learning.IsChecked == true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                learning.IsChecked = !learning.IsChecked;
                status.Text = "Could not save the learning setting.";
            }
        };
        saveMemory.Click += (_, _) =>
        {
            try
            {
                var value = memoryInput.Text.Trim();
                if (value.Length == 0)
                    File.Delete(DockData.FilePath(memoryFile));
                else
                    DockCredentials.WritePrivateText(memoryFile, value);
                savedMemory = value;
                memory.Visibility = Visibility.Collapsed;
                status.Text = value.Length > 0 ? $"{persona.Name}’s memory saved." : $"{persona.Name}’s memory cleared.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            { status.Text = "Could not save assistant memory."; }
        };
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
        prompt.TextChanged += (_, _) => chat.Draft = prompt.Text;
        prompt.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !busy && !changingFiles && PasteScreenshot())
                args.Handled = true;
            // Enter sends, as on the chat sites. Shift+Enter starts a new line.
            else if (args.Key == Key.Enter && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control)
            {
                args.Handled = true;
                if (send.IsEnabled && !string.IsNullOrWhiteSpace(prompt.Text))
                    await SendAsync();
                else if (!busy && !sendReady && !string.IsNullOrWhiteSpace(prompt.Text))
                    // Nothing to send with yet: show where to pick a provider rather than doing nothing.
                    modelMenu.IsOpen = true;
            }
        };
        prompt.TextChanged += (_, _) => send.IsEnabled = sendReady && prompt.Text.Trim().Length > 0;
        var firstShow = true;
        layout.Loaded += async (_, _) =>
        {
            cooldownTimer.Start();
            // The page is kept between visits, so pick up anything another part of the dock did to this chat while it
            // was away: a draft from search or the idea jar, attached logs or lookups, or a conversation reopened.
            if (!firstShow && !busy && !changingFiles)
            {
                if (!ReferenceEquals(chat, assistantConversations[slot]))
                    SwitchChat(assistantConversations[slot]);
                else
                {
                    if (prompt.Text != chat.Draft)
                    {
                        prompt.Text = chat.Draft;
                        prompt.CaretIndex = prompt.Text.Length;
                    }
                    RenderAttachment();
                    RenderProposal();
                    SetProvider();
                }
            }
            firstShow = false;
            prompt.Focus();
            await ReloadHistoryAsync();
        };
        // Leaving for another page keeps the chat (and any reply still arriving); only the menu and the timer stop.
        layout.Unloaded += (_, _) =>
        {
            modelMenu.IsOpen = false;
            cooldownTimer.Stop();
        };
        cancellation.Register(() =>
        {
            modelMenu.IsOpen = false;
            key.Clear();
            cooldownTimer.Stop();
        });
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
        foreach (var part in new UIElement[] { settings, accountUI, credentials,
            Labeled("Usage-limit fallback", fallbackOptions), fallbackNotice })
            setup.Children.Add(part);
        foreach (Button link in toolRow.Children)
            link.Margin = new Thickness(0, 0, 18, 0);
        // One rounded field: what you are attaching, what you are typing, then attach on the left and the model and
        // the round send button on the right.
        var composerBox = new Border
        {
            Child = new StackPanel { Children = { attachment, prompt, actions } },
            Background = ControlFill, BorderBrush = AssistantRule, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 4, 0, 0)
        };
        composerBox.MouseLeftButtonDown += (_, _) => prompt.Focus();
        prompt.GotKeyboardFocus += (_, _) => composerBox.BorderBrush = AssistantFaint;
        prompt.LostKeyboardFocus += (_, _) => composerBox.BorderBrush = AssistantRule;
        status.Margin = new Thickness(14, 5, 0, 0);
        var footer = new DockPanel { Margin = new Thickness(12, 4, 0, 0) };
        DockPanel.SetDock(toolRow, Dock.Right);
        footer.Children.Add(toolRow);
        footer.Children.Add(status);
        foreach (var part in new UIElement[] { proposal, preview, composerBox, fallback, footer })
            content.Children.Add(part);
        var composer = new ScrollViewer
        {
            Content = content, MaxHeight = Math.Max(150, window.Height * .55),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        DockPanel.SetDock(composer, Dock.Bottom);
        chatPane.Children.Add(composer);
        chatPane.Children.Add(conversation);
        WatchActiveSignIn();
        return FillPage(layout);

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
            var hiddenCount = savedChats.Count(item => item.Hidden);
            if (hiddenCount == 0)
                showHiddenChats = false;
            var matches = savedChats.Where(item => (showHiddenChats || !item.Hidden) &&
                (item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Persona.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Folder?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
                .OrderByDescending(item => item.Pinned).ThenByDescending(item => item.Updated);
            // Pinned first, then your groups, then everything else by when it was last used, the way the chat sites
            // list conversations: one quiet line each, the open one filled in. Hidden chats sit in their own group at
            // the end, and only while you ask to see them.
            var today = DateTime.Today;
            foreach (var group in matches.GroupBy(item => item.Hidden ? (3, "Hidden") : item.Pinned ? (0, "Pinned") :
                item.Folder is { Length: > 0 } folderName ? (1, folderName) : (2, ChatAge(item.Updated.LocalDateTime.Date)))
                .OrderBy(group => group.Key.Item1 == 3 ? 1 : 0))
            {
                var label = Text(group.Key.Item2, 11, AssistantFaint);
                label.Margin = new Thickness(8, history.Children.Count == 0 ? 0 : 10, 0, 3);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                history.Children.Add(label);
                foreach (var entry in group)
                {
                    var targetSlot = Array.FindIndex(AssistantPersonas.All, item => item.Id == entry.Persona);
                    if (targetSlot < 0)
                        continue;
                    var current = entry.Id == chat.Id;
                    if (renamingChat == entry.Id)
                    {
                        history.Children.Add(RenameRow(entry));
                        continue;
                    }
                    if (deletingChat == entry.Id)
                    {
                        history.Children.Add(DeleteRow(entry));
                        continue;
                    }
                    var name = Text(entry.Title, 12.5, current ? AssistantInk : AssistantSecondary);
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    name.VerticalAlignment = VerticalAlignment.Center;
                    var row = new DockPanel();
                    // Chats with another assistant carry its face, so you can tell whose they are at a glance.
                    if (targetSlot != slot)
                    {
                        var face = PersonaPortrait(AssistantPersonas.All[targetSlot], 16);
                        face.Margin = new Thickness(0, 0, 6, 0);
                        row.Children.Add(face);
                    }
                    row.Children.Add(name);
                    var open = PlainButton(row, $"{entry.Title}, with {AssistantPersonas.All[targetSlot].Name}", () => ResumeChat(entry, targetSlot));
                    open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    open.Margin = new Thickness(0, 0, 0, 1);
                    open.Padding = new Thickness(8, 6, 8, 6);
                    open.ToolTip = $"{entry.Title}\n{AssistantPersonas.All[targetSlot].Name}, {entry.Updated.LocalDateTime:g}";
                    if (current)
                        open.Background = controlSelected;
                    history.Children.Add(ChatRow(entry, open));
                }
            }

            string ChatAge(DateTime day) =>
                day >= today ? "Today" : day == today.AddDays(-1) ? "Yesterday" :
                day > today.AddDays(-7) ? "Previous 7 days" : day > today.AddDays(-30) ? "Previous 30 days" : day.ToString("MMMM yyyy");
            if (history.Children.Count == 0)
                history.Children.Add(Text(query.Length == 0 ? "Your chats will show up here." :
                    "No matching conversations.", 12, AssistantSecondary));
            if (hiddenCount > 0)
            {
                var toggle = PlainButton(Text(showHiddenChats ? "Put hidden chats away" : $"Show hidden chats ({hiddenCount})", 11.5, AssistantFaint),
                    showHiddenChats ? "Put hidden chats away" : "Show hidden chats", () =>
                    {
                        showHiddenChats = !showHiddenChats;
                        RenderHistory();
                    });
                toggle.Margin = new Thickness(0, 10, 0, 0);
                toggle.HorizontalAlignment = HorizontalAlignment.Left;
                history.Children.Add(toggle);
            }
        }

        // One chat in the list, with rename, hide and delete tucked on the right. They show while the pointer or the
        // keyboard is on the row, the way the chat sites do it, so the list stays one quiet line per chat.
        UIElement ChatRow(SavedChat entry, Button open)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 1) };
            row.Children.Add(open);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), Opacity = 0 };
            tools.Children.Add(ChatTool(0xE8AC, $"Rename “{entry.Title}”", () =>
            {
                renamingChat = entry.Id;
                deletingChat = null;
                RenderHistory();
            }));
            tools.Children.Add(ChatTool(entry.Hidden ? 0xE7B3 : 0xED1A, entry.Hidden ? $"Unhide “{entry.Title}”" : $"Hide “{entry.Title}”",
                () => _ = HideChatAsync(entry)));
            tools.Children.Add(ChatTool(0xE74D, $"Delete “{entry.Title}”", () =>
            {
                deletingChat = entry.Id;
                renamingChat = null;
                RenderHistory();
            }));
            row.Children.Add(tools);
            void Reveal() => tools.Opacity = row.IsMouseOver || row.IsKeyboardFocusWithin ? 1 : 0;
            row.MouseEnter += (_, _) => Reveal();
            row.MouseLeave += (_, _) => Reveal();
            row.IsKeyboardFocusWithinChanged += (_, _) => Reveal();
            // Leave room for the tools so a long title never runs underneath them.
            open.Padding = new Thickness(8, 6, 8, 6);
            row.MouseEnter += (_, _) => open.Padding = new Thickness(8, 6, 76, 6);
            row.MouseLeave += (_, _) => open.Padding = new Thickness(8, 6, row.IsKeyboardFocusWithin ? 76 : 8, 6);
            return row;
        }

        Button ChatTool(int icon, string label, Action action)
        {
            var button = IconButton(icon, label, action);
            button.Width = 23;
            button.Height = 23;
            button.Margin = new Thickness(0);
            button.Foreground = AssistantSecondary;
            return button;
        }

        UIElement RenameRow(SavedChat entry)
        {
            var box = Field("Chat name", 100);
            box.Text = entry.Title;
            box.Margin = new Thickness(0, 0, 0, 1);
            var done = false;
            box.KeyDown += (_, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    args.Handled = true;
                    done = true;
                    _ = RenameChatAsync(entry, box.Text);
                }
                else if (args.Key == System.Windows.Input.Key.Escape)
                {
                    args.Handled = true;
                    done = true;
                    renamingChat = null;
                    RenderHistory();
                }
            };
            // Clicking away keeps what was typed, like renaming a file.
            box.LostKeyboardFocus += (_, _) =>
            {
                if (done)
                    return;
                done = true;
                _ = RenameChatAsync(entry, box.Text);
            };
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            return box;
        }

        UIElement DeleteRow(SavedChat entry)
        {
            var card = new Border
            {
                Background = controlSelected, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7, 7, 7),
                Margin = new Thickness(0, 0, 0, 1)
            };
            var stack = new StackPanel();
            var question = Text($"Delete “{entry.Title}”?", 12, AssistantInk);
            question.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(question);
            var note = Text("It goes to the Recycle Bin with its files.", 11, AssistantFaint);
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var delete = ActionButton("Delete", () => _ = DeleteChatAsync(entry));
            delete.Foreground = bad;
            delete.Margin = new Thickness(0, 0, 6, 0);
            buttons.Children.Add(delete);
            buttons.Children.Add(QuietButton("Keep", () =>
            {
                deletingChat = null;
                RenderHistory();
            }));
            stack.Children.Add(buttons);
            card.Child = stack;
            card.Loaded += (_, _) => delete.Focus();
            card.KeyDown += (_, args) =>
            {
                if (args.Key != System.Windows.Input.Key.Escape)
                    return;
                args.Handled = true;
                deletingChat = null;
                RenderHistory();
            };
            return card;
        }

        bool ChatInUse(SavedChat entry)
        {
            if (entry.Id != chat.Id || !(busy || changingFiles))
                return false;
            status.Text = "Wait for the reply to finish before changing this chat.";
            return true;
        }

        async Task RenameChatAsync(SavedChat entry, string name)
        {
            renamingChat = null;
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == entry.Title || ChatInUse(entry))
            {
                RenderHistory();
                return;
            }
            try
            {
                UpdateSavedChat(await Task.Run(() => DockChats.Organize(entry.Id, name, entry.Folder ?? "", entry.Pinned)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "Could not rename that chat.";
                RenderHistory();
            }
        }

        async Task HideChatAsync(SavedChat entry)
        {
            if (ChatInUse(entry))
                return;
            try
            {
                var saved = await Task.Run(() => DockChats.SetHidden(entry.Id, !entry.Hidden));
                UpdateSavedChat(saved);
                status.Text = saved.Hidden ? $"Hid “{saved.Title}”. Show hidden chats brings it back." : $"“{saved.Title}” is back in the list.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Could not hide that chat."; }
        }

        async Task DeleteChatAsync(SavedChat entry)
        {
            deletingChat = null;
            if (ChatInUse(entry))
            {
                RenderHistory();
                return;
            }
            try
            {
                await Task.Run(() => DockChats.Delete(entry.Id));
                savedChats = savedChats.Where(item => item.Id != entry.Id).ToArray();
                for (var index = 0; index < assistantConversations.Length; index++)
                    if (assistantConversations[index]?.Id == entry.Id && index != slot)
                        assistantConversations[index] = new AssistantConversation();
                if (entry.Id == chat.Id)
                    SwitchChat(new AssistantConversation { SourceIndex = chat.SourceIndex });
                else
                    RenderHistory();
                status.Text = $"Deleted “{entry.Title}”. It is in the Recycle Bin if you need it back.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                status.Text = "Could not delete that chat.";
                RenderHistory();
            }
        }

        void ResumeChat(SavedChat entry, int targetSlot)
        {
            if (entry.Id == chat.Id || busy || changingFiles)
                return;
            var resumed = FromSavedChat(entry);
            if (targetSlot == slot)
                SwitchChat(resumed);
            else
            {
                assistantConversations[targetSlot] = resumed;
                OpenAssistantConversation(targetSlot);
            }
        }

        AssistantConversation FromSavedChat(SavedChat entry)
        {
            var source = Array.FindIndex(AssistantSources, item => item.Provider == entry.Provider);
            var resumed = new AssistantConversation { Id = entry.Id, SourceIndex = Math.Max(0, source), Saved = entry };
            resumed.Turns.AddRange(entry.Turns);
            if (source >= 0 && entry.Model != null)
                assistantModels[source] = entry.Model;
            return resumed;
        }

        void SwitchChat(AssistantConversation next)
        {
            chat.Draft = prompt.Text;
            chat = assistantConversations[slot] = next;
            detailsCard.Visibility = Visibility.Collapsed;
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

        async Task SaveDetailsAsync()
        {
            if (busy || changingFiles)
                return;
            using var request = BeginRequest();
            var id = chat.Id;
            var name = chatTitle.Text;
            var group = chatFolder.Text;
            var pinned = pinChat.IsChecked == true;
            try
            {
                var saved = await Task.Run(() => DockChats.Organize(id, name, group, pinned));
                if (cancellation.IsCancellationRequested)
                    return;
                UpdateSavedChat(saved);
                detailsCard.Visibility = Visibility.Collapsed;
                status.Text = "Conversation details saved.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Could not save conversation details."; }
            finally { FinishRequest(); }
        }

        async Task BranchAsync(int through, SavedTurn? edit = null, bool regenerate = false)
        {
            if (busy || changingFiles || chat.Saved == null)
                return;
            using var request = BeginRequest();
            var id = chat.Id;
            var branched = false;
            try
            {
                var (branch, files) = await Task.Run(() =>
                {
                    var files = (edit?.Files ?? []).Select(file => DockChats.ReadAttachment(id, file)).ToArray();
                    return (DockChats.Branch(id, through), files);
                });
                if (cancellation.IsCancellationRequested)
                    return;
                var next = FromSavedChat(branch);
                if (edit != null)
                {
                    next.Draft = edit.Text;
                    next.Context = edit.Context;
                    next.Files.AddRange(files);
                }
                if (through > 0)
                    UpdateSavedChat(branch);
                SwitchChat(next);
                status.Text = edit != null ? "Edit the message in this new conversation." : "Conversation branched.";
                prompt.CaretIndex = prompt.Text.Length;
                branched = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { status.Text = "Could not branch this conversation. Check its saved attachments and storage folder."; }
            finally { FinishRequest(); }
            if (branched && regenerate)
                await SendAsync(retry: true);
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
                foreach (var item in picked)
                {
                    if (QueueFile(current, item) is { } error)
                        errors.Add($"{item.File.Name}: {error}");
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

        bool PasteScreenshot()
        {
            try
            {
                if (Clipboard.ContainsText() || !Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
                    return false;
                if (QueueFile(chat, ChatAttachments.Screenshot(image)) is { } error)
                    status.Text = error;
                else
                {
                    RenderAttachment();
                    status.Text = "Screenshot attached.";
                }
            }
            catch (Exception error) when (error is ExternalException or IOException or NotSupportedException or ArgumentException)
            { status.Text = "Could not paste this screenshot."; }
            return true;
        }

        static string? QueueFile(AssistantConversation chat, PickedChatFile item)
        {
            if (chat.Files.Count >= ChatAttachments.FileLimit)
                return "Attach up to six files per message.";
            if (item.File.MediaType != null && item.Bytes.Length +
                chat.Files.Where(file => file.File.MediaType != null).Sum(file => file.Bytes.Length) > ChatAttachments.MediaLimit)
                return "Images and PDFs can total up to 10 MB.";
            chat.Files.Add(item);
            return null;
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
            var snapshot = new SavedChat(chat.Id, persona.Id, chat.Saved?.Title ?? DockChats.Title(turns),
                DateTimeOffset.UtcNow, turns, provider, modelId, chat.Saved?.Pinned == true, chat.Saved?.Folder);
            await Task.Run(() => DockChats.Save(snapshot, files));
            if (!cancellation.IsCancellationRequested)
                UpdateSavedChat(snapshot);
        }

        void UpdateSavedChat(SavedChat snapshot)
        {
            savedChats = savedChats.Where(item => item.Id != snapshot.Id).Prepend(snapshot).ToArray();
            if (chat.Id == snapshot.Id)
            {
                chat.Saved = snapshot;
                title.Text = snapshot.Title;
            }
            RenderHistory();
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
            sendReady = !changingFiles && (!isPlan || planModels.SelectedItem != null && account != null &&
                assistantPlanCooldowns.GetValueOrDefault(account.ClientId) <= DateTimeOffset.UtcNow) &&
                (provider != AssistantProvider.Ollama || localModels.SelectedItem is string);
            // Send lights up only when there is something to send; Stop takes its place while a reply is coming.
            send.IsEnabled = !busy && sendReady && prompt.Text.Trim().Length > 0;
            send.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            if (busy && live.Visibility != Visibility.Visible)
            {
                live.Visibility = Visibility.Visible;
                conversation.ScrollToEnd();
            }
            else if (!busy)
                live.Visibility = Visibility.Collapsed;
            providers.IsEnabled = model.IsEnabled = key.IsEnabled = accounts.IsEnabled = planModels.IsEnabled = !busy;
            localModels.IsEnabled = refreshLocalModels.IsEnabled = !busy;
            addAccount.IsEnabled = connect.IsEnabled = !busy;
            disconnect.IsEnabled = !busy && (accounts.SelectedItem as PlanAccount)?.Refresh.Length > 0;
            foreach (Button button in starters.Children)
                button.IsEnabled = !busy;
            toolRow.IsEnabled = attachment.IsEnabled = proposal.IsEnabled = !busy;
            rail.IsEnabled = attachFiles.IsEnabled = !busy && !changingFiles;
            setupToggle.IsEnabled = !busy && !changingFiles;
            RefreshRetry();
            if (busy)
                modelMenu.IsOpen = false;
            prompt.IsReadOnly = busy || changingFiles;
            cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        void RefreshRetry()
        {
            chatFiles.IsEnabled = chat.Turns.Count > 0;
            organize.IsEnabled = !busy && !changingFiles && chat.Saved is { Turns.Length: > 0 };
            chatDetails.IsEnabled = !busy && !changingFiles;
            retryReply.Visibility = !busy && !changingFiles && chat.Turns.LastOrDefault()?.User == true
                ? Visibility.Visible : Visibility.Collapsed;
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
            RefreshRetry();
            title.Text = chat.Saved?.Title ?? DockChats.Title(history);
            welcome.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                // Actions sit quietly under each message: what you said can be edited or branched; a reply can be
                // copied, regenerated, saved, or branched.
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(user ? 0 : -6, 2, 0, 0) };
                if (user)
                {
                    var revise = MessageAction(0xE70F, "Edit in a new branch");
                    revise.Click += async (_, _) => await BranchAsync(number - 1, turn);
                    tools.Children.Add(revise);
                }
                else
                {
                    var copy = MessageAction(0xE8C8, "Copy reply");
                    copy.Click += (_, _) => CopyWithTick(copy, text);
                    tools.Children.Add(copy);
                    if (index > 0 && history[index - 1].User)
                    {
                        var regenerate = MessageAction(0xE72C, "Regenerate in a new branch");
                        regenerate.Click += async (_, _) => await BranchAsync(number - 1, null, regenerate: true);
                        tools.Children.Add(regenerate);
                    }
                    tools.Children.Add(MessageAction(0xE74E, "Save reply as Markdown", () => SaveReply($"reply-{number}.md", text)));
                }
                var branch = MessageAction(0xE8F1, "Branch from here");
                branch.Click += async (_, _) => await BranchAsync(number);
                tools.Children.Add(branch);
                if (!user && turn.ReplyProvider is { } provider)
                {
                    var from = Text("Answered by " + provider, 11, AssistantFaint);
                    from.VerticalAlignment = VerticalAlignment.Center;
                    from.Margin = new Thickness(6, 0, 0, 0);
                    tools.Children.Add(from);
                }
                if (user)
                {
                    var message = new TextBox
                    {
                        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0), Padding = new Thickness(0), Foreground = AssistantInk, FontSize = 13.5,
                        SelectionBrush = AssistantSecondary, CaretBrush = Brushes.Transparent, IsTabStop = false
                    };
                    AutomationProperties.SetName(message, "Your message");
                    line.Children.Add(message);
                }
                else
                {
                    line.Children.Add(FormatChatReply(text, number, SaveReply));
                    if (turn.OutputFiles is { Length: > 0 } outputs)
                    {
                        var saved = Text("Saved to this chat's files: " + string.Join(", ", outputs), 11, AssistantSecondary);
                        saved.TextWrapping = TextWrapping.Wrap;
                        saved.Margin = new Thickness(0, 4, 0, 0);
                        line.Children.Add(saved);
                    }
                }
                if (turn.Files is { Length: > 0 } files)
                {
                    var attached = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                    foreach (var file in files)
                    {
                        var chip = MakeButton(IconLabel(file.Name, file.MediaType?.StartsWith("image/") == true ? 0xEB9F : 0xE8A5,
                            AssistantSecondary), "Open " + file.Name, () => ReviewFile(file));
                        chip.Padding = new Thickness(9, 4, 9, 4);
                        chip.Margin = new Thickness(0, 0, 4, 4);
                        attached.Children.Add(chip);
                    }
                    line.Children.Add(attached);
                }
                FrameworkElement body = user ? UserBubble(line) : line;
                tools.HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                // Under your bubble the actions only show when you point at it, so the thread stays calm; under a reply
                // they always show, because that is where you reach for copy.
                var group = new StackPanel { Margin = new Thickness(user ? 56 : 0, user ? 6 : 2, 0, user ? 4 : 14), Background = Brushes.Transparent,
                    Children = { body, tools } };
                if (user)
                {
                    tools.Opacity = 0;
                    group.MouseEnter += (_, _) => tools.Opacity = 1;
                    group.MouseLeave += (_, _) => tools.Opacity = tools.IsKeyboardFocusWithin ? 1 : 0;
                    tools.GotKeyboardFocus += (_, _) => tools.Opacity = 1;
                    tools.LostKeyboardFocus += (_, _) => tools.Opacity = group.IsMouseOver ? 1 : 0;
                }
                messages.Children.Add(group);
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

        async Task SendAsync(bool retry = false)
        {
            if (busy || changingFiles)
                return;
            var index = chat.SourceIndex;
            var source = AssistantSources[index];
            var isPlan = source.Provider == AssistantProvider.ChatGptPlan;
            var isCli = source.Provider is AssistantProvider.CodexCli or AssistantProvider.ClaudeCode;
            var isLocal = source.Provider == AssistantProvider.Ollama;
            var last = chat.Turns.LastOrDefault();
            if (retry && last?.User != true)
                return;
            var message = retry ? last!.Text : prompt.Text.Trim();
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
            var picked = retry ? [] : chat.Files.ToArray();
            var outgoing = retry ? last! : new SavedTurn(true, message, picked.Select(item => item.File).ToArray(), chat.Context);
            if ((outgoing.Files ?? []).Any(item => item.MediaType != null) && !ChatMediaRequest.Supports(source.Provider))
            {
                status.Text = "Choose ChatGPT plan, Claude API, or Gemini to send images and PDFs.";
                modelMenu.IsOpen = true;
                return;
            }
            var recent = retry ? chat.Turns.TakeLast(9).ToArray() : chat.Turns.TakeLast(8).Append(outgoing).ToArray();
            var requestTurns = recent.Select(turn => (turn.User, AttachmentMessage(turn))).ToArray();
            var media = new Dictionary<int, ChatMedia[]>();
            var selectedFallback = fallbackOptions.SelectedIndex switch
            {
                1 => AssistantProvider.Gemini,
                2 => AssistantProvider.Grok,
                _ => (AssistantProvider?)null
            };
            var alternateIndex = Array.FindIndex(AssistantSources, item => item.Provider == selectedFallback);
            AssistantSource? usedFallback = null;
            string? fallbackModel = null;
            string? fallbackKey = null;
            var previewVersion = 0;
            var providerCalls = 0;
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
                        for (var turnIndex = recent.Length - (retry ? 1 : 2); turnIndex >= 0; turnIndex--)
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
                if (!retry)
                {
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
                }
                status.Text = $"Waiting for {source.Label}…";
                var learn = learning.IsChecked == true;
                var personaInstructions = (savedMemory.Length == 0 ? persona.Instructions : persona.Instructions +
                    "\nWhat this person has asked you to keep in mind (from earlier chats, written or approved by them): " +
                    JsonSerializer.Serialize(savedMemory)) + (learn ? AssistantLearning.Instruction : "");
                var response = await DockAssistantTurn.RunAsync(persona.Id, personaInstructions, requestTurns,
                    SendWithPreviewAsync,
                    progress =>
                    {
                        ++previewVersion;
                        liveText.Visibility = Visibility.Collapsed;
                        status.Text = progress;
                    }, request.Token, allowHandoff: persona.Id == "casual");
                ++previewVersion;
                liveText.Visibility = Visibility.Collapsed;
                if (cancellation.IsCancellationRequested)
                    return;
                if (string.IsNullOrWhiteSpace(response.Text))
                {
                    status.Text = "The provider returned an empty reply.";
                    return;
                }
                if (typed.Length > 0 && usedFallback == null)
                {
                    assistantKeys[index]?.Dispose();
                    assistantKeys[index] = key.SecurePassword;
                    try { DockCredentials.Save(source.Provider, typed); }
                    catch (Exception error) when (DockActions.IsActionError(error) || error is System.Security.Cryptography.CryptographicException) { }
                    key.Clear();
                }
                var reply = response.Text[..Math.Min(response.Text.Length, 40_000)];
                // A "REMEMBER:" line is bookkeeping for the memory, not part of the answer. Take it out either way, and
                // keep the note only while learning is on.
                var (visibleReply, notes) = AssistantLearning.Extract(reply);
                reply = visibleReply.Length > 0 ? visibleReply : "Noted.";
                string? learned = null;
                if (learn && notes.Length > 0)
                {
                    var (merged, added) = AssistantLearning.Merge(savedMemory, notes);
                    if (added.Length > 0)
                    {
                        try
                        {
                            DockCredentials.WritePrivateText(memoryFile, merged);
                            savedMemory = merged;
                            learned = $"{persona.Name} will remember: " + string.Join(" ", added);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                            System.Security.Cryptography.CryptographicException)
                        { learned = "Could not save what was learned. Open Memory to add it yourself."; }
                    }
                }
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
                chat.Turns.Add(new(false, reply, OutputFiles: outputs.ToArray(), ReplyProvider: usedFallback?.Label));
                chat.Actions = response.Actions;
                chat.Handoff = response.Handoff is { } handoff
                    ? new(handoff, (instructions, messages, token) => SendToProvider(instructions, messages, token)) : null;
                RenderConversation();
                RenderProposal();
                SetDisclosure();
                if (usedFallback != null)
                    status.Text = $"{usedFallback.Label} answered after {source.Label} reached a usage limit.";
                if (fileIssue != null)
                    status.Text = fileIssue;
                else if (learned != null)
                    status.Text = learned + " (Memory to change it)";
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
                ++previewVersion;
                liveText.Visibility = Visibility.Collapsed;
                liveText.Text = "";
                FinishRequest();
            }

            async Task<string> SendWithPreviewAsync(string instructions, IReadOnlyList<(bool User, string Text)> messages,
                CancellationToken token)
            {
                var firstCall = ++providerCalls == 1;
                try { return await SendToProvider(instructions, messages, token, media, MakePreview()); }
                catch (Exception error) when (firstCall && !isLocal && alternateIndex >= 0 && alternateIndex != index &&
                    (media.Count == 0 || ChatMediaRequest.Supports(selectedFallback!.Value)) &&
                    (error is PlanException { QuotaExceeded: true } ||
                     error is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired }))
                {
                    token.ThrowIfCancellationRequested();
                    var alternate = AssistantSources[alternateIndex];
                    var alternateKey = AssistantKey(alternateIndex);
                    var alternateModel = assistantModels[alternateIndex] ??
                        savedChats.FirstOrDefault(item => item.Provider == alternate.Provider && !string.IsNullOrWhiteSpace(item.Model))?.Model ??
                        alternate.Model;
                    if (string.IsNullOrWhiteSpace(alternateKey) || alternateKey.Any(char.IsWhiteSpace) || string.IsNullOrWhiteSpace(alternateModel))
                        throw;
                    usedFallback = alternate;
                    fallbackModel = alternateModel;
                    fallbackKey = alternateKey;
                    if (isPlan && account != null)
                        assistantPlanCooldowns[account.ClientId] = DateTimeOffset.UtcNow.AddMinutes(1);
                    status.Text = $"Usage limit reached. Waiting for {alternate.Label}…";
                    return await SendToProvider(instructions, messages, token, media, MakePreview());
                }
            }

            Action<string> MakePreview()
            {
                var version = ++previewVersion;
                liveText.Visibility = Visibility.Collapsed;
                return text => Dispatcher.InvokeAsync(() =>
                {
                    var prefix = text.AsSpan().TrimStart();
                    if (version != previewVersion || !busy || cancellation.IsCancellationRequested || prefix.IsEmpty ||
                        prefix[0] is '{' or '`')
                        return;
                    var follow = conversation.ScrollableHeight - conversation.VerticalOffset < 32;
                    liveText.Text = AssistantLearning.HidePartial(text);
                    liveText.Visibility = Visibility.Visible;
                    if (follow)
                        conversation.ScrollToEnd();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }

            Task<string> SendToProvider(string instructions, IReadOnlyList<(bool User, string Text)> messages, CancellationToken token,
                IReadOnlyDictionary<int, ChatMedia[]>? attachments = null, Action<string>? previewReply = null) =>
                usedFallback != null ? TextModelClient.SendChat(usedFallback.Provider, fallbackModel!, fallbackKey!, messages.ToArray(),
                    token, instructions, attachments, previewReply) :
                isPlan ? ChatGptPlan.SendAsync(account!, modelId, instructions, messages.ToArray(), token, attachments, previewReply) :
                    isLocal ? OllamaTextModel.Local.ChatAsync(modelId, instructions, messages, token, previewReply) :
                    isCli ? CodingSummaryClient.ChatAsync(source.Provider, modelId, instructions, messages, token) :
                    TextModelClient.SendChat(source.Provider, modelId, apiKey!, messages.ToArray(), token, instructions, attachments, previewReply);
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

    private StackPanel Labeled(string label, Control control)
    {
        var group = new StackPanel();
        group.Children.Add(Text(label, 11, AssistantSecondary));
        control.Margin = new Thickness(0, 3, 0, 0);
        group.Children.Add(control);
        return group;
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
