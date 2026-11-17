using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private Window? agentPanel;
    private AgentPlanUsage? codexPlan;
    private AgentPlanUsage? claudePlan;
    private DateTimeOffset nextUsageRefresh;
    private bool refreshingUsage;
    private Action? updateUsagePanel;
    private DateTimeOffset lastUsagePaint;
    private sealed record SummarySource(string Label, AssistantProvider Provider, string DefaultModel, string? KeyVariable);
    private sealed record SummaryPreferences(string? Provider, Dictionary<string, string>? Models);
    private const string SummaryPreferencesFile = "summary-preferences.json";
    private static readonly SummarySource[] SummarySources =
    [
        new("Gemini", AssistantProvider.Gemini, TextModelClient.GeminiSummaryModel, "GEMINI_API_KEY"),
        new("Groq", AssistantProvider.Groq, TextModelClient.GroqSummaryModel, "GROQ_API_KEY"),
        new("Codex", AssistantProvider.CodexCli, "", null),
        new("Claude Code", AssistantProvider.ClaudeCode, "", null)
    ];
    private readonly System.Security.SecureString?[] summaryKeys = new System.Security.SecureString?[SummarySources.Length];
    private (AgentSession Session, AgentReply? Reply)? lastReplyPreview;

    private async Task<AgentReply?> ReadAgentReplyAsync(AgentSession session, CancellationToken cancellation)
    {
        if (lastReplyPreview is { } cached && cached.Session == session)
            return cached.Reply;
        var reply = await Task.Run(() => AgentSessions.LatestReply(session, cancellation), cancellation);
        cancellation.ThrowIfCancellationRequested();
        lastReplyPreview = (session, reply);
        return reply;
    }

    private static bool IsSummaryModelId(string value) => value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':');

    private void ShowTextSummary(string title, string subtitle, string label, string? draft)
    {
        var (window, content, cancellation) = CreateDockPanel(title, subtitle, 468, 0xEAB7, AssistantInk);
        BuildTextSummary(content, label, draft, null, cancellation);
        window.Show();
    }

    private Action BuildTextSummary(StackPanel content, string label, string? draft, AgentSession? session,
        CancellationToken cancellation)
    {
        var preferences = DockData.Read<SummaryPreferences>(SummaryPreferencesFile);
        var activeSource = SummarySources.FirstOrDefault(source => source.Provider.ToString() == preferences?.Provider)
            ?? SummarySources[2];
        var savedProvider = activeSource.Provider;
        var models = preferences?.Models ?? new Dictionary<string, string>();
        var modelChanged = false;
        var autoPending = session != null;
        var options = new StackPanel { Visibility = autoPending ? Visibility.Collapsed : Visibility.Visible };
        if (session == null)
            content.Children.Add(options);
        var sourceLabel = Text(label, 11, AssistantSecondary, true);
        var source = Field(label, AgentSessions.SummaryLimit);
        source.Tag = session == null ? "Paste notifications here" : "Paste a reply";
        source.Text = draft ?? "";
        source.AcceptsReturn = true;
        source.TextWrapping = TextWrapping.Wrap;
        source.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        source.Height = 89;
        source.Margin = new Thickness(0, 4, 0, 5);
        options.Children.Add(sourceLabel);
        options.Children.Add(source);
        var sourceStatus = Text(session == null ? "Review the notifications before sending." : "Looking for the latest completed reply…",
            11, AssistantSecondary);
        sourceStatus.TextWrapping = TextWrapping.Wrap;
        options.Children.Add(sourceStatus);

        var fields = Columns(1, 1.25);
        fields.Margin = new Thickness(0, 9, 0, 0);
        var provider = new ComboBox { ItemsSource = SummarySources, DisplayMemberPath = nameof(SummarySource.Label),
            SelectedItem = activeSource, MinHeight = 38 };
        AutomationProperties.SetName(provider, "Summary provider");
        var model = Field("Summary model ID", 128);
        model.MinHeight = provider.MinHeight;
        Put(fields, Labeled("Provider", provider), 0);
        Put(fields, Labeled("Model", model), 1);
        options.Children.Add(fields);
        var key = new PasswordBox { MaxLength = 512 };
        var status = Text("", 11, AssistantSecondary);
        AutomationProperties.SetName(status, "Summary status");
        AutomationProperties.SetName(key, "Summary API key");
        var keyField = Labeled("API key · saved on this Windows account", key);
        keyField.Margin = new Thickness(0, 7, 0, 0);
        options.Children.Add(keyField);
        var savedKeyRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var changeKey = ActionButton("Change saved key", () =>
        {
            keyField.Visibility = Visibility.Visible;
            savedKeyRow.Visibility = Visibility.Collapsed;
            key.Focus();
        });
        changeKey.Margin = new Thickness(0, 0, 5, 0);
        savedKeyRow.Children.Add(changeKey);
        savedKeyRow.Children.Add(ActionButton("Forget saved key", () =>
        {
            try
            {
                DockCredentials.Forget(activeSource.Provider);
                summaryKeys[provider.SelectedIndex]?.Dispose();
                summaryKeys[provider.SelectedIndex] = null;
                key.Clear();
                SetKeyVisibility();
                status.Text = "Saved key removed.";
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { status.Text = error.Message; }
        }));
        options.Children.Add(savedKeyRow);

        var result = Field("Generated summary", 0);
        result.IsReadOnly = true;
        result.TextWrapping = TextWrapping.Wrap;
        result.VerticalScrollBarVisibility = session == null ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        result.MaxHeight = session == null ? 134 : double.PositiveInfinity;
        if (session != null)
        {
            result.Background = Brushes.Transparent;
            result.BorderBrush = Brushes.Transparent;
            result.Padding = new Thickness(0);
            result.FontSize = 13;
        }
        result.Visibility = Visibility.Collapsed;
        result.Margin = new Thickness(0, session == null ? 9 : 0, 0, 0);
        content.Children.Add(result);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 8, 0, 6);
        content.Children.Add(status);

        var actions = new WrapPanel();
        Button? edit = null;
        Button? copy = null;
        var send = PrimaryButton("Summarize");
        send.Click += async (_, _) => await SummarizeAsync();
        send.Margin = new Thickness(0, 0, 5, 3);
        actions.Children.Add(send);
        if (session != null)
        {
            copy = ActionButton("Copy summary", () =>
            {
                try { Clipboard.SetText(result.Text); status.Text = "Summary copied."; }
                catch (System.Runtime.InteropServices.ExternalException) { status.Text = "The clipboard is busy."; }
            });
            copy.IsEnabled = false;
            copy.Margin = new Thickness(0, 0, 5, 3);
            actions.Children.Add(copy);
            edit = ActionButton("Source & provider");
            edit.Click += (_, _) => ShowOptions(options.Visibility != Visibility.Visible);
            edit.Margin = new Thickness(0, 0, 5, 3);
            actions.Children.Add(edit);
        }
        content.Children.Add(actions);
        if (session != null)
        {
            options.Margin = new Thickness(0, 10, 0, 0);
            content.Children.Add(options);
        }
        provider.SelectionChanged += (_, _) =>
        {
            if (provider.SelectedItem is not SummarySource selected)
                return;
            RememberModel();
            activeSource = selected;
            ApplyProvider();
            SaveChoices();
        };
        model.LostKeyboardFocus += (_, _) => SaveModel();
        cancellation.Register(() =>
        {
            SaveModel();
            key.Clear();
        });
        ApplyProvider();
        if (autoPending)
            status.Text = "Reading the latest completed reply…";
        return () => { if (session != null) _ = ReadSourceAsync(session); };

        void ShowOptions(bool visible)
        {
            autoPending = false;
            options.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (edit == null)
                return;
            var labelText = visible ? "Hide source & provider" : "Source & provider";
            edit.Content = labelText;
            AutomationProperties.SetName(edit, labelText);
        }

        StackPanel Labeled(string label, Control control)
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(label, 11, AssistantSecondary));
            control.Margin = new Thickness(0, 3, 0, 0);
            panel.Children.Add(control);
            return panel;
        }

        void ApplyProvider()
        {
            var index = provider.SelectedIndex;
            var selected = activeSource;
            var cli = selected.KeyVariable == null;
            model.Text = models.TryGetValue(selected.Provider.ToString(), out var customModel) &&
                !string.IsNullOrEmpty(customModel) && IsSummaryModelId(customModel) ? customModel : selected.DefaultModel;
            model.Tag = cli ? "CLI default" : selected.DefaultModel;
            key.Clear();
            SetKeyVisibility();
            send.Content = "Summarize with " + selected.Label;
            AutomationProperties.SetName(send, "Summarize with " + selected.Label);
            status.Text = cli ? $"Uses your local {selected.Label} sign-in to summarize the text above."
                : $"Sends the text above to {selected.Label}.";
            if (selected.KeyVariable is { } variable && (summaryKeys[index] != null || DockCredentials.Read(selected.Provider) != null || EnvironmentKey(variable) != null))
                status.Text += " A key is available.";
        }

        void SetKeyVisibility()
        {
            var saved = activeSource.KeyVariable != null && DockCredentials.Read(activeSource.Provider) != null;
            keyField.Visibility = activeSource.KeyVariable == null || saved ? Visibility.Collapsed : Visibility.Visible;
            savedKeyRow.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        }

        string CurrentModel() => string.IsNullOrWhiteSpace(model.Text) ? activeSource.DefaultModel : model.Text.Trim();

        void RememberModel()
        {
            var modelId = CurrentModel();
            if (!IsSummaryModelId(modelId))
                return;
            var providerId = activeSource.Provider.ToString();
            if (modelId == activeSource.DefaultModel)
                modelChanged |= models.Remove(providerId);
            else if (!models.TryGetValue(providerId, out var previous) || previous != modelId)
            {
                models[providerId] = modelId;
                modelChanged = true;
            }
        }

        void SaveChoices()
        {
            if (!modelChanged && savedProvider == activeSource.Provider)
                return;
            try
            {
                DockData.Save(SummaryPreferencesFile, new SummaryPreferences(activeSource.Provider.ToString(), models));
                savedProvider = activeSource.Provider;
                modelChanged = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "Could not save your summary choices.";
            }
        }

        void SaveModel()
        {
            RememberModel();
            SaveChoices();
        }

        async Task ReadSourceAsync(AgentSession chat)
        {
            try
            {
                var reply = await ReadAgentReplyAsync(chat, cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
                if (source.Text.Length == 0)
                    source.Text = reply?.Text ?? "";
                sourceStatus.Text = reply switch
                {
                    { Clipped: true } => "Showing the first 12,000 characters of the latest local reply.",
                    not null => "Latest completed local reply. Edit the text before sending if needed.",
                    _ => "This chat has no readable local reply. Paste one above."
                };
                if (reply == null || string.IsNullOrWhiteSpace(source.Text))
                {
                    ShowOptions(true);
                    status.Text = sourceStatus.Text;
                }
                else if (autoPending)
                {
                    autoPending = false;
                    await SummarizeAsync();
                }
            }
            catch (OperationCanceledException) { }
        }

        async Task SummarizeAsync()
        {
            var reply = source.Text;
            var index = provider.SelectedIndex;
            if (!send.IsEnabled || provider.SelectedItem is not SummarySource selected || index < 0)
                return;
            var modelId = CurrentModel();
            SaveModel();
            var typed = selected.KeyVariable == null ? "" : key.Password.Trim();
            var apiKey = selected.KeyVariable is { } variable
                ? (typed.Length == 0 ? ReadSessionKey(index) ?? DockCredentials.Read(selected.Provider) ?? EnvironmentKey(variable) : typed)
                : null;
            if (string.IsNullOrWhiteSpace(reply))
            {
                ShowOptions(true);
                status.Text = "Enter text to summarize.";
                return;
            }
            if (selected.KeyVariable != null && (modelId.Length == 0 || string.IsNullOrWhiteSpace(apiKey)))
            {
                ShowOptions(true);
                status.Text = "Enter a model ID and an API key.";
                return;
            }
            if (reply.Length > AgentSessions.SummaryLimit || (apiKey?.Any(char.IsWhiteSpace) ?? false) ||
                !IsSummaryModelId(modelId))
            {
                status.Text = "Check the length of the reply, API key, or model ID.";
                return;
            }
            send.IsEnabled = provider.IsEnabled = model.IsEnabled = key.IsEnabled = false;
            if (copy != null)
                copy.IsEnabled = false;
            source.IsReadOnly = true;
            status.Text = "Summarizing…";
            result.Visibility = Visibility.Collapsed;
            try
            {
                var response = await TextModelClient.SummarizeAsync(selected.Provider, modelId, apiKey, reply, cancellation,
                    notifications: session == null);
                if (cancellation.IsCancellationRequested)
                    return;
                if (string.IsNullOrWhiteSpace(response))
                {
                    status.Text = "The provider returned no summary. Try another model or shorten the reply.";
                    return;
                }
                var saved = true;
                if (typed.Length > 0)
                {
                    RememberSessionKey(index, key);
                    try { DockCredentials.Save(selected.Provider, typed); key.Clear(); SetKeyVisibility(); }
                    catch (Exception error) when (DockActions.IsActionError(error) || error is System.Security.Cryptography.CryptographicException)
                    { saved = false; }
                }
                result.Text = response.Trim();
                result.Visibility = Visibility.Visible;
                if (copy != null)
                {
                    copy.IsEnabled = true;
                    send.Content = "Regenerate";
                    AutomationProperties.SetName(send, "Regenerate summary");
                }
                status.Text = $"Summary generated by {selected.Label}." + (saved ? "" : " Windows could not save the key.");
            }
            catch (CodingSummaryException error)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    ShowOptions(true);
                    status.Text = error.Message;
                }
            }
            catch (HttpRequestException error)
            {
                if (!cancellation.IsCancellationRequested)
                    status.Text = error.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Check that your API key has access to this model.",
                        HttpStatusCode.TooManyRequests => "The provider is rate limiting requests. Try again later.",
                        HttpStatusCode.BadRequest or HttpStatusCode.NotFound => "Check the model ID and provider settings.",
                        _ => "The provider could not be reached. Try again."
                    };
            }
            catch (OperationCanceledException)
            {
                if (!cancellation.IsCancellationRequested)
                    status.Text = "The provider took too long to respond.";
            }
            catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                status.Text = "The provider did not return a readable response.";
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    send.IsEnabled = provider.IsEnabled = model.IsEnabled = key.IsEnabled = true;
                    source.IsReadOnly = false;
                }
            }
        }
    }

    private string? ReadSessionKey(int index)
    {
        if (summaryKeys[index] is not { } key)
            return null;
        var data = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(key);
        try { return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(data).Trim(); }
        finally { System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(data); }
    }

    private void RememberSessionKey(int index, PasswordBox field)
    {
        var previousKey = summaryKeys[index];
        summaryKeys[index] = field.SecurePassword;
        previousKey?.Dispose();
        field.Clear();
    }

    private static string? EnvironmentKey(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } process ? process.Trim()
            : Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)?.Trim();
    }

    private void BuildAgentAssignments(StackPanel content, AgentSession session)
    {
        agentSlots = AgentSlots.Load();
        var slots = Columns(1, 1, 1, 1);
        var selected = Math.Clamp(agentSlot, 0, 3);
        var buttons = new Button[4];
        var labels = new TextBlock[4];
        var binding = Text("", 12, subdued);
        binding.TextWrapping = TextWrapping.Wrap;
        binding.Margin = new Thickness(0, 9, 0, 8);
        for (var index = 0; index < buttons.Length; index++)
        {
            var position = index;
            var body = new StackPanel();
            body.Children.Add(Text($"Slot {position + 1}", 12, strong: true));
            var description = Text("", 11, subdued);
            body.Children.Add(description);
            labels[position] = description;
            var button = MakeButton(body, $"Slot {position + 1}", () =>
            {
                selected = position;
                Describe();
            });
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Height = 53;
            buttons[position] = button;
            Put(slots, button, position);
        }
        content.Children.Add(slots);
        content.Children.Add(binding);
        var actions = new WrapPanel();
        var pin = PrimaryButton("Pin this chat", () => Save(new(session.Provider, session.DesktopId ?? session.Id, null, session.Title)));
        pin.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(pin);
        var project = ActionButton("Follow this project", () => Save(new(session.Provider, null, session.ProjectPath, session.Project)));
        project.IsEnabled = !string.IsNullOrWhiteSpace(session.ProjectPath);
        project.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(project);
        actions.Children.Add(ActionButton("Set to automatic", () => Save(null)));
        content.Children.Add(actions);
        Describe();

        void Describe()
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                labels[i].Text = agentSlots[i]?.Label ?? "Automatic";
                buttons[i].Background = i == selected ? controlSelected : ControlFill;
                buttons[i].BorderBrush = i == selected ? AssistantSecondary : Brushes.Transparent;
                AutomationProperties.SetItemStatus(buttons[i], i == selected ? "Selected" : "");
            }
            binding.Text = agentSlots[selected] is { } slot
                ? (slot.ChatId == null ? "Following " : "Pinned to ") + slot.Label : "Automatic · follows chat activity";
        }

        void Save(AgentSlot? slot)
        {
            var candidate = AgentSlots.Load();
            if (slot != null)
                for (var i = 0; i < candidate.Length; i++)
                    if (candidate[i] is { } existing && existing.Provider == slot.Provider &&
                        string.Equals(existing.ChatId, slot.ChatId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.ProjectPath, slot.ProjectPath, StringComparison.OrdinalIgnoreCase))
                        candidate[i] = null;
            candidate[selected] = slot;
            try
            {
                AgentSlots.Save(candidate);
                agentSlots = candidate;
                PublishAgentGrid();
                Describe();
                agentSlot = selected;
                agentMessage = $"Slot {selected + 1} updated.";
                updatePage?.Invoke();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                binding.Text = "The slot could not be saved.";
            }
        }
    }

    private void BuildAgentUsage(StackPanel content, AgentSession? selected, CancellationToken cancellation)
    {
        var body = new StackPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = ActionButton("Refresh", () => _ = RefreshUsageAsync(cancellation));
        refresh.Margin = new Thickness(0, 0, 6, 4);
        actions.Children.Add(refresh);
        content.Children.Add(actions);
        content.Children.Add(body);
        AgentTokenUsage? chatUsage = null;
        var loadedChat = selected == null;
        updateUsagePanel = Render;
        cancellation.Register(() =>
        {
            if (updateUsagePanel == Render)
                updateUsagePanel = null;
        });
        Render();
        _ = RefreshUsageAsync(cancellation);
        if (selected != null)
            _ = ReadChatAsync(selected);

        async Task ReadChatAsync(AgentSession session)
        {
            try
            {
                chatUsage = await Task.Run(() => AgentSessions.TokenUsage(session, cancellation), cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
                loadedChat = true;
                Render();
            }
            catch (OperationCanceledException) { }
        }

        void Render()
        {
            body.Children.Clear();
            refresh.IsEnabled = !refreshingUsage;
            if (selected != null)
            {
                var local = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                local.Children.Add(Text("This chat", 13, strong: true));
                if (chatUsage is { } tokens)
                {
                    local.Children.Add(Text(tokens.Total is { } total ? $"{total:N0} total · {tokens.Output:N0} output" :
                        $"{tokens.Input:N0} input tokens" + (tokens.Partial ? " · recent transcript" : " · local transcript"), 12, subdued));
                    if (tokens.CachedInput > 0)
                        local.Children.Add(Text($"{tokens.CachedInput:N0} cached input", 11, muted));
                }
                else
                    local.Children.Add(Text(loadedChat ? "Local chat counts unavailable." : "Reading chat tokens…", 11, muted));
                body.Children.Add(local);
            }
            AddPlan("Codex", codexPlan, AgentUsage.CodexUsageUrl);
            AddPlan("Claude", claudePlan, AgentUsage.ClaudeUsageUrl);
        }

        void AddPlan(string name, AgentPlanUsage? plan, string url)
        {
            var panel = new StackPanel();
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var link = IconButton(0xE8A7, "Open " + name + " usage page", () => OpenTarget(url));
            DockPanel.SetDock(link, Dock.Right);
            title.Children.Add(link);
            var label = Text(name, 13, strong: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(label);
            panel.Children.Add(title);
            if (plan == null)
                panel.Children.Add(Text(refreshingUsage ? "Checking usage…" : "Usage unavailable here.", 11, subdued));
            else
            {
                if (plan.DailyTokens is { } daily)
                    panel.Children.Add(Text($"{daily:N0} tokens today (UTC)", 12, subdued, true));
                if (plan.LifetimeTokens is { } total)
                    panel.Children.Add(Text($"{total:N0} lifetime tokens", 11, subdued));
                foreach (var quota in plan.Windows)
                    panel.Children.Add(QuotaRow(quota));
                if (plan.IsBlocked(DateTimeOffset.UtcNow))
                    panel.Children.Add(Text("You have hit the included usage limit.", 12, bad));
                panel.Children.Add(Text($"{plan.Source} · {plan.Observed.LocalDateTime:g}", 10, muted));
            }
            body.Children.Add(new Border { BorderBrush = AssistantRule, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 8), Child = panel });
        }
    }

    private FrameworkElement QuotaRow(AgentQuota quota)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = quota.ResetAt is { } reset && reset <= now;
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var line = new DockPanel();
        var value = quota.UsedPercent;
        var right = Text(value.HasValue ? $"{value:0.#}% used" : "Usage unavailable", 11, expired ? muted : subdued, true);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(right, Dock.Right);
        line.Children.Add(right);
        line.Children.Add(Text(quota.Label, 11, subdued, true));
        panel.Children.Add(line);
        if (value.HasValue)
        {
            var track = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 4) };
            track.Children.Add(new Border { Background = trackFill, CornerRadius = new CornerRadius(2) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value.Value, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - value.Value, GridUnitType.Star) });
            var fill = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = expired ? muted : value.Value >= 95 ? bad : value.Value >= 75 ? amber : AssistantInk
            };
            track.Children.Add(fill);
            Grid.SetColumnSpan(track.Children[0], 2);
            panel.Children.Add(track);
        }
        var countdown = quota.ResetAt is not { } next ? "Reset time unavailable" : expired ? "Reset passed · refresh for current usage" :
            $"Resets {next.LocalDateTime:g} · {ResetCountdown(next - now)}";
        panel.Children.Add(Text(countdown, 10, muted));
        return panel;
    }

    private static string ResetCountdown(TimeSpan left) =>
        left.TotalDays >= 1 ? $"{left.Days}d {left.Hours}h" :
        left.TotalHours >= 1 ? $"{left.Hours}h {left.Minutes}m" : $"{Math.Max(1, Math.Ceiling(left.TotalMinutes)):0}m";

    private void RefreshVisibleUsage()
    {
        var now = DateTimeOffset.UtcNow;
        if (updateUsagePanel == null || now - lastUsagePaint < TimeSpan.FromMinutes(1))
            return;
        lastUsagePaint = now;
        updateUsagePanel();
    }

    private async Task RefreshUsageAsync(CancellationToken cancellation)
    {
        if (refreshingUsage || DateTimeOffset.UtcNow < nextUsageRefresh)
            return;
        refreshingUsage = true;
        updateUsagePanel?.Invoke();
        try
        {
            var claude = Task.Run(AgentUsage.ReadClaudePlan, cancellation);
            var codex = CodexUsageClient.ReadAsync(cancellation);
            claudePlan = await claude;
            codexPlan = await codex ?? await Task.Run(AgentUsage.ReadCodexCache, cancellation);
            if (!cancellation.IsCancellationRequested && !closed)
            {
                nextUsageRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
                if (releaseWhenIdle && codexPlan is { Allowed: not null } plan)
                    agentGrid?.WriteQuota(plan.BlockedUntil(DateTimeOffset.UtcNow));
                PublishAgentGrid();
                updatePage?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            refreshingUsage = false;
            lastUsagePaint = DateTimeOffset.UtcNow;
            if (!closed)
                updateUsagePanel?.Invoke();
        }
    }

    private (Window Window, StackPanel Content, CancellationToken Cancellation) CreateDockPanel(
        string title, string subtitle, double width, int icon, SolidColorBrush tint, bool widget = false,
        Action? backAction = null, string backLabel = "Back to widgets", Border? portrait = null,
        DockSection? section = null, UIElement? body = null, double? height = null, bool expandable = false)
    {
        ParkToolPanel();
        ShowDockPanelHost();
        var returnFocus = FocusManager.GetFocusedElement(this) as FrameworkElement;
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        Window window = null!;
        var close = IconButton(0xE711, "Close " + title.ToLowerInvariant(), () => window.Close());
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        if (widget || backAction != null)
        {
            var back = IconButton(0xE76B, backLabel, backAction ?? ShowWidgets);
            back.Margin = new Thickness(-6, 0, 6, 0);
            header.Children.Add(back);
        }
        var heading = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        // Only the characters get a picture beside the title; a generic icon there says nothing the title does not.
        if (portrait != null)
        {
            var badge = portrait;
            badge.Margin = new Thickness(0, 0, 9, 0);
            heading.Children.Add(badge);
        }
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(title, 17, AssistantInk, strong: true));
        if (!string.IsNullOrEmpty(subtitle))
            labels.Children.Add(Text(subtitle, 12, AssistantSecondary));
        heading.Children.Add(labels);
        header.Children.Add(heading);
        var frame = new DockPanel();
        Action? backRoute = backAction ?? (widget ? (Action)ShowWidgets : null);
        DockPanel.SetDock(header, Dock.Top);
        frame.Children.Add(header);

        var screen = TaskbarScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        var room = Math.Max(160, (DockTaskbar.AnchorTop(screen.Bounds, dpi.DpiScaleY) - screen.Bounds.Top) /
            dpi.DpiScaleY - 12);
        var panelHeight = height ?? (title switch
        {
            "Assistants" => 302,
            "Widgets" => 558,
            "Notification summary" => 468,
            "Focus timer" => 218,
            _ => 436
        });
        var expanded = false;
        window = new Window
        {
            Owner = this, Title = title, Width = Math.Min(width, screen.WorkingArea.Width / dpi.DpiScaleX - 24),
            MaxHeight = room, Height = Math.Min(room, panelHeight), SizeToContent = SizeToContent.Manual,
            WindowStyle = WindowStyle.None, AllowsTransparency = false,
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : assistantShell,
            Foreground = AssistantInk, FontFamily = FontFamily, FontSize = FontSize, ResizeMode = ResizeMode.NoResize, Topmost = true,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, UseLayoutRounding = true
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/WindowsDock;component/DashboardControls.xaml", UriKind.Relative)
        });
        ApplyDashboardResources(window);
        var compactWidth = window.Width;
        var compactHeight = window.Height;
        if (expandable)
        {
            var full = IconButton(0xE740, "Toggle fullscreen", ToggleFullscreen);
            DockPanel.SetDock(full, Dock.Right);
            header.Children.Insert(1, full);
        }
        var footer = section is { } selected ? DockNavigation(window, selected) : DockGrip(window, swipe =>
        {
            if (swipe == DockSwipe.Right && backRoute != null)
            {
                nextDockSlide = -1;
                try { backRoute(); }
                finally { nextDockSlide = 0; }
            }
            else if (swipe == DockSwipe.Down)
                window.Close();
        }, null, false);
        DockPanel.SetDock(footer, Dock.Bottom);
        frame.Children.Add(footer);
        frame.Children.Add(body ?? new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            PanningMode = PanningMode.VerticalOnly });
        var card = Card(frame);
        card.CornerRadius = new CornerRadius(8);
        DockWindowFrame.Attach(window, () => false);
        card.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : assistantShell;
        card.Padding = new Thickness(18, 14, 18, 14);
        card.BorderBrush = AssistantRule;
        window.Content = card;

        var lifetime = new CancellationTokenSource();
        var cancellation = lifetime.Token;
        var entry = TrackPanel(window);
        var lockButton = LockPanelButton(title, () => entry.Locked, value => entry.Locked = value);
        DockPanel.SetDock(lockButton, Dock.Right);
        header.Children.Insert(1, lockButton);
        entry.Switcher = MakePanelSwitcher();
        DockPanel.SetDock(entry.Switcher, Dock.Right);
        header.Children.Insert(2, entry.Switcher);
        UpdatePanelSwitchers();
        window.Loaded += (_, _) => Position();
        window.SizeChanged += (_, _) => Position();
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() => Position());
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook((nint _, int message, nint _, nint _, ref bool _) =>
                {
                    if (message is 0x001A or 0x007E)
                        window.Dispatcher.BeginInvoke(() => { if (window.IsVisible) Position(); });
                    return 0;
                });
        };
        window.PreviewKeyDown += (_, args) =>
        {
            if (expandable && args.Key == Key.F11)
            {
                ToggleFullscreen();
                args.Handled = true;
                return;
            }
            if (args.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                args.Handled = true;
                ShowCommandPalette();
                return;
            }
            if (args.Key == Key.Escape && !args.Handled)
            {
                if (OpenComboBox(args.OriginalSource) is { } combo)
                {
                    args.Handled = true;
                    combo.IsDropDownOpen = false;
                    return;
                }
                args.Handled = true;
                window.Close();
            }
        };
        window.Closed += (_, _) =>
        {
            lifetime.Cancel();
            lifetime.Dispose();
            OnToolPanelClosed(entry, returnFocus);
        };
        var direction = nextDockSlide;
        var offset = new TranslateTransform();
        card.RenderTransform = offset;
        window.Loaded += (_, _) => SlideDockSurface(offset, direction, 12);
        return (window, panel, cancellation);

        void Position()
        {
            if (expanded || PresentationSource.FromVisual(window) is not HwndSource hwnd)
                return;
            var area = TaskbarScreen().Bounds;
            var scale = VisualTreeHelper.GetDpi(window);
            var x = area.Left + (area.Width - window.ActualWidth * scale.DpiScaleX) / 2;
            var y = Math.Max(area.Top, DockTaskbar.AnchorTop(area, scale.DpiScaleY) -
                (window.ActualHeight + 6) * scale.DpiScaleY);
            MoveWindowWithoutActivation(hwnd.Handle, (int)x, (int)y);
        }

        void ToggleFullscreen()
        {
            if (PresentationSource.FromVisual(window) is not HwndSource source)
                return;
            expanded = !expanded;
            if (expanded)
            {
                var bounds = TaskbarScreen().Bounds;
                window.MaxHeight = double.PositiveInfinity;
                SetWindowPos(source.Handle, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0014);
            }
            else
            {
                window.MaxHeight = room;
                window.Width = compactWidth;
                window.Height = compactHeight;
                Position();
            }
        }
    }

    private static ComboBox? OpenComboBox(object source)
    {
        for (var element = source as DependencyObject; element != null;
             element = element is Visual visual ? VisualTreeHelper.GetParent(visual) : LogicalTreeHelper.GetParent(element))
            if (element is ComboBox { IsDropDownOpen: true } combo)
                return combo;
        return null;
    }
}
