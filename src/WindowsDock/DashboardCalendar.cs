using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private readonly DispatcherTimer focusTicker = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? focusEnds;
    private int focusMinutes = 25;
    private DateTime calendarWeek = DateTime.Today;
    private DateTime reminderDate = DateTime.Today;
    private bool showReminders;
    private List<Reminder>? reminders;
    private Action? updateFocusWidget;

    private UIElement CalendarPage()
    {
        var content = new StackPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 7) };
        foreach (var (name, icon, selected) in new[] { ("Calendar", 0xE787, false), ("Reminders", 0xEA8F, true) })
        {
            var tab = MakeButton(IconLabel(name, icon, amber), name, () =>
            {
                showReminders = selected;
                ShowPage(DashboardPage.Calendar);
            });
            tab.Background = selected == showReminders ? AgentArt.Wash(amber, 35) : Brushes.Transparent;
            tab.BorderBrush = selected == showReminders ? AgentArt.Wash(amber, 65) : Brushes.Transparent;
            tab.Margin = new Thickness(2, 0, 2, 0);
            tabs.Children.Add(tab);
        }
        content.Children.Add(tabs);
        content.Children.Add(showReminders ? RemindersContent() : CalendarContent());
        return content;
    }

    private UIElement CalendarContent()
    {
        var grid = WideDashboard ? Columns(0.8, 2.35, 1.55) : Columns(0.9, 2.2);
        var today = DateTime.Today;
        var date = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var stamp = GlyphBadge(0xE787, amber, 28);
        stamp.HorizontalAlignment = HorizontalAlignment.Center;
        stamp.Margin = new Thickness(0, 0, 0, 6);
        date.Children.Add(stamp);
        var heading = Text(today.ToString("dddd"), 11, amber, true);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        date.Children.Add(heading);
        var day = Text(today.ToString("d MMM"), 23, strong: true);
        day.HorizontalAlignment = HorizontalAlignment.Center;
        date.Children.Add(day);
        var todayButton = ActionButton("Today", () => { calendarWeek = DateTime.Today; ShowPage(DashboardPage.Calendar); });
        todayButton.HorizontalAlignment = HorizontalAlignment.Center;
        todayButton.Margin = new Thickness(0, 6, 0, 0);
        date.Children.Add(todayButton);
        Put(grid, Card(date, amber), 0);

        var calendar = new StackPanel();
        var nav = new DockPanel();
        var earlier = IconButton(0xE76B, "Previous week", () =>
        {
            calendarWeek = calendarWeek.AddDays(-7);
            ShowPage(DashboardPage.Calendar);
        });
        nav.Children.Add(earlier);
        var later = IconButton(0xE76C, "Next week", () =>
        {
            calendarWeek = calendarWeek.AddDays(7);
            ShowPage(DashboardPage.Calendar);
        });
        DockPanel.SetDock(later, Dock.Right);
        nav.Children.Add(later);
        var month = Text(calendarWeek.ToString("MMMM yyyy"), 12, strong: true);
        month.TextAlignment = TextAlignment.Center;
        month.VerticalAlignment = VerticalAlignment.Center;
        nav.Children.Add(month);
        calendar.Children.Add(nav);

        var first = calendarWeek.AddDays(-(((int)calendarWeek.DayOfWeek + 6) % 7));
        var week = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 8, 0, 0) };
        for (var i = 0; i < 7; i++)
        {
            var entry = first.AddDays(i);
            var values = new StackPanel();
            var isToday = entry == today;
            var weekday = Text(entry.ToString("ddd"), 10, isToday ? amber : muted);
            weekday.HorizontalAlignment = HorizontalAlignment.Center;
            values.Children.Add(weekday);
            var number = Text(entry.Day.ToString(), 14, isToday ? amber : Foreground, true);
            number.HorizontalAlignment = HorizontalAlignment.Center;
            values.Children.Add(number);
            var button = MakeButton(values, entry.ToString("D"), () =>
            {
                reminderDate = entry;
                showReminders = true;
                ShowPage(DashboardPage.Calendar);
            });
            button.Background = isToday ? AgentArt.Wash(amber, 35) : AgentArt.Wash(purple, 15);
            button.BorderBrush = isToday ? AgentArt.Wash(amber, 64) : Brushes.Transparent;
            button.Padding = new Thickness(2, 6, 2, 6);
            button.Margin = new Thickness(1, 0, 1, 0);
            week.Children.Add(button);
        }
        calendar.Children.Add(week);
        var meetings = ActionButton("Upcoming meetings", ShowMeetingWidget);
        meetings.HorizontalAlignment = HorizontalAlignment.Left;
        meetings.Margin = new Thickness(0, 9, 0, 0);
        calendar.Children.Add(meetings);
        Put(grid, Card(calendar), 1);

        var focus = FocusCard(out var update);
        if (WideDashboard)
            Put(grid, focus, 2);
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(focus, 1);
            Grid.SetColumnSpan(focus, 2);
            focus.Margin = new Thickness(0, 7, 0, 0);
            grid.Children.Add(focus);
        }
        updatePage = update;
        return grid;
    }

    private void ShowFocusWidget()
    {
        var (window, content, _) = CreateDockPanel("Focus timer", "", 406, 0xE916, green, widget: true);
        content.Children.Add(FocusCard(out var update, title: false));
        updateFocusWidget = update;
        window.Closed += (_, _) => updateFocusWidget = null;
        update();
        window.Show();
    }

    private Border FocusCard(out Action update, bool title = true)
    {
        var focus = new DockPanel { LastChildFill = true };
        const double size = 74;
        const double radius = 32;
        var badge = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0) };
        var dial = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = AgentArt.Wash(green, 43),
            Fill = AgentArt.Wash(green, 17), StrokeThickness = 3 };
        badge.Children.Add(dial);
        var remaining = new ShapePath { Stroke = green, StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        badge.Children.Add(remaining);
        var reading = Text("", 17, green, true);
        reading.HorizontalAlignment = HorizontalAlignment.Center;
        reading.VerticalAlignment = VerticalAlignment.Center;
        badge.Children.Add(reading);
        focus.Children.Add(badge);
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (title)
            controls.Children.Add(Text("Focus timer", 13, strong: true));
        var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        var buttons = new List<(int Minutes, Button Button)>();
        foreach (var minutes in new[] { 5, 10, 25, 50 })
        {
            var button = ActionButton(minutes + "m", () =>
            {
                focusMinutes = minutes;
                focusEnds = null;
                PersistFocus();
                UpdateDashboard();
            });
            button.Padding = new Thickness(6, 4, 6, 4);
            button.Margin = new Thickness(0, 0, 3, 0);
            buttons.Add((minutes, button));
            presets.Children.Add(button);
        }
        controls.Children.Add(presets);
        var actions = new WrapPanel();
        var toggle = AccentButton("Start", 0xE768, green, () =>
        {
            focusEnds = focusEnds == null ? DateTimeOffset.Now.AddMinutes(focusMinutes) : null;
            PersistFocus();
            UpdateDashboard();
        });
        toggle.Margin = new Thickness(0, 0, 4, 4);
        actions.Children.Add(toggle);
        var quiet = ActionButton("Windows Focus", () => OpenTarget("ms-settings:quiethours"));
        quiet.ToolTip = "Start a Windows Focus session with Do Not Disturb";
        actions.Children.Add(quiet);
        controls.Children.Add(actions);
        focus.Children.Add(controls);
        bool? showingTimer = null;
        var displayedPreset = 0;
        var displayedSeconds = -1;
        var track = AgentArt.Wash(green, 43);
        update = () =>
        {
            var running = focusEnds.HasValue;
            var duration = TimeSpan.FromMinutes(focusMinutes);
            var seconds = (int)Math.Ceiling(Math.Clamp((focusEnds - DateTimeOffset.Now ?? duration).TotalSeconds, 0, duration.TotalSeconds));
            if (displayedSeconds != seconds || displayedPreset != focusMinutes)
            {
                displayedSeconds = seconds;
                var ratio = seconds / duration.TotalSeconds;
                reading.Text = FormatTime(TimeSpan.FromSeconds(seconds));
                dial.Stroke = ratio >= 1 ? green : track;
                if (ratio is > 0 and < 1)
                {
                    var middle = size / 2;
                    var angle = (ratio * 2 - .5) * Math.PI;
                    var end = new Point(middle + radius * Math.Cos(angle), middle + radius * Math.Sin(angle));
                    var arc = new ArcSegment(end, new Size(radius, radius), 0, ratio > .5, SweepDirection.Clockwise, true);
                    remaining.Data = new PathGeometry(new[] { new PathFigure(new Point(middle, middle - radius), new[] { arc }, false) });
                }
                else
                    remaining.Data = null;
            }
            if (showingTimer != running)
            {
                showingTimer = running;
                var label = running ? "Reset" : "Start";
                toggle.Content = IconLabel(label, running ? 0xE72C : 0xE768, green);
                System.Windows.Automation.AutomationProperties.SetName(toggle, label);
            }
            if (displayedPreset != focusMinutes)
            {
                displayedPreset = focusMinutes;
                foreach (var (minutes, button) in buttons)
                {
                    button.Background = minutes == focusMinutes ? AgentArt.Wash(green, 35) : Brushes.Transparent;
                    button.BorderBrush = minutes == focusMinutes ? AgentArt.Wash(green, 65) : Brushes.Transparent;
                }
            }
        };
        return Card(focus, green);
    }

    private UIElement RemindersContent()
    {
        reminders ??= ReadJson<List<Reminder>>("reminders.json") ?? [];
        var grid = Columns(1.35, 1);
        var compose = new StackPanel();
        compose.Children.Add(CardHeading("Add reminder", 0xEA8F, amber));
        var input = Field("Reminder", 160);
        input.Margin = new Thickness(0, 6, 0, 5);
        compose.Children.Add(input);
        var actions = new DockPanel();
        var picker = Field("Reminder date", 24);
        picker.Text = reminderDate.ToString("d");
        picker.ToolTip = "Enter a date in your local format";
        picker.Width = 134;
        actions.Children.Add(picker);
        var add = ActionButton("Add", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                return;
            if (!DateTime.TryParse(picker.Text, out var chosen))
            {
                picker.Focus();
                ShowOwnedMessage("Enter a valid reminder date.");
                return;
            }
            reminderDate = chosen.Date;
            reminders.Add(new Reminder(input.Text.Trim(), reminderDate));
            WriteJson("reminders.json", reminders);
            ShowPage(DashboardPage.Calendar);
        });
        add.Background = AgentArt.Wash(amber, 35);
        add.BorderBrush = AgentArt.Wash(amber, 65);
        DockPanel.SetDock(add, Dock.Right);
        actions.Children.Add(add);
        compose.Children.Add(actions);
        Put(grid, Card(compose, amber), 0);

        var pending = new StackPanel();
        pending.Children.Add(Text("Pending", 12, subdued, true));
        foreach (var item in reminders.OrderBy(item => item.Date).Take(30))
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var done = IconButton(0xE73E, "Complete " + item.Title, () =>
            {
                reminders.Remove(item);
                WriteJson("reminders.json", reminders);
                ShowPage(DashboardPage.Calendar);
            });
            DockPanel.SetDock(done, Dock.Right);
            row.Children.Add(done);
            row.Children.Add(Text($"{item.Date:MMM d}   {item.Title}", 11));
            pending.Children.Add(row);
        }
        if (reminders.Count == 0)
            pending.Children.Add(Text("No pending reminders", 12, muted));
        Put(grid, Card(new ScrollViewer { Content = pending, MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }), 1);
        return grid;
    }

    private void InitializeFocus()
    {
        var saved = ReadJson<FocusState>("focus.json");
        if (saved is { Minutes: >= 1 and <= 120 })
        {
            focusMinutes = saved.Minutes;
            focusEnds = saved.Ends > DateTimeOffset.Now ? saved.Ends : null;
        }
        focusTicker.Tick += (_, _) =>
        {
            CheckFocusTimer();
            updateFocusWidget?.Invoke();
        };
        if (focusEnds.HasValue)
            focusTicker.Start();
        Closed += (_, _) => focusTicker.Stop();
    }

    private void CheckFocusTimer()
    {
        if (!focusEnds.HasValue || focusEnds > DateTimeOffset.Now)
            return;
        focusEnds = null;
        if (releaseWhenIdle)
            focusTicker.Stop();
        else
        {
            PersistFocus();
            System.Media.SystemSounds.Asterisk.Play();
        }
        if (IsVisible)
            UpdateDashboard();
    }

    private void PersistFocus()
    {
        if (focusEnds.HasValue)
            focusTicker.Start();
        else
            focusTicker.Stop();
        WriteJson("focus.json", new FocusState(focusMinutes, focusEnds));
    }

    private static string DataPath(string file) => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "WindowsDock", "Dashboard", file);

    private static T? ReadJson<T>(string file)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(DataPath(file))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }

    private void WriteJson<T>(string file, T value)
    {
        var path = DataPath(file);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowOwnedMessage("Windows Dock could not save this change.");
        }
    }

    private sealed record Reminder(string Title, DateTime Date);
}
