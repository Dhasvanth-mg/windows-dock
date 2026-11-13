using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private AgentSession[] agentSessions = [];
    private bool readingAgents;
    private DateTime nextAgentRead;
    private AgentApproval[] pendingApprovals = [];
    private AgentProvider? agentFilter;
    private bool agentSetup;
    private readonly HashSet<Guid> sentApprovals = [];
    private string agentMessage = "";

    private UIElement AgentsPage()
    {
        var layout = new StackPanel();
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        var setup = ActionButton(agentSetup ? "Back to chats" : "Connections and defaults", () =>
        {
            agentSetup = !agentSetup;
            ShowPage(DashboardPage.Agents);
        });
        DockPanel.SetDock(setup, Dock.Right);
        toolbar.Children.Add(setup);
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, provider) in new (string, AgentProvider?)[]
                 { ("All chats", null), ("Claude Code", AgentProvider.Claude), ("Codex", AgentProvider.Codex) })
        {
            var filter = ActionButton(label, () =>
            {
                agentFilter = provider;
                agentSetup = false;
                ShowPage(DashboardPage.Agents);
            });
            filter.Margin = new Thickness(0, 0, 5, 0);
            filter.Background = agentFilter == provider && !agentSetup ? Brush("#38FFFFFF") : Brushes.Transparent;
            filters.Children.Add(filter);
        }
        toolbar.Children.Add(filters);
        layout.Children.Add(toolbar);

        if (agentSetup)
        {
            var settings = Columns(1, 1);
            Put(settings, AgentSettingsCard(AgentProvider.Claude), 0);
            Put(settings, AgentSettingsCard(AgentProvider.Codex), 1);
            layout.Children.Add(settings);
            return layout;
        }

        var notice = Text(agentMessage, 11, subdued);
        notice.Margin = new Thickness(3, 0, 0, 6);
        layout.Children.Add(notice);
        var cards = new StackPanel();
        layout.Children.Add(new ScrollViewer
        {
            Content = cards, MaxHeight = 310, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        AgentSession[]? shown = null;
        AgentApproval[]? shownApprovals = null;
        updatePage = () =>
        {
            sentApprovals.RemoveWhere(id => !pendingApprovals.Any(request => request.Id == id));
            _ = RefreshAgentsAsync();
            var approval = pendingApprovals.Where(request => !sentApprovals.Contains(request.Id) &&
                (!agentFilter.HasValue || request.Provider == agentFilter)).ToArray();
            var visible = agentSessions.Where(session => !agentFilter.HasValue || session.Provider == agentFilter).ToArray();
            notice.Text = agentMessage;
            notice.Visibility = string.IsNullOrEmpty(agentMessage) ? Visibility.Collapsed : Visibility.Visible;
            if (shown != null && shown.SequenceEqual(visible) && shownApprovals != null && shownApprovals.SequenceEqual(approval))
                return;
            shown = visible;
            shownApprovals = approval;
            cards.Children.Clear();
            foreach (var request in approval)
            {
                var review = ApprovalCard(request);
                review.Margin = new Thickness(0, 0, 0, 6);
                cards.Children.Add(review);
            }
            if (visible.Length == 0)
            {
                if (approval.Length == 0)
                    cards.Children.Add(Card(Text("No local coding chats found yet.", 13, subdued)));
                return;
            }
            for (var index = 0; index < visible.Length; index += 2)
            {
                var row = Columns(1, 1);
                row.Margin = new Thickness(0, 0, 0, 6);
                Put(row, AgentCard(visible[index]), 0);
                if (index + 1 < visible.Length)
                    Put(row, AgentCard(visible[index + 1]), 1);
                cards.Children.Add(row);
            }
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
            agentSessions = await Task.Run(() => AgentSessions.Read().ToArray(), lifetime.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            readingAgents = false;
            nextAgentRead = DateTime.UtcNow.AddSeconds(4);
        }
        if (!closed && currentPage == DashboardPage.Agents && IsVisible)
            updatePage?.Invoke();
    }

    private Border AgentCard(AgentSession session)
    {
        var panel = new StackPanel();
        var header = new DockPanel();
        var name = Text(ProviderName(session.Provider), 10,
            session.Provider == AgentProvider.Claude ? Brush("#F9A989") : accent, true);
        header.Children.Add(name);
        var (label, color) = StateBadge(session.State);
        var badge = Text(label, 10, color, true);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(badge);
        panel.Children.Add(header);
        var title = Text(session.Title, 15, strong: true);
        title.ToolTip = session.Title;
        title.Margin = new Thickness(0, 4, 0, 2);
        panel.Children.Add(title);

        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var action = ActionButton(session.State == AgentState.Attention ? "Open approval in app" : "Open app", () => OpenAgent(session));
        action.Padding = new Thickness(7, 4, 7, 4);
        DockPanel.SetDock(action, Dock.Right);
        footer.Children.Add(action);
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(Text(string.Join(" · ", new[] { session.Project, session.Step }.Where(s => !string.IsNullOrWhiteSpace(s))), 11, subdued));
        var settings = string.Join(" · ", new[] { session.Model, session.Effort }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (settings.Length > 0)
            details.Children.Add(Text(settings, 10, muted));
        footer.Children.Add(details);
        panel.Children.Add(footer);
        return Card(panel);
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
        AgentState.Finished => ("Turn ended", green),
        AgentState.Failed => ("Failed", Brush("#FF858B")),
        AgentState.Interrupted => ("Interrupted", subdued),
        AgentState.Ready => ("Ready", purple),
        _ => ("Recent", muted)
    };
}
