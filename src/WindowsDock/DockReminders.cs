namespace WindowsDock;

internal static class DockReminders
{
    private const string FileName = "reminders.json";

    internal static List<Reminder> Load() => DockData.Read<List<Reminder>>(FileName) ?? [];

    internal static void Add(Reminder item) => DockData.Update<List<Reminder>>(FileName, saved =>
    {
        var reminders = saved ?? [];
        if (!reminders.Contains(item))
            reminders.Add(item);
        return reminders;
    });

    internal static void Change(Reminder item, Reminder? replacement) => DockData.Update<List<Reminder>>(FileName, saved =>
    {
        var reminders = saved ?? [];
        var index = reminders.IndexOf(item);
        if (index < 0)
            throw new InvalidOperationException("That reminder could not be found. Check the exact title and date.");
        if (replacement == null)
            reminders.RemoveAt(index);
        else
            reminders[index] = replacement;
        return reminders;
    });
}
