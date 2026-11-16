using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private CodingWorkspace? codingWorkspace;

    private UIElement AgentsPage()
    {
        codingWorkspace ??= new(this);
        updatePage = codingWorkspace.Refresh;
        return codingWorkspace.View;
    }

    private CodingWorkspace ShowCodingWorkspace(bool compact = false)
    {
        ParkToolPanel();
        SetDashboardSurface(true);
        if (compact && !compactAgents)
        {
            compactAgents = true;
            ConfigureWidth();
        }
        if (codingWorkspace == null || currentPage != DashboardPage.Agents || !pageInitialized)
            ShowPage(DashboardPage.Agents);
        UpdatePanelSwitchers();
        return codingWorkspace!;
    }

    private void ShowAgentUsage() => ShowCodingWorkspace().ShowUsage();

    private sealed class CodingWorkspace : IDisposable
    {
        private enum Pane { Reply, Approval, Slot, Usage, Setup, Activity, Jobs }

        private readonly DashboardWindow dock;
        private readonly ListBox conversations = new();
        private readonly ComboBox chatPicker = new() { DisplayMemberPath = nameof(AgentSession.Title), Visibility = Visibility.Collapsed };
        private readonly TextBox search;
        private static readonly AgentProvider?[] filterChoices = [null, AgentProvider.Codex, AgentProvider.Claude];
        private readonly Action<int> markFilter;
        private readonly FrameworkElement toolbar;
        private readonly Dictionary<Pane, Button> globalTabs = new();
        private readonly TextBlock empty;
        private readonly TextBlock title;
        private readonly TextBlock metadata;
        private readonly TextBlock notice;
        private readonly Button close;
        private Button? panelSwitcher;
        private readonly ContentControl portrait = new();
        private readonly Button openChat;
        private readonly Button back;
        private readonly StackPanel chatTabs = new() { Orientation = Orientation.Horizontal };
        private readonly Dictionary<Pane, Button> tabs = new();
        private readonly ScrollViewer reader = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.VerticalOnly };
        private readonly ColumnDefinition railColumn = new() { Width = new GridLength(226) };
        private readonly Border rail;
        private AgentSession[] visibleChats = [];
        private AgentProvider? filter;
        private AgentSession? chat;
        private AgentSession? selectedSnapshot;
        private AgentSession? summarySession;
        private AgentApproval? approval;
        private AgentState? portraitState;
        private Pane pane;
        private string? jobTitle;
        private bool hasBody;
        private bool selecting;
        private bool narrow;
        private bool showSummary;
        private CancellationTokenSource? paneLifetime;
        private CancellationTokenSource? summaryLifetime;
        private StackPanel? summary;

        internal FrameworkElement View { get; }

        internal CodingWorkspace(DashboardWindow dock)
        {
            this.dock = dock;
            close = dock.IconButton(0xE711, "Close coding chats", dock.Dismiss);
            var layout = new DockPanel();
            View = layout;
            toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);

            var workspace = new Grid();
            workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
            workspace.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            workspace.ColumnDefinitions.Add(railColumn);
            workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(workspace);

            search = dock.Field("Search chats", 100);
            search.Tag = "Search chats";
            search.Margin = new Thickness(0, 0, 0, 8);
            search.TextChanged += (_, _) => Refresh();
            var filterRow = dock.Segmented("Show chats from", ["All", "Codex", "Claude"], 0, index =>
            {
                filter = filterChoices[index];
                Refresh();
            }, out markFilter);
            filterRow.Margin = new Thickness(0, 0, 0, 8);

            conversations.Background = Brushes.Transparent;
            conversations.BorderThickness = new Thickness(0);
            conversations.Padding = new Thickness(0);
            conversations.ItemContainerStyle = (Style)dock.FindResource("CodingConversationRow");
            ScrollViewer.SetVerticalScrollBarVisibility(conversations, ScrollBarVisibility.Hidden);
            ScrollViewer.SetHorizontalScrollBarVisibility(conversations, ScrollBarVisibility.Disabled);
            ScrollViewer.SetPanningMode(conversations, PanningMode.VerticalOnly);
            AutomationProperties.SetName(conversations, "Coding conversations");
            conversations.SelectionChanged += (_, _) =>
            {
                if (!selecting && conversations.SelectedItem is ListBoxItem { Tag: AgentSession selected })
                    Select(selected);
            };
            empty = dock.Text("", 12, AssistantSecondary);
            empty.TextWrapping = TextWrapping.Wrap;
            var listHost = new Grid();
            listHost.Children.Add(conversations);
            listHost.Children.Add(empty);
            var sidebar = new DockPanel();
            DockPanel.SetDock(search, Dock.Top);
            DockPanel.SetDock(filterRow, Dock.Top);
            sidebar.Children.Add(search);
            sidebar.Children.Add(filterRow);
            sidebar.Children.Add(listHost);
            rail = new Border
            {
                Child = sidebar,
                BorderBrush = AssistantRule, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 0, 12, 0)
            };
            Grid.SetRow(rail, 1);
            workspace.Children.Add(rail);

            AutomationProperties.SetName(chatPicker, "Coding conversation");
            chatPicker.Margin = new Thickness(0, 0, 0, 7);
            chatPicker.SelectionChanged += (_, _) =>
            {
                if (!selecting && chatPicker.SelectedItem is AgentSession selected)
                    Select(selected);
            };
            Grid.SetColumnSpan(chatPicker, 2);
            workspace.Children.Add(chatPicker);
            workspace.SizeChanged += (_, e) =>
            {
                var small = e.NewSize.Width < 550;
                if (small == narrow)
                    return;
                narrow = small;
                railColumn.Width = new GridLength(small ? 0 : 226);
                rail.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
                chatPicker.Visibility = small ? Visibility.Visible : Visibility.Collapsed;
                Refresh();
            };

            title = dock.Text("", 18, AssistantInk, true);
            metadata = dock.Text("", 12, AssistantSecondary);
            metadata.Margin = new Thickness(0, 2, 0, 0);
            portrait.Margin = new Thickness(0, 0, 10, 0);
            portrait.VerticalAlignment = VerticalAlignment.Center;
            openChat = dock.ActionButton("Open chat", () => { if (chat != null) dock.OpenAgent(chat); });
            openChat.VerticalAlignment = VerticalAlignment.Center;
            back = dock.ActionButton("Back to chat", () => Back());
            back.VerticalAlignment = VerticalAlignment.Center;
            var header = new DockPanel { Margin = new Thickness(18, 0, 0, 8), LastChildFill = true };
            foreach (var button in new[] { openChat, back })
            {
                DockPanel.SetDock(button, Dock.Right);
                button.Margin = new Thickness(8, 0, 0, 0);
                header.Children.Add(button);
            }
            header.Children.Add(portrait);
            header.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, metadata } });

            foreach (var (target, label) in new[] { (Pane.Reply, "Reply"), (Pane.Approval, "Approval"), (Pane.Slot, "Agent slot"), (Pane.Usage, "Usage") })
            {
                var button = Tab(label, () => Navigate(target));
                tabs.Add(target, button);
                chatTabs.Children.Add(button);
            }
            chatTabs.Margin = new Thickness(10, 0, 0, 0);
            // Things that are about the chat sit on the left; Jobs, Activity and Setup belong to no one chat, so they
            // sit apart on the right of the same row.
            var everywhere = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, target) in new[] { ("Jobs", Pane.Jobs), ("Activity", Pane.Activity), ("Setup", Pane.Setup) })
            {
                var button = Tab(label, () => Navigate(target));
                globalTabs.Add(target, button);
                everywhere.Children.Add(button);
            }
            DockPanel.SetDock(everywhere, Dock.Right);
            var tabRow = new DockPanel { Children = { everywhere, chatTabs } };
            var tabsRule = new Border { Child = tabRow, BorderBrush = AssistantRule, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 3) };
            notice = dock.Text("", 11, AssistantSecondary);
            notice.TextWrapping = TextWrapping.Wrap;
            notice.Margin = new Thickness(18, 4, 0, 0);
            var work = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(tabsRule, Dock.Top);
            DockPanel.SetDock(notice, Dock.Bottom);
            work.Children.Add(header);
            work.Children.Add(tabsRule);
            work.Children.Add(notice);
            work.Children.Add(reader);
            Grid.SetRow(work, 1);
            Grid.SetColumn(work, 1);
            workspace.Children.Add(work);
        }

        private FrameworkElement BuildToolbar()
        {
            // Only the taskbar popup needs its own title; inside Dashboard the tab strip already says where you are.
            var bar = new DockPanel { Margin = new Thickness(0, 4, 0, 12) };
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
            var lockButton = dock.LockPanelButton("Coding chats", () => dock.codingPanelLocked, value => dock.codingPanelLocked = value);
            DockPanel.SetDock(lockButton, Dock.Right);
            bar.Children.Add(lockButton);
            panelSwitcher = dock.MakePanelSwitcher();
            DockPanel.SetDock(panelSwitcher, Dock.Right);
            bar.Children.Add(panelSwitcher);
            var heading = dock.Text("Coding chats", 17, AssistantInk, true);
            heading.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(heading);
            return bar;
        }

        internal void ShowLockedPanelSwitcher(bool show)
        {
            if (panelSwitcher != null)
                panelSwitcher.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void Refresh()
        {
            dock.sentApprovals.RemoveWhere(id => !dock.pendingApprovals.Any(item => item.Id == id));
            var requests = dock.pendingApprovals.Where(item => !dock.sentApprovals.Contains(item.Id)).ToArray();
            var ranked = dock.RankedSessions(requests);
            var query = narrow ? "" : search.Text.Trim();
            var filtered = ranked.Where(item => (narrow || filter == null || item.Provider == filter) &&
                (item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 item.Project.Contains(query, StringComparison.OrdinalIgnoreCase))).Take(40).ToArray();
            var key = dock.selectedAgent;
            var selected = key == null ? filtered.FirstOrDefault() : ranked.FirstOrDefault(item => Identity(item) == key)
                ?? (selectedSnapshot != null && Identity(selectedSnapshot) == key ? selectedSnapshot : filtered.FirstOrDefault());
            dock.selectedAgent ??= selected == null ? null : Identity(selected);
            var request = selected == null ? null : requests.FirstOrDefault(item => item.Provider == selected.Provider && item.SessionId == selected.Id);
            var changedChat = Identity(chat) != Identity(selected);
            var changedReply = chat != selected;
            var changedApproval = approval != request;
            if (changedChat)
                ClearSummary();
            chat = selected;
            approval = request;
            SyncRail(filtered, ranked.Length > 0);
            if (!hasBody || changedChat && pane is Pane.Reply or Pane.Approval or Pane.Slot)
                Navigate(approval == null ? Pane.Reply : Pane.Approval);
            else if (pane == Pane.Reply && !showSummary && changedReply || pane == Pane.Approval && changedApproval ||
                     pane == Pane.Usage && changedChat)
                Navigate(pane);
            else
                UpdateHeader();
            _ = dock.RefreshAgentsAsync();
        }

        internal void Select(AgentSession selected)
        {
            selectedSnapshot = selected;
            dock.selectedAgent = Identity(selected);
            dock.agentMessage = "";
            if (GlobalPane || Identity(chat) != Identity(selected))
                hasBody = false;
            Refresh();
        }

        private static (AgentProvider, string)? Identity(AgentSession? item) => item == null ? null : (item.Provider, item.Id);

        private void SyncRail(AgentSession[] matches, bool hasChats)
        {
            selecting = true;
            if (!visibleChats.SequenceEqual(matches))
            {
                var hadFocus = conversations.IsKeyboardFocusWithin;
                conversations.Items.Clear();
                foreach (var item in matches)
                    conversations.Items.Add(ChatRow(item));
                chatPicker.ItemsSource = matches;
                visibleChats = matches;
                if (hadFocus)
                    conversations.Items.OfType<ListBoxItem>().FirstOrDefault(item => Identity((AgentSession)item.Tag) == Identity(chat))?.Focus();
            }
            var index = Array.FindIndex(matches, item => Identity(item) == Identity(chat));
            conversations.SelectedIndex = chatPicker.SelectedIndex = index;
            empty.Text = hasChats ? "No matching chats." : "No local coding chats yet.";
            empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            markFilter(Array.IndexOf(filterChoices, filter));
            selecting = false;
        }

        private ListBoxItem ChatRow(AgentSession session)
        {
            var (state, color) = StateBadge(session.State);
            var face = dock.AgentAvatar(session.State, small: true);
            face.Margin = new Thickness(0, 0, 10, 0);
            var line = new DockPanel { Children = { face } };
            var heading = dock.Text(session.Title, 13);
            heading.Margin = new Thickness(0, 0, 0, 1);
            // The state carries the colour; the provider is quiet context after it.
            var subline = dock.Text("", 11, AssistantFaint);
            subline.Inlines.Add(new Run(state) { Foreground = SystemParameters.HighContrast ? AssistantSecondary : color });
            subline.Inlines.Add(new Run("   " + ProviderName(session.Provider)));
            line.Children.Add(new StackPanel { Children = { heading, subline } });
            var item = new ListBoxItem { Content = line, Tag = session, ToolTip = dock.AgentTooltip(session) };
            AutomationProperties.SetName(item, $"{session.Title}, {state}");
            return item;
        }

        private bool GlobalPane => pane is Pane.Setup or Pane.Activity or Pane.Jobs;

        private void UpdateHeader()
        {
            toolbar.Visibility = dock.compactAgents ? Visibility.Visible : Visibility.Collapsed;
            title.Text = pane switch
            {
                Pane.Setup => "Setup", Pane.Activity => "Activity", Pane.Jobs => jobTitle ?? "Jobs",
                _ => chat?.Title ?? "Pick a chat"
            };
            title.ToolTip = title.Text;
            metadata.Inlines.Clear();
            if (!GlobalPane && chat != null)
            {
                var (state, color) = StateBadge(chat.State);
                metadata.Inlines.Add(new Run(state) { Foreground = SystemParameters.HighContrast ? AssistantSecondary : color });
                metadata.Inlines.Add(new Run("   " + ProviderName(chat.Provider) +
                    (string.IsNullOrWhiteSpace(chat.Project) ? "" : " in " + chat.Project)));
                var model = string.Join(" ", new[] { chat.Model, chat.Effort }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (model.Length > 0)
                    metadata.Inlines.Add(new Run("   " + model) { Foreground = AssistantFaint });
            }
            metadata.Visibility = metadata.Inlines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var (target, button) in globalTabs)
                SelectTab(button, target == pane);
            portrait.Visibility = !GlobalPane && chat != null ? Visibility.Visible : Visibility.Collapsed;
            if (chat != null && portraitState != chat.State)
            {
                portraitState = chat.State;
                var avatar = dock.AgentAvatar(chat.State);
                avatar.Width = 42;
                avatar.Height = 38;
                avatar.CornerRadius = new CornerRadius(9);
                if (avatar.Child is Image face) { face.Width = 32; face.Height = 24; }
                portrait.Content = avatar;
            }
            openChat.Visibility = !GlobalPane && chat != null ? Visibility.Visible : Visibility.Collapsed;
            back.Content = jobTitle == null ? "Back to chat" : "Back to jobs";
            AutomationProperties.SetName(back, (string)back.Content);
            back.Visibility = GlobalPane ? Visibility.Visible : Visibility.Collapsed;
            chatTabs.Visibility = GlobalPane ? Visibility.Collapsed : Visibility.Visible;
            tabs[Pane.Approval].Visibility = approval == null && pane != Pane.Approval ? Visibility.Collapsed : Visibility.Visible;
            tabs[Pane.Slot].IsEnabled = chat != null;
            foreach (var (target, button) in tabs)
                SelectTab(button, target == pane);
            notice.Text = dock.agentMessage;
            notice.Visibility = notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private Button Tab(string label, Action command)
        {
            var button = dock.ActionButton(label, command);
            button.Style = (Style)dock.FindResource("AssistantPickerButton");
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Padding = new Thickness(8, 4, 8, 6);
            ToolTipService.SetIsEnabled(button, false);
            return button;
        }

        private static void SelectTab(Button button, bool selected)
        {
            button.Foreground = selected ? AssistantInk : AssistantSecondary;
            button.Tag = selected ? "Selected" : null;
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        }

        internal void ShowUsage() => Navigate(Pane.Usage);

        internal bool Back()
        {
            if (jobTitle != null)
                Navigate(Pane.Jobs);
            else if (pane != Pane.Reply)
                Navigate(Pane.Reply);
            else if (showSummary)
            {
                showSummary = false;
                Navigate(Pane.Reply);
            }
            else
                return false;
            return true;
        }

        private StackPanel Mount(Pane target, string? title = null)
        {
            Release(ref paneLifetime);
            paneLifetime = CancellationTokenSource.CreateLinkedTokenSource(dock.lifetime.Token);
            pane = target;
            jobTitle = title;
            hasBody = true;
            var content = new StackPanel { Margin = new Thickness(18, 14, 2, 8) };
            reader.Content = content;
            reader.ScrollToTop();
            UpdateHeader();
            return content;
        }

        private void Navigate(Pane target)
        {
            var content = Mount(target);
            var token = paneLifetime!.Token;
            switch (target)
            {
                case Pane.Reply: BuildReply(content, token); break;
                case Pane.Approval:
                    if (approval != null)
                        content.Children.Add(dock.ApprovalDetails(approval));
                    else
                        content.Children.Add(dock.Text("No pending approval for this chat.", 13, AssistantSecondary));
                    break;
                case Pane.Slot:
                    if (chat != null)
                        dock.BuildAgentAssignments(content, chat);
                    break;
                case Pane.Usage: dock.BuildAgentUsage(content, chat, token); break;
                case Pane.Setup: BuildSetup(content); break;
                case Pane.Activity: dock.BuildActivity(content, token, OpenActivityChat); break;
                case Pane.Jobs: dock.BuildJobsPanel(content, token, ShowNewJob, ShowJob); break;
            }
        }

        private void BuildReply(StackPanel content, CancellationToken token)
        {
            if (chat is not { } selected)
            {
                content.Children.Add(dock.Text("Replies and chat controls appear here.", 13, AssistantSecondary));
                return;
            }
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var toggle = dock.ActionButton(showSummary ? "Original reply" : "Summarize reply", () =>
            {
                if (!showSummary && summarySession is { } previous && chat is { } current &&
                    (previous.LastReplyId != current.LastReplyId || previous.LastReplySessionId != current.LastReplySessionId ||
                     current.LastReplyId == null && current.State == AgentState.Finished && current.Updated > previous.Updated))
                    ClearSummary();
                showSummary = !showSummary;
                Navigate(Pane.Reply);
            });
            if (!showSummary)
                dock.MarkPrimary(toggle, true);
            DockPanel.SetDock(toggle, Dock.Right);
            row.Children.Add(toggle);
            if (!showSummary)
            {
                var copy = dock.IconButton(0xE8C8, "Copy latest reply", () => _ = dock.CopyLatestReplyAsync(selected, token));
                DockPanel.SetDock(copy, Dock.Right);
                row.Children.Add(copy);
            }
            var heading = dock.Text(showSummary ? "Summary" : "Latest reply", 12, AssistantSecondary);
            heading.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(heading);
            content.Children.Add(row);
            if (showSummary)
            {
                if (summary == null)
                {
                    summaryLifetime = CancellationTokenSource.CreateLinkedTokenSource(dock.lifetime.Token);
                    summarySession = selected;
                    summary = new StackPanel();
                    content.Children.Add(summary);
                    dock.BuildTextSummary(summary, "Reply to summarize", null, selected, summaryLifetime.Token)();
                }
                else
                {
                    if (summary.Parent is Panel previous)
                        previous.Children.Remove(summary);
                    content.Children.Add(summary);
                }
                return;
            }
            var transcript = dock.Field("Latest completed reply", 0);
            transcript.IsReadOnly = true;
            transcript.TextWrapping = TextWrapping.Wrap;
            transcript.Background = Brushes.Transparent;
            transcript.BorderBrush = Brushes.Transparent;
            transcript.Padding = new Thickness(0);
            transcript.FontSize = 13;
            transcript.Text = "Reading the latest reply…";
            content.Children.Add(transcript);
            _ = ReadAsync();

            async Task ReadAsync()
            {
                try
                {
                    var reply = await dock.ReadAgentReplyAsync(selected, token);
                    if (!token.IsCancellationRequested)
                        transcript.Text = reply?.Text ?? "No completed reply is available for this chat.";
                }
                catch (OperationCanceledException) { }
            }
        }

        private void BuildSetup(StackPanel content)
        {
            var providers = new[] { AgentProvider.Codex, AgentProvider.Claude };
            var choices = new ComboBox { ItemsSource = providers.Select(ProviderName).ToArray(), MinWidth = 132,
                HorizontalAlignment = HorizontalAlignment.Left, SelectedIndex = chat?.Provider == AgentProvider.Claude ? 1 : 0,
                Margin = new Thickness(0, 0, 0, 9) };
            AutomationProperties.SetName(choices, "Coding provider");
            var settings = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(choices);
            content.Children.Add(settings);
            choices.SelectionChanged += (_, _) => Load();
            Load();

            void Load()
            {
                if (choices.SelectedIndex >= 0)
                    settings.Content = dock.BuildAgentSettings(providers[choices.SelectedIndex]);
            }
        }

        private void OpenActivityChat(InboxItem item)
        {
            if (item.ChatId.Length == 0)
            {
                ShowUsage();
                return;
            }
            var match = dock.agentSessions.FirstOrDefault(candidate => candidate.Provider == item.Provider && candidate.Id == item.ChatId);
            Select(match ?? new(item.Provider, item.ChatId, item.Title, item.Project, item.State, "", item.At, item.DesktopId));
        }

        private void ShowNewJob() => dock.BuildCodingJobForm(dock, Mount(Pane.Jobs, "New coding job"), () => Navigate(Pane.Jobs));

        private void ShowJob(Guid id)
        {
            var job = DockJobs.Load().FirstOrDefault(item => item.Id == id);
            if (job != null)
                dock.BuildJobDetails(dock, Mount(Pane.Jobs, $"{job.Specialist} job"), job, paneLifetime!.Token, () => ShowJob(id));
        }

        private void ClearSummary()
        {
            Release(ref summaryLifetime);
            summary = null;
            summarySession = null;
            showSummary = false;
        }

        internal void Suspend()
        {
            Release(ref paneLifetime);
            ClearSummary();
            reader.Content = null;
            hasBody = false;
        }

        public void Dispose() => Suspend();

        private static void Release(ref CancellationTokenSource? work)
        {
            work?.Cancel();
            work?.Dispose();
            work = null;
        }
    }
}
