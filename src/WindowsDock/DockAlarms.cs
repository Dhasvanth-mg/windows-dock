namespace WindowsDock;

/// <summary>A one-off alarm at a time of day. It rings once and then removes itself.</summary>
internal sealed record DockAlarm(Guid Id, string Label, DateTimeOffset At);

/// <summary>
/// Alarms kept in one local file and rung by the background monitor, so they go off even when the dashboard is
/// closed. Every change goes through the data store's lock.
/// </summary>
internal static class DockAlarms
{
    internal const int Limit = 50;
    internal const int LabelLimit = 120;
    internal const string FileName = "alarms.json";

    internal static List<DockAlarm> Load() =>
        (DockData.Read<List<DockAlarm>>(FileName) ?? []).OrderBy(alarm => alarm.At).ToList();

    /// <summary>The next time an alarm should ring, or null when none is set.</summary>
    internal static DateTimeOffset? Next(IEnumerable<DockAlarm> alarms) =>
        alarms.Select(alarm => (DateTimeOffset?)alarm.At).Min();

    internal static DockAlarm Add(string label, DateTimeOffset at, DateTimeOffset now)
    {
        var text = label.Trim();
        if (text.Length == 0)
            text = "Alarm";
        if (text.Length > LabelLimit || text.Any(char.IsControl))
            throw new ArgumentException($"Keep the alarm label to {LabelLimit} characters on one line.");
        if (at <= now)
            throw new ArgumentException("Choose a time that has not passed yet.");
        if (at > now.AddDays(366))
            throw new ArgumentException("Alarms can be set up to a year ahead.");
        var alarm = new DockAlarm(Guid.NewGuid(), text, at);
        DockData.Update<List<DockAlarm>>(FileName, saved =>
        {
            var alarms = saved ?? [];
            if (alarms.Count >= Limit)
                throw new InvalidOperationException($"That is {Limit} alarms. Remove one first.");
            alarms.Add(alarm);
            return alarms;
        });
        return alarm;
    }

    /// <summary>Removes one alarm. Returns false when it was already gone.</summary>
    internal static bool Remove(Guid id)
    {
        var removed = false;
        DockData.Update<List<DockAlarm>>(FileName, saved =>
        {
            var alarms = saved ?? [];
            removed = alarms.RemoveAll(alarm => alarm.Id == id) > 0;
            return alarms;
        });
        return removed;
    }

    /// <summary>
    /// Takes every alarm that is due and removes it in the same step, so two processes watching the same file can
    /// never both ring it.
    /// </summary>
    internal static DockAlarm[] TakeDue(DateTimeOffset now)
    {
        DockAlarm[] due = [];
        DockData.Update<List<DockAlarm>>(FileName, saved =>
        {
            var alarms = saved ?? [];
            due = alarms.Where(alarm => alarm.At <= now).OrderBy(alarm => alarm.At).ToArray();
            alarms.RemoveAll(alarm => alarm.At <= now);
            return alarms;
        });
        return due;
    }

    /// <summary>
    /// Turns "07:30" (and an optional "YYYY-MM-DD") into the next matching moment. With no date, a time that has
    /// already passed today means tomorrow, the way a clock app treats it.
    /// </summary>
    internal static DateTimeOffset When(string time, string? date, DateTimeOffset now)
    {
        if (!TimeOnly.TryParseExact(time.Trim(), ["HH:mm", "H:mm"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var clock))
            throw new ArgumentException("Enter the time as HH:MM on a 24-hour clock, such as 07:30 or 18:05.");
        var local = now.ToLocalTime();
        DateOnly day;
        if (string.IsNullOrWhiteSpace(date))
            day = DateOnly.FromDateTime(local.DateTime) is var today && today.ToDateTime(clock) > local.DateTime ? today : today.AddDays(1);
        else if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None, out day))
            throw new ArgumentException("Enter the date as YYYY-MM-DD.");
        var moment = day.ToDateTime(clock);
        return new DateTimeOffset(moment, TimeZoneInfo.Local.GetUtcOffset(moment));
    }
}
