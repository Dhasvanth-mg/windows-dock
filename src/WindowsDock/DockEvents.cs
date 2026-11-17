using System.Globalization;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace WindowsDock;

internal sealed record DockEvent(Guid Id, string Title, DateTimeOffset Start, DateTimeOffset End);

internal static class DockEvents
{
    private const string StateFile = "local-calendar.json";
    private const int EventLimit = 2048;

    internal static DockEvent[] Load() => DockData.Read<DockEvent[]>(StateFile) ?? [];

    internal static DockEvent[] Save(Guid id, string title, string date, string start, string end)
    {
        title = title.Trim();
        if (title.Length is < 1 or > 140)
            throw new ArgumentException("Enter an event title up to 140 characters.");
        var begins = LocalTime(date, start);
        var ends = LocalTime(date, end);
        if (ends <= begins)
            throw new ArgumentException("End time must be after the start on the same day.");
        var item = new DockEvent(id, title, begins, ends);
        return DockData.Update<DockEvent[]>(StateFile, current =>
        {
            var rest = (current ?? []).Where(other => other.Id != id).ToArray();
            if (rest.Length >= EventLimit)
                throw new ArgumentException("The local calendar is full. Remove an older event first.");
            return rest.Append(item).OrderBy(other => other.Start).ToArray();
        });
    }

    internal static DateTimeOffset LocalTime(string date, string time)
    {
        if (!DateTime.TryParseExact($"{date.Trim()} {time.Trim()}", "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ArgumentException("Use YYYY-MM-DD for the date and HH:mm for times.");
        var zone = TimeZoneInfo.Local;
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("Choose a time outside the daylight saving clock change.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    internal static void Remove(Guid id) => DockData.Update<DockEvent[]>(StateFile,
        current => (current ?? []).Where(item => item.Id != id).ToArray());

    internal static bool Overlaps(DockEvent item, IEnumerable<DockEvent> schedule) =>
        schedule.Any(other => item.Id != other.Id && item.Start < other.End && other.Start < item.End);

    internal static string Export()
    {
        var calendar = new Ical.Net.Calendar();
        foreach (var item in Load())
        {
            calendar.Events.Add(new CalendarEvent
            {
                Uid = item.Id.ToString("N") + "@windowsdock.local",
                Summary = item.Title,
                DtStart = new CalDateTime(item.Start.UtcDateTime, "UTC"),
                DtEnd = new CalDateTime(item.End.UtcDateTime, "UTC")
            });
        }
        return new CalendarSerializer().SerializeToString(calendar) ?? throw new InvalidOperationException("Could not serialize the calendar.");
    }
}
