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
    private bool summaryGemini = true;
    private readonly string?[] summaryModels = new string?[2];
    private readonly System.Security.SecureString?[] summaryKeys = new System.Security.SecureString?[2];
    private (AgentSession Session, AgentReply? Reply)? lastReplyPreview;
    private Task<AgentReply?>? pendingReplyPreview;
    private AgentSession? previewSession;

    private async Task LoadReplyPreviewAsync(AgentSession session, TextBlock preview)
    {
        try
        {
            if (lastReplyPreview is not { } cached || cached.Session != session)
            {
                if (previewSession != session || pendingReplyPreview == null)
                {
                    previewSession = session;
                    pendingReplyPreview = Task.Run(() => AgentSessions.LatestReply(session, lifetime.Token), lifetime.Token);
                }
                var result = await pendingReplyPreview;
                if (previewSession == session)
                    lastReplyPreview = (session, result);
            }
            var reply = lastReplyPreview is { } latest && latest.Session == session ? latest.Reply : null;
            preview.Text = reply?.Text[..Math.Min(210, reply.Text.Length)] ?? "";
            preview.Visibility = reply == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
    }

    private void ShowReplySummary(AgentSession session) => ShowTextSummary("Reply summary", session.Title,
        "Reply to summarize", null, session);

    private void ShowTextSummary(string title, string subtitle, string label, string? draft, AgentSession? session = null)
    {
        var (window, content, cancellation) = CreateDockPanel(title, subtitle, 468, 0xEAB7, purple);
        var sourceLabel = Text(label, 11, subdued, true);
        var source = Field(label, AgentSessions.SummaryLimit);
        source.Tag = session == null ? "Paste notifications here" : "Paste a reply";
        source.Text = draft ?? "";
        source.AcceptsReturn = true;
        source.TextWrapping = TextWrapping.Wrap;
        source.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        source.Height = 89;
        source.Margin = new Thickness(0, 4, 0, 5);
        content.Children.Add(sourceLabel);
        content.Children.Add(source);
        var sourceStatus = Text(session == null ? "Review the notifications before sending." : "Looking for the latest completed reply…", 10, subdued);
        sourceStatus.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(sourceStatus);

        var fields = Columns(0.75, 1.25);
        fields.Margin = new Thickness(0, 9, 0, 0);
        var provider = new ComboBox { ItemsSource = new[] { "Gemini", "Groq" }, SelectedIndex = summaryGemini ? 0 : 1 };
        AutomationProperties.SetName(provider, "Summary provider");
        var model = Field("Summary model ID", 128);
        Put(fields, Labeled("Provider", provider), 0);
        Put(fields, Labeled("Model", model), 1);
        content.Children.Add(fields);
        var key = new PasswordBox { MaxLength = 512 };
        AutomationProperties.SetName(key, "Summary API key");
        var keyField = Labeled("API key for this dock session", key);
        keyField.Margin = new Thickness(0, 7, 0, 0);
        content.Children.Add(keyField);

        var result = Field("Generated summary", 0);
        result.IsReadOnly = true;
        result.TextWrapping = TextWrapping.Wrap;
        result.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        result.MinHeight = 44;
        result.MaxHeight = 134;
        result.Visibility = Visibility.Collapsed;
        result.Margin = new Thickness(0, 9, 0, 0);
        content.Children.Add(result);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 8, 0, 6);
        content.Children.Add(status);

        var actions = new WrapPanel();
        var send = AccentButton("Summarize", 0xEAB7, purple);
        send.Click += async (_, _) => await SummarizeAsync();
        send.Margin = new Thickness(0, 0, 5, 3);
        actions.Children.Add(send);
        if (session != null)
        {
            var open = ActionButton("Open chat", () => { window.Close(); OpenAgent(session); });
            open.Margin = new Thickness(0, 0, 5, 3);
            actions.Children.Add(open);
        }
        content.Children.Add(actions);
        provider.SelectionChanged += (_, _) => ApplyProvider();
        window.Closed += (_, _) => key.Clear();
        ApplyProvider();
        window.Show();
        if (session != null)
            _ = ReadSourceAsync(session);

        StackPanel Labeled(string label, Control control)
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(label, 10, subdued));
            control.Margin = new Thickness(0, 3, 0, 0);
            panel.Children.Add(control);
            return panel;
        }

        void ApplyProvider()
        {
            var index = provider.SelectedIndex;
            if (index is < 0 or > 1)
                return;
            summaryGemini = index == 0;
            model.Text = summaryModels[index] ?? (summaryGemini ? TextModelClient.GeminiSummaryModel : TextModelClient.GroqSummaryModel);
            key.Clear();
            var name = index == 0 ? "Gemini" : "Groq";
            send.Content = IconLabel("Summarize with " + name, 0xEAB7, purple);
            AutomationProperties.SetName(send, "Summarize with " + name);
            status.Text = "Sends the text above to " + name + ".";
            if (summaryKeys[index] != null || EnvironmentKey(index) != null)
                status.Text += " A key is available.";
        }

        async Task ReadSourceAsync(AgentSession chat)
        {
            try
            {
                var reply = lastReplyPreview is { } cached && cached.Session == chat ? cached.Reply :
                    await Task.Run(() => AgentSessions.LatestReply(chat, cancellation), cancellation);
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
            }
            catch (OperationCanceledException) { }
        }

        async Task SummarizeAsync()
        {
            var reply = source.Text;
            var index = provider.SelectedIndex;
            if (index is < 0 or > 1)
                return;
            var modelId = model.Text.Trim();
            var typed = key.Password.Trim();
            var apiKey = typed.Length == 0 ? ReadSessionKey(index) ?? EnvironmentKey(index) : typed;
            if (string.IsNullOrWhiteSpace(reply) || string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(apiKey))
            {
                status.Text = "Enter reply text, a model ID, and an API key.";
                return;
            }
            if (reply.Length > AgentSessions.SummaryLimit || apiKey.Any(char.IsWhiteSpace) ||
                modelId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or '/' or ':')))
            {
                status.Text = "Check the length of the reply, API key, or model ID.";
                return;
            }
            summaryModels[index] = modelId;
            send.IsEnabled = provider.IsEnabled = model.IsEnabled = key.IsEnabled = false;
            source.IsReadOnly = true;
            status.Text = "Summarizing…";
            result.Visibility = Visibility.Collapsed;
            try
            {
                var response = await TextModelClient.SummarizeAsync(index == 0, modelId, apiKey, reply, cancellation,
                    notifications: session == null);
                if (cancellation.IsCancellationRequested)
                    return;
                if (string.IsNullOrWhiteSpace(response))
                {
                    status.Text = "The provider returned no summary. Try another model or shorten the reply.";
                    return;
                }
                if (typed.Length > 0)
                    RememberSessionKey(index, key);
                result.Text = response.Trim();
                result.Visibility = Visibility.Visible;
                status.Text = "Summary generated by " + (index == 0 ? "Gemini" : "Groq") + ".";
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
            catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
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

    private static string? EnvironmentKey(int index)
    {
        var name = index == 0 ? "GEMINI_API_KEY" : "GROQ_API_KEY";
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } process ? process.Trim()
            : Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)?.Trim();
    }

    private void ShowAgentAssignments(AgentSession session)
    {
        var (window, content, _) = CreateDockPanel("Assign agent slot", session.Title, 428, 0xE718, purple);
        var items = new ComboBox { ItemsSource = Enumerable.Range(1, 4).Select(index => $"Slot {index}").ToArray(),
            SelectedIndex = Math.Clamp(agentSlot, 0, 3) };
        AutomationProperties.SetName(items, "Agent slot");
        content.Children.Add(items);
        var binding = Text("", 11, subdued);
        binding.Margin = new Thickness(0, 6, 0, 9);
        content.Children.Add(binding);
        var actions = new WrapPanel();
        var pin = ActionButton("Pin this chat", () => Save(new(session.Provider, session.DesktopId ?? session.Id, null, session.Title)));
        pin.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(pin);
        var project = ActionButton("Follow this project", () => Save(new(session.Provider, null, session.ProjectPath, session.Project)));
        project.IsEnabled = !string.IsNullOrWhiteSpace(session.ProjectPath);
        project.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(project);
        actions.Children.Add(ActionButton("Set to automatic", () => Save(null)));
        content.Children.Add(actions);
        items.SelectionChanged += (_, _) => Describe();
        Describe();
        window.Show();

        void Describe() => binding.Text = agentSlots[items.SelectedIndex] is { } slot
            ? (slot.ChatId == null ? "Following " : "Pinned to ") + slot.Label : "Automatic · follows chat activity";

        void Save(AgentSlot? slot)
        {
            var candidate = (AgentSlot?[])agentSlots.Clone();
            if (slot != null)
                for (var i = 0; i < candidate.Length; i++)
                    if (candidate[i] is { } existing && existing.Provider == slot.Provider &&
                        string.Equals(existing.ChatId, slot.ChatId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.ProjectPath, slot.ProjectPath, StringComparison.OrdinalIgnoreCase))
                        candidate[i] = null;
            candidate[items.SelectedIndex] = slot;
            try
            {
                AgentSlots.Save(candidate);
                agentSlots = candidate;
                PublishAgentGrid();
                Describe();
                agentMessage = $"Slot {items.SelectedIndex + 1} updated.";
                window.Close();
                UpdateDashboard();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                binding.Text = "The slot could not be saved.";
            }
        }
    }

    private void ShowAgentUsage()
    {
        var (window, content, cancellation) = CreateDockPanel("Token usage", "", 468, 0xE9D9, accent);
        var body = new StackPanel();
        content.Children.Add(body);
        var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var refresh = ActionButton("Refresh", () => _ = RefreshUsageAsync(cancellation));
        refresh.Margin = new Thickness(0, 0, 6, 4);
        actions.Children.Add(refresh);
        content.Children.Add(actions);
        var selected = selectedAgent is { } id ? agentSessions.FirstOrDefault(session => session.Provider == id.Provider &&
            session.Id == id.Id) : RankedSessions(pendingApprovals).FirstOrDefault();
        AgentTokenUsage? chatUsage = null;
        var loadedChat = selected == null;
        updateUsagePanel = Render;
        window.Closed += (_, _) => updateUsagePanel = null;
        window.Show();
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
            AddPlan("Codex", codexPlan, AgentUsage.CodexUsageUrl);
            AddPlan("Claude", claudePlan, AgentUsage.ClaudeUsageUrl);
            if (selected == null)
                return;
            var local = new StackPanel();
            local.Children.Add(Text(selected.Title, 12, strong: true));
            if (chatUsage is { } tokens)
            {
                local.Children.Add(Text(tokens.Total is { } total ? $"{total:N0} total · {tokens.Output:N0} output" :
                    $"{tokens.Input:N0} input tokens" + (tokens.Partial ? " · recent transcript" : " · local transcript"), 12, subdued));
                if (tokens.CachedInput > 0)
                    local.Children.Add(Text($"{tokens.CachedInput:N0} cached input", 11, muted));
            }
            else
                local.Children.Add(Text(loadedChat ? "Local chat counts unavailable." : "Reading chat tokens…", 11, muted));
            var card = Card(local);
            card.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(card);
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
                    panel.Children.Add(Text("Included usage is limited", 11, Brush("#FF879E"), true));
                panel.Children.Add(Text($"{plan.Source} · {plan.Observed.LocalDateTime:g}", 10, muted));
            }
            var border = Card(panel);
            border.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(border);
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
            track.Children.Add(new Border { Background = Brush("#24FFFFFF"), CornerRadius = new CornerRadius(2) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value.Value, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - value.Value, GridUnitType.Star) });
            var fill = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = expired ? muted : value.Value >= 95 ? Brush("#FF879E") : value.Value >= 75 ? amber : accent
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
        string title, string subtitle, double width, int icon, SolidColorBrush tint, bool widget = false)
    {
        agentPanel?.Close();
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = IconButton(0xE711, "Close " + title.ToLowerInvariant(), () => agentPanel?.Close());
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        if (widget)
        {
            var back = IconButton(0xE76B, "Back to widgets", ShowWidgets);
            back.Margin = new Thickness(0, 0, 4, 0);
            header.Children.Add(back);
        }
        var heading = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var badge = GlyphBadge(icon, tint, 33);
        badge.Margin = new Thickness(0, 0, 9, 0);
        heading.Children.Add(badge);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(title, 19, strong: true));
        if (!string.IsNullOrEmpty(subtitle))
            labels.Children.Add(Text(subtitle, 11, subdued));
        heading.Children.Add(labels);
        header.Children.Add(heading);
        var frame = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        frame.Children.Add(header);

        var screen = TaskbarScreen();
        var dpi = VisualTreeHelper.GetDpi(this);
        var window = new Window
        {
            Owner = this, Title = title, Width = Math.Min(width, screen.WorkingArea.Width / dpi.DpiScaleX - 24),
            MaxHeight = screen.Bounds.Height / dpi.DpiScaleY - 80, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            Foreground = Foreground, FontFamily = FontFamily, ResizeMode = ResizeMode.NoResize, Topmost = true,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, UseLayoutRounding = true
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/WindowsDock;component/DashboardControls.xaml", UriKind.Relative)
        });
        frame.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var card = Card(frame);
        card.CornerRadius = new CornerRadius(21);
        card.Background = new LinearGradientBrush(Color.FromRgb(37, 28, 53), Color.FromRgb(23, 21, 35), 55);
        card.BorderBrush = AgentArt.Wash(tint, 123);
        card.Padding = new Thickness(15);
        window.Content = card;

        var lifetime = new CancellationTokenSource();
        var cancellation = lifetime.Token;
        modalOpen = true;
        agentPanel = window;
        window.Loaded += (_, _) => Position();
        window.SizeChanged += (_, _) => Position();
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                args.Handled = true;
                ShowCommandPalette();
                return;
            }
            if (args.Key == Key.Escape && !args.Handled)
            {
                args.Handled = true;
                window.Close();
            }
        };
        window.Closed += (_, _) =>
        {
            lifetime.Cancel();
            lifetime.Dispose();
            agentPanel = null;
            modalOpen = false;
        };
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            var offset = new TranslateTransform();
            card.RenderTransform = offset;
            window.Loaded += (_, _) => offset.BeginAnimation(TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(165))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                });
        }
        return (window, panel, cancellation);

        void Position()
        {
            if (PresentationSource.FromVisual(window) is not HwndSource hwnd)
                return;
            var area = TaskbarScreen().Bounds;
            var scale = VisualTreeHelper.GetDpi(window);
            var x = area.Left + (area.Width - window.ActualWidth * scale.DpiScaleX) / 2;
            var y = Math.Max(area.Top, area.Bottom - (window.ActualHeight + 58) * scale.DpiScaleY);
            MoveWindowWithoutActivation(hwnd.Handle, (int)x, (int)y);
        }
    }
}
