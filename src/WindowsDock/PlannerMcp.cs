using System.IO;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WindowsDock;

internal sealed record Reminder(string Title, DateTime Date);

internal static class PlannerMcp
{
    internal static int Run()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "Dock Planner", Version = "1.0" },
            ToolCollection = [McpServerTool.Create(ReadSchedule, new McpServerToolCreateOptions
            {
                Name = "upcoming_schedule",
                Description = "Read due and upcoming Windows Dock reminders and events, including meetings from the configured calendar export.",
                ReadOnly = true
            })]
        };
        var transport = new StreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
        var server = McpServer.Create(transport, options);
        try { server.RunAsync().GetAwaiter().GetResult(); }
        finally { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        return 0;
    }

    private static string ReadSchedule(int days)
    {
        var until = DateTime.Today.AddDays(Math.Clamp(days, 1, 7));
        var reminders = DockReminders.Load()
            .Where(item => item.Date.Date < until).OrderBy(item => item.Date).Take(30)
            .Select(item => new { title = item.Title, date = item.Date.ToString("yyyy-MM-dd") });
        var path = DockData.Read<string>("calendar-source.json");
        var calendar = string.IsNullOrWhiteSpace(path) ? "No calendar imported" : "Imported calendar";
        DashboardWindow.Meeting[] meetings = [];
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { meetings = DashboardWindow.ReadMeetings(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or
                ArgumentException or InvalidOperationException or Ical.Net.Evaluation.EvaluationOutOfRangeException)
            {
                calendar = "Calendar export could not be read";
            }
        }
        return JsonSerializer.Serialize(new
        {
            today = DateTime.Today.ToString("yyyy-MM-dd"), calendar, reminders,
            localEvents = DockEvents.Load().Where(item => item.End >= DateTimeOffset.Now && item.Start.LocalDateTime < until)
                .Take(40).Select(item => new { title = item.Title, starts = item.Start.ToLocalTime(), ends = item.End.ToLocalTime() }),
            meetings = meetings.Where(item => item.Start.LocalDateTime < until)
                .Select(item => new { title = item.Title, starts = item.Start.ToLocalTime() })
        });
    }
}
