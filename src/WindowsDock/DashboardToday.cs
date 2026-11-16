using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace WindowsDock;

// The first page is the whole day on one sheet: the calendar and reminders, what is playing, the timer, the
// weather and the coding chats. Nothing here needs a second page to be useful.
public partial class DashboardWindow
{
    private Meeting[] homeMeetings = [];
    private DateTimeOffset homeMeetingsRead;
    private bool readingHomeMeetings;

    // Pages sit in a scroller, so anything that should reach the bottom edge needs the viewport height.
    private T FillPage<T>(T page) where T : FrameworkElement
    {
        page.SetBinding(MinHeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = PageScroll });
        return page;
    }

    // Side-by-side panes with hairlines between them. On a narrow window they stack and the page scrolls instead.
    private Grid Panes(params (double Weight, FrameworkElement Content)[] panes)
    {
        var grid = new Grid();
        var wide = WideDashboard;
        for (var index = 0; index < panes.Length; index++)
        {
            var (weight, content) = panes[index];
            if (index > 0)
            {
                FrameworkElement line = wide ? Divider(panes.Length > 2 ? 16 : 20) : Rule(16, 16);
                Add(line, GridLength.Auto);
            }
            Add(content, wide ? new GridLength(weight, GridUnitType.Star) : GridLength.Auto);
        }
        return grid;

        void Add(FrameworkElement element, GridLength size)
        {
            if (wide)
            {
                Grid.SetColumn(element, grid.ColumnDefinitions.Count);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = size });
            }
            else
            {
                Grid.SetRow(element, grid.RowDefinitions.Count);
                grid.RowDefinitions.Add(new RowDefinition { Height = size });
            }
            grid.Children.Add(element);
        }
    }

    private Grid SplitPage(double left, double right, out StackPanel first, out StackPanel second)
    {
        first = new StackPanel();
        second = new StackPanel();
        return Panes((left, first), (right, second));
    }

    private UIElement HomePage()
    {
        var day = DayPane(out var updateDay);
        var now = NowPane(out var updateNow);
        var chats = ChatsPane(out var updateChats);
        updatePage = () =>
        {
            updateDay();
            updateNow();
            updateChats();
        };
        return FillPage(Panes((1.12, day), (1, now), (1, chats)));
    }

    // Left: the date, a week you can step through, and reminders with meetings mixed in.
    private FrameworkElement DayPane(out Action update)
    {
        var pane = new DockPanel { LastChildFill = true };
        var masthead = new DockPanel { Margin = new Thickness(0, -2, 0, 8) };
        var later = IconButton(0xE76C, "Next week");
        var earlier = IconButton(0xE76B, "Previous week");
        later.VerticalAlignment = earlier.VerticalAlignment = VerticalAlignment.Bottom;
        later.Margin = new Thickness(0, 0, -6, 0);
        DockPanel.SetDock(later, Dock.Right);
        DockPanel.SetDock(earlier, Dock.Right);
        masthead.Children.Add(later);
        masthead.Children.Add(earlier);
        var weekday = Text("", 13, AssistantSecondary);
        var date = Text("", 24, strong: true);
        date.Margin = new Thickness(0, -3, 0, 0);
        var backToToday = PlainButton(new StackPanel { Children = { weekday, date } }, "Back to today", () => { });
        backToToday.HorizontalAlignment = HorizontalAlignment.Left;
        masthead.Children.Add(backToToday);
        DockPanel.SetDock(masthead, Dock.Top);
        pane.Children.Add(masthead);

        var week = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(-2, 0, -2, 10) };
        DockPanel.SetDock(week, Dock.Top);
        pane.Children.Add(week);

        var compose = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var input = Field("New reminder", 160);
        var add = IconButton(0xE710, "Add reminder");
        add.IsEnabled = false;
        add.Margin = new Thickness(4, 0, -6, 0);
        DockPanel.SetDock(add, Dock.Right);
        compose.Children.Add(add);
        compose.Children.Add(input);
        DockPanel.SetDock(compose, Dock.Top);
        pane.Children.Add(compose);

        // The sticky takes the space under the list, so the column is never just empty.
        var sticky = TodaySticky();
        sticky.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(sticky, Dock.Bottom);
        pane.Children.Add(sticky);

        var agenda = new StackPanel();
        pane.Children.Add(new ScrollViewer { Content = agenda, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 96,
            VerticalAlignment = VerticalAlignment.Top });

        input.TextChanged += (_, _) => add.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && add.IsEnabled)
            {
                e.Handled = true;
                Add();
            }
        };
        add.Click += (_, _) => Add();
        later.Click += (_, _) => ShowWeek(calendarWeek.AddDays(7));
        earlier.Click += (_, _) => ShowWeek(calendarWeek.AddDays(-7));
        backToToday.Click += (_, _) =>
        {
            reminderDate = DateTime.Today;
            ShowWeek(DateTime.Today);
        };

        var shownDay = DateTime.MinValue;
        update = () =>
        {
            if (shownDay == DateTime.Today)
                return;
            // Midnight passed (or this is the first paint): if the old "today" was selected, follow it forward.
            if (reminderDate == shownDay || shownDay == DateTime.MinValue && reminderDate < DateTime.Today)
                reminderDate = calendarWeek = DateTime.Today;
            shownDay = DateTime.Today;
            RenderWeek();
            RenderAgenda();
        };
        _ = LoadHomeMeetingsAsync(RenderAgenda);
        return pane;

        void ShowWeek(DateTime target)
        {
            calendarWeek = target;
            RenderWeek();
        }

        void Add()
        {
            try
            {
                DockReminders.Add(new(input.Text.Trim(), reminderDate));
                input.Clear();
                RenderWeek();
                RenderAgenda();
            }
            catch (Exception error) when (DockActions.IsActionError(error)) { ShowOwnedMessage(error.Message); }
        }

        List<Reminder> Reminders()
        {
            try { return DockReminders.Load(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
        }

        void RenderWeek()
        {
            var today = DateTime.Today;
            var reminders = Reminders();
            var first = calendarWeek.AddDays(-(((int)calendarWeek.DayOfWeek + 6) % 7));
            var thisWeek = first <= today && today < first.AddDays(7);
            weekday.Text = thisWeek ? today.ToString("dddd") : "Week of";
            date.Text = thisWeek ? today.ToString("MMMM d") : first.ToString("MMMM d");
            AutomationProperties.SetName(backToToday, thisWeek ? today.ToString("D") : "Back to today");
            input.Tag = reminderDate == today ? "Remind me today to…" : "On " + reminderDate.ToString("ddd, MMM d") + ", remind me to…";
            week.Children.Clear();
            for (var i = 0; i < 7; i++)
            {
                var entry = first.AddDays(i);
                var cell = new StackPanel();
                var name = Text(entry.ToString("ddd"), 11, AssistantSecondary);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Children.Add(name);
                var number = Text(entry.Day.ToString(), 15, entry == today ? AccentFill : AssistantInk, entry == today);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Children.Add(number);
                // A small mark under days that already have something on them.
                cell.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 4, Height = 4, Margin = new Thickness(0, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                    Fill = AssistantSecondary, Opacity = reminders.Any(item => item.Date == entry) ? 1 : 0
                });
                var button = new Button
                {
                    Content = cell, Style = (Style)FindResource("Segment"), Padding = new Thickness(0, 4, 0, 5),
                    Margin = new Thickness(1, 0, 1, 0), Tag = entry == reminderDate ? "Selected" : null
                };
                AutomationProperties.SetName(button, entry.ToString("D") + (entry == today ? ", today" : ""));
                button.Click += (_, _) =>
                {
                    reminderDate = entry;
                    RenderWeek();
                    input.Focus();
                };
                week.Children.Add(button);
            }
        }

        void RenderAgenda()
        {
            agenda.Children.Clear();
            var today = DateTime.Today;
            var items = new List<(DateTime At, FrameworkElement Row)>();
            foreach (var item in Reminders())
            {
                var late = item.Date < today;
                var when = late ? "Overdue" : item.Date == today ? "Today" : item.Date == today.AddDays(1) ? "Tomorrow"
                    : item.Date < today.AddDays(7) ? item.Date.ToString("ddd") : item.Date.ToString("MMM d");
                var done = IconButton(0xE73E, "Mark done: " + item.Title, () =>
                {
                    try
                    {
                        DockReminders.Change(item, null);
                        RenderWeek();
                        RenderAgenda();
                    }
                    catch (Exception error) when (DockActions.IsActionError(error)) { ShowOwnedMessage(error.Message); }
                });
                items.Add((item.Date, AgendaRow(when, item.Title, done, late)));
            }
            foreach (var meeting in homeMeetings)
            {
                var start = meeting.Start.LocalDateTime;
                var when = start.Date == today ? start.ToString("t") : start.ToString("ddd ") + start.ToString("t");
                Button? join = null;
                if (meeting.Link is { } link)
                {
                    join = ActionButton("Join", () => OpenTarget(link));
                    join.Padding = new Thickness(9, 3, 9, 3);
                }
                items.Add((start, AgendaRow(when, meeting.Title, join, false)));
            }
            foreach (var (_, row) in items.OrderBy(item => item.At).Take(40))
                agenda.Children.Add(row);
            if (items.Count == 0)
            {
                var clear = Text("Nothing on the list. Suspicious.", 12, AssistantFaint);
                clear.Margin = new Thickness(0, 6, 0, 0);
                agenda.Children.Add(clear);
            }
        }
    }

    // Middle: what is playing, the focus timer and the weather.
    private FrameworkElement NowPane(out Action update)
    {
        var pane = new StackPanel();
        pane.Children.Add(NowPlaying(out var updateMedia, 48, compact: true));
        var volume = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var speaker = DockGlyph(0xE767, 13, AssistantSecondary);
        speaker.Margin = new Thickness(1, 0, 10, 0);
        volume.Children.Add(speaker);
        volume.Children.Add(OutputSlider());
        pane.Children.Add(volume);
        pane.Children.Add(Rule(10, 10));
        pane.Children.Add(FocusStrip(out var updateFocus));
        pane.Children.Add(Rule(12, 8));
        pane.Children.Add(WeatherGlance());
        update = () =>
        {
            updateMedia();
            updateFocus();
        };
        return pane;
    }

    // Right: the coding chats that matter most right now, then shortcuts and how the machine is doing.
    private FrameworkElement ChatsPane(out Action update)
    {
        var pane = new DockPanel { LastChildFill = true };
        var vitals = Text("", 11, AssistantSecondary);
        Typography.SetNumeralAlignment(vitals, FontNumeralAlignment.Tabular);
        var vitalsButton = PlainButton(vitals, "Open system details", () => ShowPage(DashboardPage.Desk));
        vitalsButton.HorizontalAlignment = HorizontalAlignment.Left;
        vitalsButton.Margin = new Thickness(-8, 4, -8, -5);
        DockPanel.SetDock(vitalsButton, Dock.Bottom);
        pane.Children.Add(vitalsButton);

        homeFavorites = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        RenderHomeFavorites();
        DockPanel.SetDock(homeFavorites, Dock.Bottom);
        pane.Children.Add(homeFavorites);

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var all = QuietButton("Open chats", () => ShowPage(DashboardPage.Agents));
        DockPanel.SetDock(all, Dock.Right);
        heading.Children.Add(all);
        var title = SectionTitle("Coding chats");
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(0);
        heading.Children.Add(title);
        DockPanel.SetDock(heading, Dock.Top);
        pane.Children.Add(heading);
        var chats = new StackPanel { Margin = new Thickness(-8, 0, -8, 0) };
        pane.Children.Add(chats);

        var shownChats = "";
        var shownVitals = "";
        var ticks = 0;
        AgentSession[]? rankedFrom = null;
        var rankedApprovals = "";
        update = () =>
        {
            // The readings change on nearly every sample, and each change is a repaint, so refresh them a few times a
            // minute rather than on every tick. Approvals and chats below are still checked every tick.
            if (ticks % 3 == 0)
            {
                var sample = ReadMetrics(full: false);
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                var hasBattery = !power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
                var plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                var reading = $"{sample.Cpu:0}|{sample.Memory:0}|{(hasBattery ? Math.Clamp(power.BatteryLifePercent, 0, 1) : -1):0.00}|{plugged}";
                if (reading != shownVitals)
                {
                    shownVitals = reading;
                    vitals.Inlines.Clear();
                    AddVital("CPU", $"{sample.Cpu:0}%");
                    AddVital("Memory", $"{sample.Memory:0}%");
                    if (hasBattery)
                        AddVital(plugged ? "Charging" : "Battery", $"{Math.Clamp(power.BatteryLifePercent, 0, 1):P0}");
                }
            }

            // The native controller already keeps chats fresh; standalone, look now and then.
            if (ticks++ % 5 == 0 && agentGrid == null)
                _ = RefreshAgentsAsync();
            var waiting = pendingApprovals.Where(item => !sentApprovals.Contains(item.Id)).ToArray();
            // Ranking sorts every known chat, so only redo it when the chats or the approvals actually changed.
            var approvalKey = waiting.Length == 0 ? "" : string.Join("|", waiting.Select(item => item.Id));
            if (ReferenceEquals(rankedFrom, agentSessions) && approvalKey == rankedApprovals && chats.Children.Count > 0)
                return;
            rankedFrom = agentSessions;
            rankedApprovals = approvalKey;
            var ranked = RankedSessions(waiting).Take(4).ToArray();
            var signature = string.Join("|", ranked.Select(item => $"{item.Provider}{item.Id}{item.State}{item.Title}{item.Step}")) +
                string.Join("|", waiting.Select(item => item.Id));
            if (signature == shownChats && chats.Children.Count > 0)
                return;
            shownChats = signature;
            chats.Children.Clear();
            foreach (var chat in ranked)
                chats.Children.Add(HomeChatRow(chat, waiting.FirstOrDefault(item => item.Provider == chat.Provider && item.SessionId == chat.Id)));
            if (ranked.Length == 0)
            {
                var none = Text("All quiet. Chats from Codex and Claude Code turn up here.", 12, AssistantFaint);
                none.TextWrapping = TextWrapping.Wrap;
                none.Margin = new Thickness(8, 6, 8, 0);
                chats.Children.Add(none);
            }
        };
        return pane;

        void AddVital(string label, string figure)
        {
            if (vitals.Inlines.Count > 0)
                vitals.Inlines.Add(new Run("    "));
            vitals.Inlines.Add(new Run(label + " "));
            vitals.Inlines.Add(new Run(figure) { Foreground = AssistantInk });
        }
    }

    private FrameworkElement AgendaRow(string when, string title, FrameworkElement? action, bool late)
    {
        var row = new DockPanel { MinHeight = 30 };
        if (action != null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(action, Dock.Right);
            row.Children.Add(action);
        }
        var time = Text(when, 11, late ? amber : AssistantSecondary);
        time.Width = 66;
        time.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(time);
        var label = Text(title, 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ToolTip = title;
        row.Children.Add(label);
        return new Border { Child = row, BorderBrush = AssistantRule, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // One chat on Today. If it is waiting on a permission, the answer buttons are right here: no need to open it.
    private Button HomeChatRow(AgentSession chat, AgentApproval? approval)
    {
        var (state, color) = StateBadge(chat.State);
        var line = new DockPanel();
        var face = AgentAvatar(chat.State, small: true);
        face.Margin = new Thickness(0, 0, 10, 0);
        face.VerticalAlignment = VerticalAlignment.Top;
        line.Children.Add(face);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(chat.Title, 13));
        var detail = Text("", 11, AssistantFaint);
        var tone = SystemParameters.HighContrast ? AssistantSecondary : color;
        if (approval != null)
            detail.Inlines.Add(new Run("Wants to use " + approval.Tool) { Foreground = tone });
        else
        {
            detail.Inlines.Add(new Run(state) { Foreground = tone });
            // While it is working, say what it is doing; otherwise say where it lives.
            var context = chat.State == AgentState.Working && !string.IsNullOrWhiteSpace(chat.Step) ? chat.Step
                : string.IsNullOrWhiteSpace(chat.Project) ? ProviderName(chat.Provider) : chat.Project;
            detail.Inlines.Add(new Run("   " + context));
        }
        labels.Children.Add(detail);
        if (approval != null)
        {
            var answers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 1) };
            var allow = PrimaryButton("Allow once");
            var deny = ActionButton("Deny");
            foreach (var button in new[] { allow, deny })
            {
                button.Padding = new Thickness(10, 3, 10, 3);
                button.Margin = new Thickness(0, 0, 6, 0);
                answers.Children.Add(button);
            }
            allow.Click += (_, e) => { e.Handled = true; _ = Answer(true); };
            deny.Click += (_, e) => { e.Handled = true; _ = Answer(false); };
            allow.ToolTip = deny.ToolTip = approval.Details.Length > 400 ? approval.Details[..400] + "…" : approval.Details;
            labels.Children.Add(answers);

            async Task Answer(bool allowed)
            {
                answers.IsEnabled = false;
                var delivered = await AgentApprovals.DecideAsync(approval, allowed);
                if (delivered)
                    sentApprovals.Add(approval.Id);
                else
                    agentMessage = "The request is no longer reachable here. Check the provider’s prompt.";
                if (!closed)
                    UpdateDashboard();
            }
        }
        line.Children.Add(labels);
        var row = PlainButton(line, $"{chat.Title}, {(approval != null ? "needs approval for " + approval.Tool : state)}", () => OpenPaletteChat(chat));
        row.Padding = new Thickness(8, 5, 8, 5);
        row.Margin = new Thickness(0);
        row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        row.ToolTip = AgentTooltip(chat);
        return row;
    }

    // Content that behaves like a button but looks like the content itself until hovered or focused.
    private Button PlainButton(object content, string name, Action action)
    {
        var button = MakeButton(content, name, action);
        button.Style = (Style)FindResource("AssistantPickerButton");
        button.Foreground = AssistantInk;
        button.Padding = new Thickness(8, 4, 8, 5);
        button.Margin = new Thickness(-8, 0, -8, 0);
        return button;
    }

    private async Task LoadHomeMeetingsAsync(Action loaded)
    {
        if (readingHomeMeetings || DateTimeOffset.UtcNow - homeMeetingsRead < TimeSpan.FromMinutes(5))
            return;
        var path = ReadJson<string>("calendar-source.json");
        if (string.IsNullOrWhiteSpace(path))
            return;
        readingHomeMeetings = true;
        try
        {
            homeMeetings = await Task.Run(() => ReadMeetings(path), lifetime.Token);
            homeMeetingsRead = DateTimeOffset.UtcNow;
            if (!closed && currentPage == DashboardPage.Home)
                loaded();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or
            ArgumentException or InvalidOperationException or Ical.Net.Evaluation.EvaluationOutOfRangeException)
        {
            homeMeetings = [];
            homeMeetingsRead = DateTimeOffset.UtcNow;
        }
        finally { readingHomeMeetings = false; }
    }
}
