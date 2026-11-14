using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WindowsDock;

public partial class DashboardWindow
{
    private AgentSession[] agentSessions = [];
    private readonly AgentGrid? agentGrid;
    private AgentSlot?[] agentSlots = AgentSlots.Load();
    private bool readingAgents;
    private DateTime nextAgentRead;
    private AgentApproval[] pendingApprovals = [];
    private AgentProvider? agentFilter;
    private bool agentSetup;
    private int agentSlot = -1;
    private (AgentProvider Provider, string Id)? selectedAgent;
    private bool showAgentList;
    private readonly HashSet<Guid> sentApprovals = [];
    private string agentMessage = "";

    private UIElement AgentsPage()
    {
        var layout = new StackPanel { Margin = new Thickness(5, 3, 5, 2) };
        var toolbar = new DockPanel { Margin = new Thickness(1, 0, 1, 9) };
        var close = IconButton(0xE711, "Close coding chats", Dismiss);
        DockPanel.SetDock(close, Dock.Right);
        if (compactAgents)
            toolbar.Children.Add(close);
        var setup = IconButton(0xE713, agentSetup ? "Back to chats" : "Connections and defaults", () =>
        {
            agentSetup = !agentSetup;
            RefreshAgentPage();
        });
        DockPanel.SetDock(setup, Dock.Right);
        toolbar.Children.Add(setup);
        var usage = ActionButton("Usage", ShowAgentUsage);
        DockPanel.SetDock(usage, Dock.Right);
        usage.Margin = new Thickness(0, 0, 6, 0);
        toolbar.Children.Add(usage);
        var heading = Text(agentSetup ? "Connections" : "Coding chats", 17, strong: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(heading);
        layout.Children.Add(toolbar);

        if (agentSetup)
        {
            var settings = new StackPanel();
            settings.Children.Add(AgentSettingsCard(AgentProvider.Claude));
            var codex = AgentSettingsCard(AgentProvider.Codex);
            codex.Margin = new Thickness(0, 6, 0, 0);
            settings.Children.Add(codex);
            layout.Children.Add(new ScrollViewer
            {
                Content = settings, MaxHeight = 415, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            return layout;
        }

        var detail = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        layout.Children.Add(detail);
        var filters = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        var all = ActionButton(showAgentList ? "Hide chats" : "All chats", () =>
        {
            showAgentList = !showAgentList;
            RefreshAgentPage();
        });
        filters.Children.Add(all);
        foreach (var (label, provider) in new[] { ("Claude", AgentProvider.Claude), ("Codex", AgentProvider.Codex) })
        {
            var filter = ActionButton(label, () =>
            {
                agentFilter = agentFilter == provider ? null : provider;
                showAgentList = true;
                RefreshAgentPage();
            });
            filter.Margin = new Thickness(4, 0, 0, 0);
            filter.Background = agentFilter == provider && showAgentList ? Brush("#38FFFFFF") : Brushes.Transparent;
            filters.Children.Add(filter);
        }
        layout.Children.Add(filters);

        var notice = Text(agentMessage, 11, subdued);
        notice.TextWrapping = TextWrapping.Wrap;
        layout.Children.Add(notice);
        var cards = new StackPanel();
        layout.Children.Add(new ScrollViewer
        {
            Content = cards, MaxHeight = 194, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        AgentSession[]? shown = null;
        AgentApproval[]? shownApprovals = null;
        (AgentProvider Provider, string Id)? detailKey = null;
        updatePage = () =>
        {
            sentApprovals.RemoveWhere(id => !pendingApprovals.Any(request => request.Id == id));
            _ = RefreshAgentsAsync();
            var approval = pendingApprovals.Where(request => !sentApprovals.Contains(request.Id)).ToArray();
            var ordered = RankedSessions(approval);
            var focused = selectedAgent is { } key
                ? ordered.FirstOrDefault(session => session.Provider == key.Provider && session.Id == key.Id)
                : ordered.FirstOrDefault();
            var visible = ordered.Where(session => !agentFilter.HasValue || session.Provider == agentFilter).ToArray();
            notice.Text = agentMessage;
            notice.Visibility = string.IsNullOrEmpty(agentMessage) ? Visibility.Collapsed : Visibility.Visible;
            var focusKey = focused == null ? ((AgentProvider, string)?)null : (focused.Provider, focused.Id);
            if (shown != null && shown.SequenceEqual(visible) && shownApprovals != null &&
                shownApprovals.SequenceEqual(approval) && detailKey == focusKey)
                return;
            shown = visible;
            shownApprovals = approval;
            detailKey = focusKey;
            var focusedApproval = approval.FirstOrDefault(item => focused != null &&
                item.Provider == focused.Provider && item.SessionId == focused.Id);
            detail.Content = focused == null
                ? Card(Text("No local coding chats yet.", 13, subdued))
                : AgentDetail(focused, focusedApproval);
            cards.Children.Clear();
            if (!showAgentList)
                return;
            foreach (var session in visible)
            {
                var row = AgentRow(session, focusKey == (session.Provider, session.Id));
                row.Margin = new Thickness(0, 0, 0, 4);
                cards.Children.Add(row);
            }
            if (visible.Length == 0)
                cards.Children.Add(Text("No chats for this provider.", 12, subdued));
        };
        return layout;
    }

    private async Task RefreshAgentsAsync()
    {
        if (readingAgents || DateTime.UtcNow < nextAgentRead)
            return;

        readingAgents = true;
        try
        {
            var next = await Task.Run(() => (Sessions: AgentSessions.Read().ToArray(), Approvals: AgentApprovals.Read()), lifetime.Token);
            if (closed)
                return;
            agentSessions = next.Sessions;
            pendingApprovals = next.Approvals;
            PublishAgentGrid();
        }
        catch (OperationCanceledException) { }
        finally
        {
            readingAgents = false;
            nextAgentRead = DateTime.UtcNow.AddSeconds(3);
        }
        if (!closed && currentPage == DashboardPage.Agents && IsVisible)
            updatePage?.Invoke();
    }

    internal void OpenAgentGrid()
    {
        agentPanel?.Close();
        compactAgents = true;
        agentSetup = false;
        showAgentList = true;
        agentFilter = null;
        var selection = agentGrid?.ReadSelection() ?? (-1, (AgentProvider?)null, "");
        agentSlot = selection.Item1;
        pendingApprovals = AgentApprovals.Read();
        var ranked = RankedSessions(pendingApprovals.Where(request => !sentApprovals.Contains(request.Id)).ToArray());
        var urgent = ranked.FirstOrDefault(session => session.State == AgentState.Attention);
        var clicked = ranked.FirstOrDefault(session => session.Provider == selection.Item2 && session.Id == selection.Item3);
        var finished = ranked.FirstOrDefault(session => session.State == AgentState.Finished &&
            DateTimeOffset.UtcNow - session.Updated < TimeSpan.FromMinutes(3));
        var focused = urgent ?? (clicked?.State == AgentState.Finished ? clicked : finished) ?? clicked;
        selectedAgent = focused == null ? null : (focused.Provider, focused.Id);
        ConfigureWidth();
        RefreshAgentPage();
        PositionAtTaskbar();
        RevealAgentPage();
    }

    private void RefreshAgentPage() => ShowPage(DashboardPage.Agents);

    private void RevealAgentPage()
    {
        if (IsVisible && SystemParameters.ClientAreaAnimation)
            revealOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(13, 0,
                TimeSpan.FromMilliseconds(175)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private AgentSession[] RankedSessions(AgentApproval[] approvals)
    {
        var now = DateTimeOffset.UtcNow;
        var map = agentSessions.ToDictionary(session => (session.Provider, session.Id));
        foreach (var approval in approvals)
        {
            var key = (approval.Provider, approval.SessionId);
            var session = map.GetValueOrDefault(key) ?? new AgentSession(approval.Provider, approval.SessionId,
                approval.Project, approval.Project, AgentState.Attention, "", approval.Expires.AddSeconds(-30),
                ProjectPath: approval.WorkingDirectory);
            map[key] = session with { State = AgentState.Attention, Step = "Approval for " + approval.Tool };
        }
        return map.Values.Select(session => session.Provider == AgentProvider.Codex &&
                session.State is AgentState.Recent or AgentState.Ready && codexPlan?.IsBlocked(now) == true
                    ? session with { State = AgentState.QuotaReached, Step = "Account usage limit reached" } : session)
            .OrderBy(session => session.State switch
            {
                AgentState.Attention => 0,
                AgentState.QuotaReached => 1,
                AgentState.Working => 2,
                AgentState.Finished when now - session.Updated < TimeSpan.FromMinutes(3) => 3,
                _ => 4
            }).ThenByDescending(session => session.Updated).ToArray();
    }

    private void PublishAgentGrid() => agentGrid?.Publish(AgentSlots.Select(agentSlots, RankedSessions(pendingApprovals)));

    private Button AgentRow(AgentSession session, bool selected)
    {
        var line = new Grid { Width = Math.Max(210, Math.Min(424, Width - 76)) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text(session.Title, 12);
        line.Children.Add(title);
        var (label, color) = StateBadge(session.State);
        var state = Text(label, 10, color);
        state.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(state, 1);
        line.Children.Add(state);
        var button = MakeButton(line, session.Title + ", " + label, () =>
        {
            selectedAgent = (session.Provider, session.Id);
            RefreshAgentPage();
        });
        button.Background = selected ? Brush("#24FFFFFF") : Brush("#10FFFFFF");
        button.ToolTip = AgentTooltip(session);
        return button;
    }

    private Border AgentDetail(AgentSession session, AgentApproval? approval)
    {
        var panel = new StackPanel();
        var hero = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var face = AgentAvatar(session.State);
        face.Margin = new Thickness(0, 0, 10, 0);
        hero.Children.Add(face);
        var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(session.Title, 15, strong: true);
        title.ToolTip = session.Title;
        description.Children.Add(title);
        var (label, color) = StateBadge(session.State);
        description.Children.Add(Text(ProviderName(session.Provider) + "  ·  " + label, 11, color));
        hero.Children.Add(description);
        panel.Children.Add(hero);
        var info = string.Join("  ·  ", new[] { session.Project, session.Step, session.Model, session.Effort }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var details = Text(info, 11, subdued);
        details.ToolTip = AgentTooltip(session);
        panel.Children.Add(details);

        if (approval != null)
        {
            var review = ApprovalCard(approval);
            review.Margin = new Thickness(0, 7, 0, 0);
            panel.Children.Add(review);
        }
        else if (session.State == AgentState.Finished)
        {
            var preview = Text("", 11, subdued);
            preview.MaxHeight = 48;
            preview.TextWrapping = TextWrapping.Wrap;
            preview.Margin = new Thickness(0, 7, 0, 0);
            panel.Children.Add(preview);
            _ = LoadReplyPreviewAsync(session, preview);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        AddAction(approval == null ? "Summarize reply" : "Last reply", () => ShowReplySummary(session));
        AddAction("Open chat", () => OpenAgent(session));
        if (session.State == AgentState.QuotaReached)
            AddAction("Token usage", ShowAgentUsage);
        AddAction("Assign slot", () => ShowAgentAssignments(session));
        panel.Children.Add(actions);
        return Card(panel);

        void AddAction(string text, Action action)
        {
            var button = ActionButton(text, action);
            button.Margin = new Thickness(0, 0, 5, 3);
            actions.Children.Add(button);
        }
    }

    private string AgentTooltip(AgentSession session) => string.Join("\n", new[]
    {
        session.Title, ProviderName(session.Provider) + " · " + StateBadge(session.State).Label,
        session.Step, session.Model, session.Effort, session.Updated.ToLocalTime().ToString("g")
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private Border AgentAvatar(AgentState state)
    {
        var (_, color) = StateBadge(state);
        var glyph = state switch
        {
            AgentState.Attention => "!", AgentState.Finished => "✓",
            AgentState.Failed => "×", AgentState.QuotaReached or AgentState.Interrupted => "–", _ => "••"
        };
        var face = Text(glyph, 17, color, true);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        face.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border
        {
            Width = 42, Height = 39, CornerRadius = new CornerRadius(12),
            Background = Brush("#16FFFFFF"), BorderBrush = color, BorderThickness = new Thickness(1), Child = face
        };
        if (SystemParameters.ClientAreaAnimation && state is AgentState.Working or AgentState.Attention or
            AgentState.QuotaReached or AgentState.Finished)
        {
            badge.Loaded += (_, _) => face.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1,
                TimeSpan.FromMilliseconds(state == AgentState.Finished ? 280 : 790))
            {
                AutoReverse = state != AgentState.Finished,
                RepeatBehavior = state == AgentState.Finished ? new RepeatBehavior(1) : RepeatBehavior.Forever
            });
            badge.Unloaded += (_, _) => face.BeginAnimation(OpacityProperty, null);
        }
        return badge;
    }

    private Border ApprovalCard(AgentApproval request)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text($"{ProviderName(request.Provider)} · {request.Project} · {request.Tool}", 13, amber, true));
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            var directory = Text(request.WorkingDirectory, 11, subdued);
            directory.ToolTip = request.WorkingDirectory;
            panel.Children.Add(directory);
        }
        var details = new TextBox
        {
            IsReadOnly = true, Text = request.Details, TextWrapping = TextWrapping.Wrap,
            MaxHeight = 122, FontSize = 11, FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#28000000"),
            Foreground = Foreground, BorderThickness = new Thickness(0), Padding = new Thickness(7),
            Margin = new Thickness(0, 6, 0, 6)
        };
        AutomationProperties.SetName(details, "Approval details");
        panel.Children.Add(details);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var allow = ActionButton("Allow once", () => _ = Decide(true));
        var deny = ActionButton("Deny", () => _ = Decide(false));
        allow.Background = Brush("#245848");
        allow.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(allow);
        actions.Children.Add(deny);
        panel.Children.Add(actions);
        return Card(panel);

        async Task Decide(bool allowRequest)
        {
            actions.IsEnabled = false;
            var delivered = await AgentApprovals.DecideAsync(request, allowRequest);
            if (delivered)
                sentApprovals.Add(request.Id);
            agentMessage = delivered ? "Decision sent." : "The request is no longer reachable here. Check the provider’s prompt.";
            if (!closed && currentPage == DashboardPage.Agents)
                updatePage?.Invoke();
        }
    }

    private Border AgentSettingsCard(AgentProvider provider)
    {
        var card = new StackPanel();
        card.Children.Add(Text(ProviderName(provider), 15, strong: true));
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 7, 0, 0);

        var installed = AgentHookSetup.IsInstalled(provider);
        var approvals = new CheckBox
        {
            Content = "Allow or deny individual requests here", Foreground = Foreground,
            IsChecked = AgentHookSetup.IsInstalled(provider, approvals: true),
            Margin = new Thickness(0, 7, 0, 5)
        };
        card.Children.Add(approvals);
        var hookButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
        var connect = ActionButton(installed ? "Update live hooks" : "Enable live hooks", () =>
        {
            Apply(() => AgentHookSetup.SetInstalled(provider, true, approvals.IsChecked == true),
                provider == AgentProvider.Codex
                    ? "Saved. Review the hooks in Codex Settings → Hooks to activate them."
                    : "Saved. Start or resume a local Claude Code session to load the hooks.");
        });
        connect.Margin = new Thickness(0, 0, 6, 0);
        hookButtons.Children.Add(connect);
        hookButtons.Children.Add(ActionButton("Remove dock hooks", () =>
            Apply(() => AgentHookSetup.SetInstalled(provider, false), "Dock hooks removed.")));
        card.Children.Add(hookButtons);

        var model = Field("Default model", 128);
        model.Tag = "Model ID or alias";
        var effort = new ComboBox
        {
            Margin = new Thickness(0, 4, 0, 7),
            ItemsSource = provider == AgentProvider.Claude ? new[] { "Keep current", "low", "medium", "high", "xhigh" } :
                new[] { "Keep current", "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" }
        };
        effort.SelectedIndex = 0;
        AutomationProperties.SetName(effort, "Default thinking effort");
        try
        {
            var defaults = AgentDefaults.Read(provider);
            model.Text = defaults.Model ?? "";
            if (defaults.Effort != null && effort.Items.Contains(defaults.Effort))
                effort.SelectedItem = defaults.Effort;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or Tomlyn.TomlException)
        {
            status.Text = "Could not read the provider settings.";
        }
        card.Children.Add(Text("Defaults for new sessions", 11, subdued, true));
        model.Margin = new Thickness(0, 4, 0, 0);
        card.Children.Add(model);
        card.Children.Add(effort);
        var save = ActionButton("Save defaults", () =>
            Apply(() => AgentDefaults.Save(provider, model.Text, effort.SelectedIndex > 0 ? (string)effort.SelectedItem : ""),
                "Saved. The provider applies supported defaults when it loads its settings."));
        save.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(save);
        card.Children.Add(status);
        return Card(card);

        void Apply(Action change, string success)
        {
            try { change(); status.Text = success; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
                                               System.Text.Json.JsonException or Tomlyn.TomlException)
            {
                status.Text = "Settings could not be saved. " + (error is InvalidOperationException ? error.Message : "Check the provider config.");
            }
        }
    }

    private void OpenAgent(AgentSession session)
    {
        if (session.Provider == AgentProvider.Codex && Guid.TryParse(session.Id, out var id))
        {
            try { ShellIntegration.Launch("codex://threads/" + id.ToString("D")); return; }
            catch (Win32Exception) { }
        }
        if (session.Provider == AgentProvider.Claude && session.DesktopId is { } desktop &&
            desktop.StartsWith("local_", StringComparison.Ordinal) && Guid.TryParseExact(desktop[6..], "D", out _))
        {
            try { ShellIntegration.Launch("claude://claude.ai/code/" + desktop); return; }
            catch (Win32Exception) { }
        }
        var app = ShellIntegration.Windows().FirstOrDefault(item =>
        {
            var name = Path.GetFileNameWithoutExtension(item.Path);
            return session.Provider == AgentProvider.Claude ? name.Equals("claude", StringComparison.OrdinalIgnoreCase) :
                name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) || name.Equals("codex", StringComparison.OrdinalIgnoreCase);
        });
        if (app != null && ShellIntegration.Switch(app.Handle, 0))
            return;
        OpenTarget(session.Provider == AgentProvider.Claude ? "claude://" : "codex://");
    }

    private static string ProviderName(AgentProvider provider) => provider == AgentProvider.Claude ? "Claude Code" : "Codex";

    private (string Label, Brush Color) StateBadge(AgentState state) => state switch
    {
        AgentState.Working => ("Working", accent),
        AgentState.Attention => ("Needs approval", amber),
        AgentState.Finished => ("Reply ready", green),
        AgentState.Failed => ("Failed", Brush("#FF858B")),
        AgentState.Interrupted => ("Interrupted", subdued),
        AgentState.Ready => ("Ready", purple),
        AgentState.QuotaReached => ("Limit reached", Brush("#FF7E9B")),
        _ => ("Recent", muted)
    };
}
