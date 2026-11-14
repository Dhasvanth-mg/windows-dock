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
        if (compactAgents)
        {
            var close = IconButton(0xE711, "Close coding chats", Dismiss);
            DockPanel.SetDock(close, Dock.Right);
            toolbar.Children.Add(close);
        }
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
        if (compactAgents)
        {
            var widgets = WidgetLauncherButton();
            DockPanel.SetDock(widgets, Dock.Right);
            widgets.Margin = new Thickness(0, 0, 5, 0);
            toolbar.Children.Add(widgets);
        }
        var heading = Text(agentSetup ? "Connections" : "Coding chats", 20, strong: true);
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

        var activity = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        layout.Children.Add(activity);
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
        AgentApproval? detailApproval = null;
        AgentSession? detailSession = null;
        (AgentProvider Provider, string Id)? selectedRow = null;
        var listDrawn = false;
        (int Working, int Waiting, int Replies, int Limited)? lastActivity = null;
        updatePage = () =>
        {
            sentApprovals.RemoveWhere(id => !pendingApprovals.Any(request => request.Id == id));
            _ = RefreshAgentsAsync();
            var approval = pendingApprovals.Where(request => !sentApprovals.Contains(request.Id)).ToArray();
            var ordered = RankedSessions(approval);
            var focused = selectedAgent is { } key
                ? ordered.FirstOrDefault(session => session.Provider == key.Provider && session.Id == key.Id) ?? ordered.FirstOrDefault()
                : ordered.FirstOrDefault();
            var visible = ordered.Where(session => !agentFilter.HasValue || session.Provider == agentFilter).ToArray();
            notice.Text = agentMessage;
            notice.Visibility = string.IsNullOrEmpty(agentMessage) ? Visibility.Collapsed : Visibility.Visible;
            var now = DateTimeOffset.UtcNow;
            var counts = (Working: ordered.Count(session => session.State == AgentState.Working),
                Waiting: ordered.Count(session => session.State == AgentState.Attention),
                Replies: ordered.Count(session => session.State == AgentState.Finished && now - session.Updated < TimeSpan.FromMinutes(3)),
                Limited: ordered.Count(session => session.State == AgentState.QuotaReached));
            if (counts != lastActivity)
            {
                lastActivity = counts;
                activity.Children.Clear();
                AddActivity(counts.Waiting, "need approval", AgentState.Attention);
                AddActivity(counts.Working, "working", AgentState.Working);
                AddActivity(counts.Replies, counts.Replies == 1 ? "reply ready" : "replies ready", AgentState.Finished);
                AddActivity(counts.Limited, "at limit", AgentState.QuotaReached);
                activity.Visibility = activity.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            var focusedApproval = approval.FirstOrDefault(item => focused != null &&
                item.Provider == focused.Provider && item.SessionId == focused.Id);
            if (detail.Content == null || detailSession != focused || detailApproval != focusedApproval)
            {
                detailSession = focused;
                detailApproval = focusedApproval;
                detail.Content = focused == null
                    ? Card(Text("No local coding chats yet.", 13, subdued))
                    : AgentDetail(focused, focusedApproval);
            }
            var focus = focused == null ? ((AgentProvider, string)?)null : (focused.Provider, focused.Id);
            if (listDrawn == showAgentList && selectedRow == focus && shown != null && shown.SequenceEqual(visible))
                return;
            listDrawn = showAgentList;
            selectedRow = focus;
            shown = visible;
            cards.Children.Clear();
            if (!showAgentList)
                return;
            foreach (var session in visible)
            {
                var row = AgentRow(session, focus == (session.Provider, session.Id));
                row.Margin = new Thickness(0, 0, 0, 4);
                cards.Children.Add(row);
            }
            if (visible.Length == 0)
                cards.Children.Add(Text("No chats for this provider.", 12, subdued));
        };
        return layout;

        void AddActivity(int count, string label, AgentState state)
        {
            if (count == 0)
                return;
            var tint = AgentArt.Tint(state);
            var button = ActionButton($"{count} {label}", () =>
            {
                var match = RankedSessions(pendingApprovals.Where(request => !sentApprovals.Contains(request.Id)).ToArray())
                    .FirstOrDefault(session => session.State == state);
                if (match == null)
                    return;
                selectedAgent = (match.Provider, match.Id);
                showAgentList = true;
                agentFilter = null;
                RefreshAgentPage();
            });
            button.Background = AgentArt.Wash(tint, 26);
            button.BorderBrush = AgentArt.Wash(tint, 57);
            button.Foreground = tint;
            button.FontSize = 11;
            button.Padding = new Thickness(9, 4, 9, 4);
            button.Margin = new Thickness(0, 0, 5, 3);
            activity.Children.Add(button);
        }
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
        if (IsVisible && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            revealOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(13, 0,
                TimeSpan.FromMilliseconds(175)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private AgentSession[] RankedSessions(AgentApproval[] approvals) =>
        AgentSessions.Rank(agentSessions, approvals, codexPlan?.IsBlocked(DateTimeOffset.UtcNow) == true);

    private void PublishAgentGrid()
    {
        if (agentGrid == null || releaseWhenIdle)
            return;
        agentGrid.Publish(AgentSlots.Select(agentSlots, RankedSessions(pendingApprovals)));
    }

    private Button AgentRow(AgentSession session, bool selected)
    {
        var line = new DockPanel();
        var (label, color) = StateBadge(session.State);
        var state = Text(label, 10.5, color, true);
        state.Margin = new Thickness(8, 0, 0, 0);
        state.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(state, Dock.Right);
        line.Children.Add(state);
        var face = AgentAvatar(session.State, small: true);
        face.Margin = new Thickness(0, 0, 8, 0);
        line.Children.Add(face);
        var title = Text(session.Title, 12, strong: selected);
        title.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(title);
        var button = MakeButton(line, session.Title + ", " + label, () =>
        {
            selectedAgent = (session.Provider, session.Id);
            RefreshAgentPage();
        });
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(8, 4, 10, 4);
        button.Background = selected ? AgentArt.Wash(color, 32) : Brush("#0BFFFFFF");
        button.BorderBrush = selected ? AgentArt.Wash(color, 72) : Brush("#12C7B3F4");
        button.ToolTip = AgentTooltip(session);
        return button;
    }

    private Border AgentDetail(AgentSession session, AgentApproval? approval)
    {
        var panel = new StackPanel();
        var hero = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        var face = AgentAvatar(session.State);
        face.Margin = new Thickness(0, 0, 13, 0);
        hero.Children.Add(face);
        var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(session.Title, 17, strong: true);
        title.ToolTip = session.Title;
        description.Children.Add(title);
        var (label, color) = StateBadge(session.State);
        var status = Text(ProviderName(session.Provider) + "  ·  " + label, 11, color, true);
        status.Margin = new Thickness(0, 3, 0, 0);
        description.Children.Add(status);
        hero.Children.Add(description);
        panel.Children.Add(hero);
        var step = string.Equals(session.Step, label, StringComparison.OrdinalIgnoreCase) ? null : session.Step;
        var info = string.Join("  ·  ", new[] { session.Project, step, session.Model, session.Effort }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var details = Text(info, 11, subdued);
        details.ToolTip = AgentTooltip(session);
        if (info.Length > 0)
        {
            details.Margin = new Thickness(0, 3, 0, 0);
            panel.Children.Add(details);
        }

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
            preview.Visibility = Visibility.Collapsed;
            panel.Children.Add(preview);
            _ = LoadReplyPreviewAsync(session, preview);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 11, 0, 0) };
        AddAction(approval == null ? "Summarize reply" : "Last reply", 0xEAB7, () => ShowReplySummary(session),
            approval == null && session.State == AgentState.Finished);
        AddAction("Open chat", 0xE8A7, () => OpenAgent(session));
        if (session.State == AgentState.QuotaReached)
            AddAction("Token usage", 0xE9D9, ShowAgentUsage, true);
        AddAction("Assign slot", 0xE718, () => ShowAgentAssignments(session));
        panel.Children.Add(actions);
        var card = Card(panel);
        card.Padding = new Thickness(13);
        card.CornerRadius = new CornerRadius(16);
        card.BorderBrush = AgentArt.Wash(color, 53);
        var tint = color.Color;
        card.Background = new LinearGradientBrush(Color.FromRgb((byte)(25 + tint.R / 10),
            (byte)(21 + tint.G / 10), (byte)(36 + tint.B / 10)), Color.FromRgb(30, 26, 44), 55);
        return card;

        void AddAction(string text, int icon, Action action, bool primary = false)
        {
            var button = MakeButton(IconLabel(text, icon, primary ? color : subdued), text, action);
            button.Background = primary ? AgentArt.Wash(color, 40) : Brush("#13FFFFFF");
            button.BorderBrush = primary ? AgentArt.Wash(color, 76) : Brush("#17FFFFFF");
            button.Margin = new Thickness(0, 0, 5, 3);
            actions.Children.Add(button);
        }
    }

    private string AgentTooltip(AgentSession session) => string.Join("\n", new[]
    {
        session.Title, ProviderName(session.Provider) + " · " + StateBadge(session.State).Label,
        session.Step, session.Model, session.Effort, session.Updated.ToLocalTime().ToString("g")
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private Border AgentAvatar(AgentState state, bool small = false)
    {
        var (_, color) = StateBadge(state);
        var size = small ? 23 : 61;
        var badge = new Border
        {
            Width = size, Height = small ? 22 : 51, CornerRadius = new CornerRadius(small ? 6 : 13),
            Background = AgentArt.Wash(color, small ? (byte)27 : (byte)31),
            BorderBrush = AgentArt.Wash(color, small ? (byte)43 : (byte)75),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
        };
        if (SystemParameters.HighContrast)
        {
            badge.Background = SystemColors.WindowBrush;
            badge.BorderBrush = SystemColors.WindowTextBrush;
            var symbol = Text(state switch
            {
                AgentState.Attention => "!", AgentState.Finished => "✓", AgentState.Failed => "×",
                AgentState.QuotaReached => "–", AgentState.Interrupted => "Ⅱ", _ => "••"
            }, small ? 10 : 18, SystemColors.WindowTextBrush, true);
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            symbol.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = symbol;
            return badge;
        }
        var offset = new TranslateTransform();
        var face = new Image
        {
            Source = AgentArt.Face(state), Width = small ? 16 : 48, Height = small ? 12 : 36,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = offset, IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        badge.Child = face;
        if (!small)
        {
            badge.IsVisibleChanged += (_, _) =>
            {
                offset.BeginAnimation(TranslateTransform.XProperty, null);
                offset.BeginAnimation(TranslateTransform.YProperty, null);
                face.BeginAnimation(OpacityProperty, null);
                if (!badge.IsVisible || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
                    return;
                switch (state)
                {
                    case AgentState.Working: offset.BeginAnimation(TranslateTransform.YProperty, Motion(0, -3, 700, true)); break;
                    case AgentState.Attention: offset.BeginAnimation(TranslateTransform.XProperty, Motion(-2, 2, 475, true)); break;
                    case AgentState.Finished: offset.BeginAnimation(TranslateTransform.YProperty, Motion(-5, 0, 355, false)); break;
                    case AgentState.QuotaReached: face.BeginAnimation(OpacityProperty, Motion(0.69, 1, 1100, true)); break;
                }
            };
        }
        return badge;

        static DoubleAnimation Motion(double from, double to, int duration, bool repeat) => new(from, to, TimeSpan.FromMilliseconds(duration))
        {
            AutoReverse = repeat,
            RepeatBehavior = repeat ? RepeatBehavior.Forever : new RepeatBehavior(1),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
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
        var details = Field("Approval details", 0);
        details.IsReadOnly = true;
        details.Text = request.Details;
        details.TextWrapping = TextWrapping.Wrap;
        details.MaxHeight = 122;
        details.FontSize = 11;
        details.FontFamily = new FontFamily("Consolas");
        details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        details.Margin = new Thickness(0, 6, 0, 6);
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

    private static (string Label, SolidColorBrush Color) StateBadge(AgentState state) => (state switch
    {
        AgentState.Working => "Working",
        AgentState.Attention => "Needs approval",
        AgentState.Finished => "Reply ready",
        AgentState.Failed => "Failed",
        AgentState.Interrupted => "Interrupted",
        AgentState.Ready => "Ready",
        AgentState.QuotaReached => "Limit reached",
        _ => "Recent"
    }, AgentArt.Tint(state));
}
