using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Microsoft.Win32;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowMeetingWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Meetings", "", 470, 0xE787, accent, widget: true);
        BuildPinnedMeetings(window, content, cancellation);
        window.Show();
    }

    private void BuildPinnedMeetings(Window window, StackPanel content, System.Threading.CancellationToken cancellation)
    {
        var status = Text("Loading calendar…", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        var list = new StackPanel();
        content.Children.Add(status);
        content.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var select = AccentButton("Choose calendar file…", 0xE8B7, accent);
        select.Margin = new Thickness(0, 0, 6, 4);
        actions.Children.Add(select);
        var refresh = ActionButton("Refresh");
        actions.Children.Add(refresh);
        content.Children.Add(actions);
        BuildLocalCalendar(window, content);
        var path = ReadJson<string>("calendar-source.json");
        select.Click += (_, _) => Choose();
        refresh.Click += (_, _) => _ = LoadAsync();
        _ = LoadAsync();

        void Choose()
        {
            var picker = new OpenFileDialog { Filter = "Calendar files|*.ics|All files|*.*", CheckFileExists = true };
            if (picker.ShowDialog(window) != true)
                return;
            path = picker.FileName;
            WriteJson("calendar-source.json", path);
            _ = LoadAsync();
        }

        async System.Threading.Tasks.Task LoadAsync()
        {
            refresh.IsEnabled = select.IsEnabled = false;
            list.Children.Clear();
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    status.Text = "Choose an .ics calendar export to see upcoming meetings.";
                    return;
                }
                status.Text = "Reading " + Path.GetFileName(path) + "…";
                var selected = path;
                var meetings = await System.Threading.Tasks.Task.Run(() => ReadMeetings(selected), cancellation);
                if (cancellation.IsCancellationRequested || path != selected)
                    return;
                status.Text = meetings.Length == 0 ? "No timed events in the next seven days." : "Upcoming";
                foreach (var item in meetings)
                {
                    var body = new StackPanel();
                    body.Children.Add(CardHeading(item.Title, 0xE787, accent,
                        detail: item.Start.LocalDateTime.ToString("ddd, MMM d · t")));
                    if (item.Link != null)
                    {
                        var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                        var join = AccentButton("Join", 0xE8A7, accent, () => OpenTarget(item.Link));
                        join.Margin = new Thickness(0, 0, 5, 0);
                        buttons.Children.Add(join);
                        buttons.Children.Add(ActionButton("Copy link", () =>
                        {
                            try { Clipboard.SetText(item.Link); status.Text = "Meeting link copied."; }
                            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "The clipboard is busy."; }
                        }));
                        body.Children.Add(buttons);
                    }
                    var card = Card(body, accent);
                    card.Margin = new Thickness(0, 5, 0, 0);
                    list.Children.Add(card);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or
                ArgumentException or InvalidOperationException or Ical.Net.Evaluation.EvaluationOutOfRangeException)
            {
                status.Text = "The calendar could not be read. Check the file or select another export.";
            }
            finally { refresh.IsEnabled = select.IsEnabled = true; }
        }
    }

    private void BuildLocalCalendar(Window window, StackPanel content)
    {
        var local = new StackPanel();
        var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, 5) };
        var add = ActionButton("New local event");
        add.Margin = new Thickness(0, 0, 5, 4);
        var export = ActionButton("Export local calendar");
        buttons.Children.Add(add);
        buttons.Children.Add(export);
        var date = Field("Event date", 10);
        var title = Field("Event title", 140);
        var start = Field("Start time", 5);
        var end = Field("End time", 5);
        var save = ActionButton("Save event");
        var remove = ActionButton("Delete event");
        var cancel = ActionButton("Cancel event edit");
        var editorButtons = new WrapPanel { Margin = new Thickness(0, 7, 0, 0), Children = { save, remove, cancel } };
        var times = Columns(1, 1);
        Put(times, Labeled("Start · HH:mm", start), 0);
        Put(times, Labeled("End · HH:mm", end), 1);
        var editor = new StackPanel
        {
            Children = { Labeled("Title", title), Labeled("Date · YYYY-MM-DD", date), times, editorButtons },
            Visibility = Visibility.Collapsed
        };
        var editId = Guid.Empty;
        var feedback = Text("", 11, AssistantSecondary);
        feedback.TextWrapping = TextWrapping.Wrap;
        var overview = new StackPanel();
        var range = new DatePicker { SelectedDate = DateTime.Today };
        System.Windows.Automation.AutomationProperties.SetName(range, "Show local events for date");
        local.Children.Add(CardHeading("Local calendar", 0xE787, accent));
        local.Children.Add(buttons);
        local.Children.Add(editor);
        local.Children.Add(feedback);
        local.Children.Add(range);
        local.Children.Add(overview);
        content.Children.Add(Card(local));

        add.Click += (_, _) => Edit(null);
        cancel.Click += (_, _) => editor.Visibility = Visibility.Collapsed;
        range.SelectedDateChanged += (_, _) => Render();
        remove.Click += (_, _) =>
        {
            if (editId == Guid.Empty || MessageBox.Show(window, "Delete this local event?", "Calendar",
                MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                DockEvents.Remove(editId);
                feedback.Text = "Event deleted.";
                editor.Visibility = Visibility.Collapsed;
                Render();
                RefreshCalendarPage();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { feedback.Text = "Could not delete this event."; }
        };
        save.Click += (_, _) =>
        {
            try
            {
                var id = editId == Guid.Empty ? Guid.NewGuid() : editId;
                var schedule = DockEvents.Save(id, title.Text, date.Text, start.Text, end.Text);
                var saved = schedule.First(item => item.Id == id);
                feedback.Text = DockEvents.Overlaps(saved, schedule) ? "Saved. This overlaps another local event." : "Event saved.";
                editor.Visibility = Visibility.Collapsed;
                range.SelectedDate = saved.Start.LocalDateTime.Date;
                Render();
                RefreshCalendarPage();
            }
            catch (ArgumentException error) { feedback.Text = error.Message; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { feedback.Text = "Could not save this event."; }
        };
        export.Click += (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = "Calendar file|*.ics", FileName = "Windows Dock.ics" };
            if (picker.ShowDialog(window) != true)
                return;
            try
            {
                File.WriteAllText(picker.FileName, DockEvents.Export());
                feedback.Text = "Local calendar exported.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { feedback.Text = "Could not export this calendar."; }
        };
        Render();

        void Edit(DockEvent? item)
        {
            editId = item?.Id ?? Guid.Empty;
            title.Text = item?.Title ?? "";
            date.Text = (item?.Start.LocalDateTime.Date ?? range.SelectedDate ?? DateTime.Today)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            start.Text = item?.Start.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "09:00";
            end.Text = item?.End.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "10:00";
            editor.Visibility = Visibility.Visible;
            remove.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            title.Focus();
        }

        void Render()
        {
            overview.Children.Clear();
            var day = range.SelectedDate ?? DateTime.Today;
            foreach (var item in DockEvents.Load().Where(item => item.Start.LocalDateTime.Date == day.Date))
            {
                var label = $"{item.Start.LocalDateTime:t}  {item.Title}";
                var edit = ActionButton(label, () => Edit(item));
                edit.HorizontalContentAlignment = HorizontalAlignment.Left;
                edit.Margin = new Thickness(0, 4, 0, 0);
                overview.Children.Add(edit);
            }
            if (overview.Children.Count == 0)
                overview.Children.Add(Text("No local events for this day.", 11, AssistantSecondary));
        }

        void RefreshCalendarPage()
        {
            if (!ReferenceEquals(window, this) && currentPage == DashboardPage.Home)
                ShowPage(DashboardPage.Home);
        }
    }

    internal static Meeting[] ReadMeetings(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 8 * 1024 * 1024)
            throw new IOException("Calendar file is larger than 8 MB.");
        var calendar = Ical.Net.Calendar.Load(stream);
        if (calendar == null)
            return [];
        var now = DateTimeOffset.UtcNow;
        var from = new CalDateTime(now.AddDays(-1).UtcDateTime);
        var until = new CalDateTime(now.AddDays(7).UtcDateTime);
        var meetings = new List<Meeting>();
        foreach (var occurrence in calendar.GetOccurrences(from).TakeWhileBefore(until).Take(1000))
        {
            if (occurrence.Source is not CalendarEvent item || item.IsAllDay ||
                string.Equals(item.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                continue;
            var start = new DateTimeOffset(occurrence.Period.StartTime.AsUtc, TimeSpan.Zero);
            if (start < now && (occurrence.Period.EndTime is not { } end || end.AsUtc <= now.UtcDateTime))
                continue;
            var link = MeetingLink(item);
            meetings.Add(new(string.IsNullOrWhiteSpace(item.Summary) ? "Calendar event" : item.Summary, start, link));
        }
        return meetings.OrderBy(item => item.Start).Distinct().Take(8).ToArray();
    }

    private static string? MeetingLink(CalendarEvent item)
    {
        var fields = new[] { item.Url?.OriginalString, item.Location, item.Description };
        foreach (var field in fields.Where(field => !string.IsNullOrWhiteSpace(field)))
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(field!,
                """https://[^\s<>"']+""", System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)))
            {
                var value = match.Value.TrimEnd(')', ']', '.', ',');
                if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.UserInfo.Length != 0)
                    continue;
                if (url.Host is "meet.google.com" or "teams.microsoft.com" or "teams.live.com" or "zoom.us" or "webex.com" ||
                    url.Host.EndsWith(".zoom.us", StringComparison.OrdinalIgnoreCase) ||
                    url.Host.EndsWith(".webex.com", StringComparison.OrdinalIgnoreCase))
                    return url.AbsoluteUri;
            }
        return null;
    }

    internal sealed record Meeting(string Title, DateTimeOffset Start, string? Link);
}
