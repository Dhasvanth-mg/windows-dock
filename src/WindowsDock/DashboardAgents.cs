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
    private int agentSlot = -1;
    private (AgentProvider Provider, string Id)? selectedAgent;
    private readonly HashSet<Guid> sentApprovals = [];
    private string agentMessage = "";

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
        var selection = agentGrid?.ReadSelection() ?? (-1, (AgentProvider?)null, "");
        agentSlot = selection.Item1;
        pendingApprovals = AgentApprovals.Read();
        var ranked = RankedSessions(pendingApprovals.Where(request => !sentApprovals.Contains(request.Id)).ToArray());
        var urgent = ranked.FirstOrDefault(session => session.State == AgentState.Attention);
        var clicked = ranked.FirstOrDefault(session => session.Provider == selection.Item2 && session.Id == selection.Item3);
        var finished = ranked.FirstOrDefault(session => session.State == AgentState.Finished &&
            DateTimeOffset.UtcNow - session.Updated < TimeSpan.FromMinutes(3));
        var focused = urgent ?? (clicked?.State == AgentState.Finished ? clicked : finished) ?? clicked;
        if (selection is (_, { } provider, { Length: > 0 } id) && focused == null)
            selectedAgent = (provider, id);
        var workspace = ShowCodingWorkspace(compact: true);
        if (focused != null)
            workspace.Select(focused);
        else
            workspace.Refresh();
        PositionAtTaskbar();
        RevealAgentPage();
    }

    private void RevealAgentPage()
    {
        if (IsVisible)
            SlideDockSurface(revealOffset, nextDockSlide, 13);
    }

    private AgentSession[] RankedSessions(AgentApproval[] approvals) =>
        AgentSessions.Rank(agentSessions, approvals, codexPlan?.IsBlocked(DateTimeOffset.UtcNow) == true);

    private void PublishAgentGrid()
    {
        if (agentGrid == null || releaseWhenIdle)
            return;
        agentGrid.Publish(AgentSlots.Select(agentSlots, RankedSessions(pendingApprovals)));
    }

    private async Task CopyLatestReplyAsync(AgentSession session, System.Threading.CancellationToken cancellation)
    {
        try
        {
            var reply = await ReadAgentReplyAsync(session, cancellation);
            if (cancellation.IsCancellationRequested)
                return;
            if (reply == null)
                agentMessage = "This chat has no readable completed reply.";
            else
            {
                Clipboard.SetText(reply.Text);
                agentMessage = "Latest reply copied.";
            }
        }
        catch (OperationCanceledException) { }
        catch (System.Runtime.InteropServices.ExternalException) { agentMessage = "The clipboard is busy."; }
        if (!closed && currentPage == DashboardPage.Agents)
            updatePage?.Invoke();
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

    private UIElement ApprovalDetails(AgentApproval request)
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
        return panel;

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

    private UIElement BuildAgentSettings(AgentProvider provider)
    {
        var card = new StackPanel();
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
        return card;

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
