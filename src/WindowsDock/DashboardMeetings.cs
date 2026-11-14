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
        var (window, content, cancellation) = CreateAgentPanel("Meetings", "", 470);
        var status = Text("Loading calendar…", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        var list = new StackPanel();
        content.Children.Add(status);
        content.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var select = ActionButton("Choose calendar file…");
        select.Margin = new Thickness(0, 0, 6, 4);
        actions.Children.Add(select);
        var refresh = ActionButton("Refresh");
        actions.Children.Add(refresh);
        content.Children.Add(actions);
        var path = ReadJson<string>("calendar-source.json");
        select.Click += (_, _) => Choose();
        refresh.Click += (_, _) => _ = LoadAsync();
        window.Show();
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
                    body.Children.Add(Text(item.Title, 13, strong: true));
                    body.Children.Add(Text(item.Start.LocalDateTime.ToString("ddd, MMM d · t"), 11, subdued));
                    if (item.Link != null)
                    {
                        var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                        var join = ActionButton("Join", () => OpenTarget(item.Link));
                        join.Margin = new Thickness(0, 0, 5, 0);
                        buttons.Children.Add(join);
                        buttons.Children.Add(ActionButton("Copy link", () =>
                        {
                            try { Clipboard.SetText(item.Link); status.Text = "Meeting link copied."; }
                            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "The clipboard is busy."; }
                        }));
                        body.Children.Add(buttons);
                    }
                    var card = Card(body);
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
