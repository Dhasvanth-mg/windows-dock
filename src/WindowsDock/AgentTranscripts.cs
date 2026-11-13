using System.IO;
using System.Text.Json;
using System.Threading;

namespace WindowsDock;

internal sealed record AgentReply(string Text, bool Clipped);
internal sealed record AgentTokenUsage(long Input, long CachedInput, long CacheWrite, long? Output, long? Total, bool Partial);

internal static partial class AgentSessions
{
    internal const int SummaryLimit = 12_000;
    private const int TranscriptTail = 4 * 1024 * 1024;

    internal static IEnumerable<string> RecentCodexRollouts() =>
        Files(Path.Combine(CodexHome, "sessions"), "rollout-*.jsonl", true, 6);

    internal static string[] ReadTail(string path) => Tail(path, TranscriptTail);

    internal static AgentReply? LatestReply(AgentSession session, CancellationToken cancellation)
    {
        foreach (var path in TranscriptPaths(session))
        {
            foreach (var bytes in new[] { TranscriptTail, 16 * 1024 * 1024 })
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var lines = Tail(path, bytes);
                    var reply = session.Provider == AgentProvider.Codex
                        ? CodexReply(lines, cancellation)
                        : ClaudeReply(lines, session.LastReplyId, cancellation);
                    if (reply != null)
                        return reply;
                    if (new FileInfo(path).Length <= bytes)
                        break;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { break; }
            }
        }
        return null;
    }

    internal static AgentTokenUsage? TokenUsage(AgentSession session, CancellationToken cancellation)
    {
        // A Claude Desktop chat can restart its CLI session. Report usage for the current transcript.
        var path = TranscriptPaths(session, includeLastReply: false).FirstOrDefault();
        if (path == null)
            return null;
        try
        {
            var limit = session.Provider == AgentProvider.Codex ? 16 * 1024 * 1024 : TranscriptTail;
            var partial = new FileInfo(path).Length > limit;
            var lines = Tail(path, limit);
            return session.Provider == AgentProvider.Codex
                ? CodexTokens(lines, cancellation) : ClaudeTokens(lines, partial, cancellation);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> TranscriptPaths(AgentSession session, bool includeLastReply = true)
    {
        var ids = includeLastReply ? new[] { session.LastReplySessionId, session.Id } : new[] { session.Id };
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Guid.TryParseExact(id, "D", out _))
                continue;
            var codex = session.Provider == AgentProvider.Codex;
            var directory = codex ? Path.Combine(CodexHome, "sessions") : Path.Combine(Profile, ".claude", "projects");
            var pattern = codex ? $"rollout-*-{id}.jsonl" : $"{id}.jsonl";
            foreach (var path in Files(directory, pattern, true, 1))
                yield return path;
        }
    }

    internal static AgentReply? CodexReply(string[] lines, CancellationToken cancellation)
    {
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = lines[index];
            if (!line.Contains("\"final_answer\"", StringComparison.Ordinal) &&
                !line.Contains("\"task_complete\"", StringComparison.Ordinal))
                continue;
            using var doc = ParseTranscriptRecord(line);
            if (doc == null)
                continue;
            var record = doc.RootElement;
            var payload = Property(record, "payload");
            var type = String(record, "type");
            if (type == "response_item" && String(payload, "type") == "message" &&
                String(payload, "role") == "assistant" && String(payload, "phase") == "final_answer")
            {
                if (ClipReply(VisibleText(payload, "output_text")) is { } reply)
                    return reply;
            }
            if (type == "event_msg" && String(payload, "type") == "task_complete" &&
                ClipReply(String(payload, "last_agent_message")) is { } completed)
                return completed;
        }
        return null;
    }

    internal static AgentReply? ClaudeReply(string[] lines, string? lastReplyId, CancellationToken cancellation)
    {
        string? messageId = null;
        var chunks = new List<string>();
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = lines[index];
            if (!line.Contains("\"assistant\"", StringComparison.Ordinal))
                continue;
            using var doc = ParseTranscriptRecord(line);
            if (doc == null || !ClaudeAssistant(doc.RootElement, out var message))
                continue;
            var id = String(message, "id");
            if (messageId != null && id != messageId)
                break;
            var visible = VisibleText(message, "text");
            if (messageId == null && String(message, "stop_reason") != "end_turn" &&
                (lastReplyId == null || lastReplyId != String(doc.RootElement, "uuid")))
                continue;
            if (visible.Length > 0)
                chunks.Add(visible);
            if (id == null)
                break;
            messageId = id;
        }
        chunks.Reverse();
        return ClipReply(string.Join("\n\n", chunks));
    }

    internal static AgentTokenUsage? CodexTokens(string[] lines, CancellationToken cancellation)
    {
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            cancellation.ThrowIfCancellationRequested();
            var line = lines[index];
            if (!line.Contains("\"token_count\"", StringComparison.Ordinal))
                continue;
            using var doc = ParseTranscriptRecord(line);
            if (doc == null || String(doc.RootElement, "type") != "event_msg")
                continue;
            var payload = Property(doc.RootElement, "payload");
            if (String(payload, "type") != "token_count")
                continue;
            var usage = Property(Property(payload, "info"), "total_token_usage");
            if (Count(usage, "total_tokens") is not { } total)
                continue;
            return new(Count(usage, "input_tokens") ?? 0, Count(usage, "cached_input_tokens") ?? 0,
                Count(usage, "cache_write_input_tokens") ?? 0, Count(usage, "output_tokens"), total, false);
        }
        return null;
    }

    internal static AgentTokenUsage? ClaudeTokens(string[] lines, bool partial, CancellationToken cancellation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long input = 0, cached = 0, written = 0;
        foreach (var line in lines.Reverse())
        {
            cancellation.ThrowIfCancellationRequested();
            if (!line.Contains("\"usage\"", StringComparison.Ordinal) || !line.Contains("\"assistant\"", StringComparison.Ordinal))
                continue;
            using var doc = ParseTranscriptRecord(line);
            if (doc == null || !ClaudeAssistant(doc.RootElement, out var message) ||
                String(message, "id") is not { } id || String(message, "model") is null or "<synthetic>")
                continue;
            var usage = Property(message, "usage");
            if (Count(usage, "input_tokens") is not { } fresh || !seen.Add(id))
                continue;
            var cacheRead = Count(usage, "cache_read_input_tokens") ?? 0;
            var cacheWrite = Count(usage, "cache_creation_input_tokens") ?? 0;
            if (fresh > int.MaxValue || cacheRead > int.MaxValue || cacheWrite > int.MaxValue)
                continue;
            input += fresh + cacheRead + cacheWrite;
            cached += cacheRead;
            written += cacheWrite;
        }

        // Claude's assistant records only provide an output placeholder at message start.
        return seen.Count > 0 ? new(input, cached, written, null, null, partial) : null;
    }

    internal static JsonDocument? ParseTranscriptRecord(string line)
    {
        try { return JsonDocument.Parse(line); }
        catch (JsonException) { return null; }
    }

    private static bool ClaudeAssistant(JsonElement record, out JsonElement message)
    {
        message = Property(record, "message");
        return String(record, "type") == "assistant" && String(message, "role") == "assistant" &&
            Property(record, "isSidechain").ValueKind != JsonValueKind.True &&
            Property(record, "isApiErrorMessage").ValueKind != JsonValueKind.True;
    }

    private static string VisibleText(JsonElement message, string type)
    {
        var blocks = Property(message, "content");
        return blocks.ValueKind == JsonValueKind.Array
            ? string.Join("\n\n", blocks.EnumerateArray().Where(block => String(block, "type") == type)
                .Select(block => String(block, "text")).Where(text => !string.IsNullOrWhiteSpace(text)))
            : "";
    }

    private static AgentReply? ClipReply(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        var clipped = text.Length > SummaryLimit;
        if (clipped)
        {
            var length = char.IsHighSurrogate(text[SummaryLimit - 1]) ? SummaryLimit - 1 : SummaryLimit;
            text = text[..length];
        }
        return new(text, clipped);
    }
}
