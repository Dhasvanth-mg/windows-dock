using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
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

    private UIElement CalendarPage()
    {
        var content = new StackPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 7) };
        foreach (var (name, selected) in new[] { ("Calendar", false), ("Reminders", true) })
        {
            var tab = ActionButton(name, () => { showReminders = selected; ShowPage(DashboardPage.Calendar); });
            tab.Background = selected == showReminders ? Brush("#3DFFFFFF") : Brushes.Transparent;
            tab.Margin = new Thickness(2, 0, 2, 0);
            tabs.Children.Add(tab);
        }
        content.Children.Add(tabs);
        content.Children.Add(showReminders ? RemindersContent() : CalendarContent());
        return content;
    }

    private UIElement CalendarContent()
    {
        var grid = Columns(0.75, 2.45, 1.6);
        var today = DateTime.Today;
        var date = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Text(today.ToString("ddd MMM"), 12, Brush("#FF8487"), true);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        date.Children.Add(heading);
        var day = Text(today.ToString("dd"), 32, strong: true);
        day.HorizontalAlignment = HorizontalAlignment.Center;
        date.Children.Add(day);
        date.Children.Add(Text($"Week {System.Globalization.ISOWeek.GetWeekOfYear(today)}", 10, muted));
        Put(grid, Card(date), 0);

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
        var week = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < 7; i++)
        {
            var entry = first.AddDays(i);
            var values = new StackPanel();
            var weekday = Text(entry.ToString("ddd"), 10, muted);
            weekday.HorizontalAlignment = HorizontalAlignment.Center;
            values.Children.Add(weekday);
            var number = Text(entry.Day.ToString(), 12, strong: true);
            values.Children.Add(MakeButton(number, entry.ToString("D"), () =>
            {
                reminderDate = entry;
                showReminders = true;
                ShowPage(DashboardPage.Calendar);
            }));
            var button = (Button)values.Children[1];
            button.Background = entry == today ? Brush("#B42E45") : Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Padding = new Thickness(4, 3, 4, 3);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            week.Children.Add(values);
        }
        calendar.Children.Add(week);
        Put(grid, Card(calendar), 1);

        var focus = new DockPanel { LastChildFill = true };
        var badge = new Grid { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        badge.Children.Add(new Ellipse { Stroke = amber, StrokeThickness = 3 });
        var reading = Text("", 16, strong: true);
        reading.HorizontalAlignment = HorizontalAlignment.Center;
        reading.VerticalAlignment = VerticalAlignment.Center;
        badge.Children.Add(reading);
        focus.Children.Add(badge);
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(Text("Focus timer", 12, strong: true));
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var minutes in new[] { 5, 10, 25, 50 })
        {
            var button = ActionButton(minutes + "m", () =>
            {
                focusMinutes = minutes;
                focusEnds = null;
                PersistFocus();
                ShowPage(DashboardPage.Calendar);
            });
            button.Padding = new Thickness(5, 3, 5, 3);
            button.Margin = new Thickness(0, 0, 3, 0);
            if (minutes == focusMinutes)
                button.Background = Brush("#664315");
            presets.Children.Add(button);
        }
        controls.Children.Add(presets);
        var toggle = ActionButton("Start", () =>
        {
            focusEnds = focusEnds == null ? DateTimeOffset.Now.AddMinutes(focusMinutes) : null;
            PersistFocus();
            UpdateDashboard();
        });
        toggle.Background = Brush("#795018");
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        controls.Children.Add(toggle);
        focus.Children.Add(controls);
        Put(grid, Card(focus), 2);
        updatePage = () =>
        {
            var left = focusEnds.HasValue ? focusEnds.Value - DateTimeOffset.Now : TimeSpan.FromMinutes(focusMinutes);
            reading.Text = FormatTime(left < TimeSpan.Zero ? TimeSpan.Zero : left);
            toggle.Content = focusEnds.HasValue ? "Reset" : "Start";
            System.Windows.Automation.AutomationProperties.SetName(toggle, (string)toggle.Content);
        };
        return grid;
    }

    private UIElement RemindersContent()
    {
        reminders ??= ReadJson<List<Reminder>>("reminders.json") ?? [];
        var grid = Columns(1.35, 1);
        var compose = new StackPanel();
        compose.Children.Add(Text("Add reminder", 13, strong: true));
        var input = Field("Reminder", 160);
        input.Margin = new Thickness(0, 6, 0, 5);
        compose.Children.Add(input);
        var actions = new DockPanel();
        var picker = new DatePicker { SelectedDate = reminderDate, Width = 134 };
        System.Windows.Automation.AutomationProperties.SetName(picker, "Reminder date");
        actions.Children.Add(picker);
        var add = ActionButton("Add", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                return;
            reminderDate = picker.SelectedDate ?? DateTime.Today;
            reminders.Add(new Reminder(input.Text.Trim(), reminderDate));
            WriteJson("reminders.json", reminders);
            ShowPage(DashboardPage.Calendar);
        });
        add.Background = Brush("#275998");
        DockPanel.SetDock(add, Dock.Right);
        actions.Children.Add(add);
        compose.Children.Add(actions);
        Put(grid, Card(compose), 0);

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
        focusTicker.Tick += (_, _) => CheckFocusTimer();
        focusTicker.Start();
        Closed += (_, _) => focusTicker.Stop();
    }

    private void CheckFocusTimer()
    {
        if (!focusEnds.HasValue || focusEnds > DateTimeOffset.Now)
            return;
        focusEnds = null;
        PersistFocus();
        System.Media.SystemSounds.Asterisk.Play();
        if (IsVisible)
            UpdateDashboard();
    }

    private void PersistFocus() => WriteJson("focus.json", new FocusState(focusMinutes, focusEnds));

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
    private sealed record FocusState(int Minutes, DateTimeOffset? Ends);
}
