using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal static class CodexUsageClient
{
    internal static async Task<AgentPlanUsage?> ReadAsync(CancellationToken cancellation)
    {
        var executable = FindExecutable();
        if (executable == null)
            return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--stdio");
        try
        {
            using var process = Process.Start(start);
            if (process == null)
                return null;
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            JsonElement limits = default, usage = default;
            try
            {
                await process.StandardInput.WriteLineAsync(
                    """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"windows-dock","version":"0.1.0"}}}"""
                        .AsMemory(), timeout.Token).ConfigureAwait(false);
                var initialized = await ReadResponseAsync(process.StandardOutput, 0, timeout.Token).ConfigureAwait(false);
                if (initialized.ValueKind != JsonValueKind.Object)
                    return null;
                await process.StandardInput.WriteLineAsync("""{"method":"initialized"}""".AsMemory(), timeout.Token)
                    .ConfigureAwait(false);
                await process.StandardInput.WriteLineAsync("""{"method":"account/rateLimits/read","id":1}""".AsMemory(), timeout.Token)
                    .ConfigureAwait(false);
                await process.StandardInput.WriteLineAsync("""{"method":"account/usage/read","id":2}""".AsMemory(), timeout.Token)
                    .ConfigureAwait(false);
                for (var received = 0; received < 2;)
                {
                    using var doc = await ReadMessageAsync(process.StandardOutput, timeout.Token).ConfigureAwait(false);
                    if (doc == null)
                        break;
                    var id = Count(doc.RootElement, "id");
                    if (id is not (1 or 2))
                        continue;
                    received++;
                    if (id == 1)
                        limits = Property(doc.RootElement, "result").Clone();
                    else
                        usage = Property(doc.RootElement, "result").Clone();
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
                try { await errors.ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
            return AgentUsage.ParseCodexPlan(limits, usage, DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException) { return null; }
    }

    private static async Task<JsonElement> ReadResponseAsync(StreamReader reader, int id, CancellationToken cancellation)
    {
        while (await ReadMessageAsync(reader, cancellation).ConfigureAwait(false) is { } doc)
        {
            using (doc)
                if (Count(doc.RootElement, "id") == id)
                    return Property(doc.RootElement, "result").Clone();
        }
        return default;
    }

    private static async Task<JsonDocument?> ReadMessageAsync(StreamReader reader, CancellationToken cancellation)
    {
        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            if (line.Length > 2 * 1024 * 1024)
                continue;
            if (ParseTranscriptRecord(line) is { } doc)
                return doc;
        }
        return null;
    }

    internal static string? FindExecutable()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try
        {
            var installed = Directory.EnumerateFiles(root, "codex.exe", new EnumerationOptions
                {
                    RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (installed != null)
                return installed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        return FindOnPath("codex.exe");
    }

    internal static string? FindOnPath(string executable)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = folder.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory))
                continue;
            var path = Path.Combine(directory, executable);
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
