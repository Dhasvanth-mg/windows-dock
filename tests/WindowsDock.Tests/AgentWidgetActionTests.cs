using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class AgentWidgetActionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-agent-widgets-" + Guid.NewGuid().ToString("N"));

    public AgentWidgetActionTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private static DockAction Plan(string name, object args)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(args));
        return DockActions.Plan(name, document.RootElement.Clone());
    }

    private static string Run(DockAction action) => action.Execute(CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void AlarmTimeWithoutDateMeansTheNextMatchingMoment()
    {
        var now = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 17, 9, 0, 0)));
        var later = DockAlarms.When("18:05", null, now);
        Assert.Equal(new DateTime(2026, 11, 17, 18, 5, 0), later.LocalDateTime);
        var passed = DockAlarms.When("7:30", null, now);
        Assert.Equal(new DateTime(2026, 11, 18, 7, 30, 0), passed.LocalDateTime);
        Assert.Equal(new DateTime(2026, 12, 1, 6, 0, 0), DockAlarms.When("06:00", "2026-12-01", now).LocalDateTime);
        Assert.Throws<ArgumentException>(() => DockAlarms.When("7pm", null, now));
        Assert.Throws<ArgumentException>(() => DockAlarms.When("07:00", "tomorrow", now));
    }

    [Fact]
    public void AlarmsAreValidatedAndRingOnlyOnce()
    {
        var now = DateTimeOffset.Now;
        Assert.Throws<ArgumentException>(() => DockAlarms.Add("Late", now.AddMinutes(-1), now));
        Assert.Throws<ArgumentException>(() => DockAlarms.Add("Far", now.AddDays(400), now));
        Assert.Throws<ArgumentException>(() => DockAlarms.Add("two\nlines", now.AddHours(1), now));

        var soon = DockAlarms.Add("Tea", now.AddMinutes(5), now);
        var later = DockAlarms.Add(" ", now.AddHours(2), now);
        Assert.Equal("Alarm", later.Label);
        Assert.Equal(soon.At, DockAlarms.Next(DockAlarms.Load()));

        Assert.Empty(DockAlarms.TakeDue(now));
        var due = Assert.Single(DockAlarms.TakeDue(now.AddMinutes(10)));
        Assert.Equal(soon.Id, due.Id);
        Assert.Empty(DockAlarms.TakeDue(now.AddMinutes(10)));

        Assert.True(DockAlarms.Remove(later.Id));
        Assert.False(DockAlarms.Remove(later.Id));
        Assert.Null(DockAlarms.Next(DockAlarms.Load()));
    }

    [Fact]
    public void AgentsCanSetAndCancelAlarmsAfterPreview()
    {
        var at = DateTimeOffset.Now.AddHours(3);
        var time = at.ToString("HH:mm");
        var date = at.ToString("yyyy-MM-dd");
        var plan = Plan("set_alarm", new { time, date, label = "Stand up" });
        Assert.Contains("Stand up", plan.Preview);
        Assert.Empty(DockAlarms.Load());

        Run(plan);
        var alarm = Assert.Single(DockAlarms.Load());
        Assert.Equal("Stand up", alarm.Label);

        Run(Plan("cancel_alarm", new { label = "stand up" }));
        Assert.Empty(DockAlarms.Load());
        Assert.Throws<ArgumentException>(() => Plan("cancel_alarm", new { label = "stand up" }));
        Assert.Throws<ArgumentException>(() => Plan("cancel_alarm", new { }));
    }

    [Fact]
    public void AgentsCanAddAndCompleteTodos()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        Run(Plan("add_todo", new { title = "Buy milk", date = tomorrow.ToString("yyyy-MM-dd") }));
        Run(Plan("add_todo", new { title = "Call the bank" }));
        Assert.Equal(DateTime.Today, DockReminders.Load().Single(item => item.Title == "Call the bank").Date);

        Run(Plan("complete_todo", new { title = "buy milk" }));
        Assert.DoesNotContain(DockReminders.Load(), item => item.Title == "Buy milk");
        Assert.Throws<ArgumentException>(() => Plan("complete_todo", new { title = "Buy milk" }));
        Assert.Throws<ArgumentException>(() => Plan("add_todo", new { title = "x", date = "next week" }));
    }

    [Fact]
    public void AgentsCanWriteStickiesAndMemos()
    {
        Run(Plan("add_sticky", new { text = "Water the plants", color = "mint" }));
        Run(Plan("add_sticky", new { text = "Pick up parcel", show_on_today = true }));
        Assert.Equal("Pick up parcel", DockNotes.Title(DockNotes.ForToday(DockNotes.Load())!));
        Assert.Throws<ArgumentException>(() => Plan("add_sticky", new { text = "x", color = "neon" }));

        Run(Plan("show_sticky_on_today", new { title = "Water" }));
        var today = DockNotes.ForToday(DockNotes.Load())!;
        Assert.Equal("Water the plants", DockNotes.Title(today));
        Assert.Equal("mint", today.Color);
        Assert.Single(DockNotes.Load(), note => note.Pinned);

        Run(Plan("add_memo", new { text = "Trip ideas\nLisbon" }));
        Run(Plan("append_note", new { title = "Trip ideas", text = "Porto" }));
        Assert.Equal("Trip ideas\nLisbon\nPorto", DockNotes.Load().Single(note => note.Kind == DockNotes.Memo).Text);
    }

    [Fact]
    public void NotesThatLookSecretAreHiddenFromAgents()
    {
        DockNotes.Add(DockNotes.Memo, "Wifi password is hunter2");
        DockNotes.Add(DockNotes.Memo, "Groceries");

        var summaries = JsonSerializer.Serialize(DockActions.NoteSummaries());
        Assert.Contains("Groceries", summaries);
        Assert.DoesNotContain("Wifi", summaries);
        Assert.Throws<ArgumentException>(() => Plan("append_note", new { title = "Wifi", text = "changed" }));
    }
}
