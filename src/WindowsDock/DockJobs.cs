using System.ComponentModel;
using System.Diagnostics;

namespace WindowsDock;

internal enum DockJobStatus { Running, Completed, Failed, Canceled, Interrupted }

internal sealed record DockJob(Guid Id, string Role, string Task, string? Workspace, DateTimeOffset Started,
    DockJobStatus Status, int Worker, string Result, string? Proposal = null)
{
    internal string Specialist => new DockHandoff(Role, Task).Name;
}

internal static class DockJobs
{
    internal const string DataFile = "assistant-jobs.json";

    internal static DockJob[] Load() => (DockData.Read<DockJob[]>(DataFile) ?? [])
        .Select(job => job.Status == DockJobStatus.Running && !WorkerIsAlive(job)
            ? job with { Status = DockJobStatus.Interrupted, Result = "The previous dock process exited. Check the workspace before retrying a coding job." }
            : job)
        .OrderByDescending(job => job.Started).ToArray();

    internal static void Save(DockJob job) => DockData.Update<DockJob[]>(DataFile, saved =>
    {
        var others = (saved ?? []).Where(item => item.Id != job.Id);
        return others.Append(job).OrderByDescending(item => item.Started).Take(24).ToArray();
    });

    private static bool WorkerIsAlive(DockJob job)
    {
        try
        {
            using var worker = Process.GetProcessById(job.Worker);
            return !worker.HasExited && worker.StartTime.ToUniversalTime() <= job.Started.UtcDateTime;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or ArgumentException) { return false; }
    }
}
