using System.IO;
using System.Text.Json;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal sealed record AgentQuota(string Label, double? UsedPercent, DateTimeOffset? ResetAt);
internal sealed record AgentPlanUsage(DateTimeOffset Observed, AgentQuota[] Windows, long? DailyTokens = null,
    long? LifetimeTokens = null, bool? Allowed = null, string Source = "")
{
    internal bool IsBlocked(DateTimeOffset now)
    {
        if (Allowed != false || now - Observed > TimeSpan.FromMinutes(10))
            return false;
        var resets = Windows.Where(window => window.ResetAt.HasValue && window.UsedPercent >= 100).ToArray();
        return resets.Length == 0 || resets.Any(window => window.ResetAt > now);
    }
}

internal static class AgentUsage
{
    internal const string ClaudeUsageUrl = "https://claude.ai/settings/usage";
    internal const string CodexUsageUrl = "https://chatgpt.com/codex/settings/usage";

    internal static AgentPlanUsage? ReadClaudePlan()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude", "plan-usage-history.json");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4 * 1024 * 1024)
                return null;
            using var document = JsonDocument.Parse(stream);
            return ParseClaudePlan(document.RootElement, DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static AgentPlanUsage? ParseClaudePlan(JsonElement root, DateTimeOffset now)
    {
        var samples = Property(root, "samples");
        if (Count(root, "version") != 2 || samples.ValueKind != JsonValueKind.Array)
            return null;
        var latest = samples.EnumerateArray().Where(sample => Count(sample, "t") <= now.AddMinutes(2).ToUnixTimeMilliseconds())
            .DefaultIfEmpty().MaxBy(sample => Count(sample, "t"));
        if (Count(latest, "t") is not { } stamp)
            return null;
        var usage = Property(latest, "u");
        var windows = new List<AgentQuota>(3);
        AddWindow(windows, "5-hour", Property(usage, "fh"), Property(usage, "five_hour"));
        AddWindow(windows, "Weekly", Property(usage, "sd"), Property(usage, "seven_day"));
        return windows.Count == 0 ? null :
            new(DateTimeOffset.FromUnixTimeMilliseconds(stamp), windows.ToArray(), Source: "Claude Desktop cache");

        static void AddWindow(List<AgentQuota> list, string label, JsonElement cachedPercent, JsonElement window)
        {
            var percent = Percent(cachedPercent) ?? Percent(Property(window, "used_percentage"));
            var reset = UnixSeconds(Count(window, "resets_at"));
            if (percent.HasValue || reset.HasValue)
                list.Add(new(label, percent, reset));
        }
    }

    internal static AgentPlanUsage? ReadCodexCache()
    {
        var windows = new List<AgentQuota>();
        var buckets = new HashSet<string>(StringComparer.Ordinal);
        var seen = DateTimeOffset.MinValue;
        foreach (var file in RecentCodexRollouts())
        {
            try
            {
                foreach (var line in ReadTail(file).Reverse())
                {
                    if (!line.Contains("\"token_count\"", StringComparison.Ordinal) ||
                        !line.Contains("\"rate_limits\"", StringComparison.Ordinal))
                        continue;
                    using var doc = ParseTranscriptRecord(line);
                    if (doc == null || String(doc.RootElement, "type") != "event_msg")
                        continue;
                    var payload = Property(doc.RootElement, "payload");
                    var bucket = Property(payload, "rate_limits");
                    if (String(payload, "type") != "token_count" || bucket.ValueKind != JsonValueKind.Object ||
                        !DateTimeOffset.TryParse(String(doc.RootElement, "timestamp"), out var at))
                        continue;
                    var id = String(bucket, "limit_id") ?? "codex";
                    if (buckets.Contains(id))
                        continue;
                    var count = windows.Count;
                    AddCodexBucket(windows, bucket, id, local: true);
                    if (windows.Count > count)
                    {
                        buckets.Add(id);
                        if (at > seen)
                            seen = at;
                    }
                }
                if (windows.Count > 0)
                    break;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return windows.Count == 0 ? null : new(seen, windows.ToArray(), Source: "Local Codex activity");
    }

    internal static AgentPlanUsage? ParseCodexPlan(JsonElement limits, JsonElement usage, DateTimeOffset now)
    {
        var windows = new List<AgentQuota>();
        var buckets = Property(limits, "rateLimitsByLimitId");
        if (buckets.ValueKind == JsonValueKind.Object)
            foreach (var bucket in buckets.EnumerateObject().Take(20))
                AddCodexBucket(windows, bucket.Value, bucket.Name, local: false);
        if (windows.Count == 0)
        {
            var fallback = Property(limits, "rateLimits");
            AddCodexBucket(windows, fallback, String(fallback, "limitId") ?? "codex", local: false);
        }
        var daily = Property(usage, "dailyUsageBuckets");
        long? today = null;
        if (daily.ValueKind == JsonValueKind.Array)
        {
            var date = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            today = Count(daily.EnumerateArray().FirstOrDefault(bucket => String(bucket, "startDate") == date), "tokens");
        }
        var total = Count(Property(usage, "summary"), "lifetimeTokens");
        bool? allowed = Property(limits, "ordinaryUsageAllowed").ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
        return windows.Count == 0 && today == null && total == null && allowed == null ? null :
            new(now, windows.ToArray(), today, total, allowed, "Codex account");
    }

    private static void AddCodexBucket(List<AgentQuota> windows, JsonElement bucket, string id, bool local)
    {
        var title = id == "codex" ? "" : (String(bucket, local ? "limit_name" : "limitName") ?? id).Replace('_', ' ');
        foreach (var key in new[] { "primary", "secondary" })
        {
            var item = Property(bucket, key);
            var percent = Percent(Property(item, local ? "used_percent" : "usedPercent"));
            var reset = UnixSeconds(Count(item, local ? "resets_at" : "resetsAt"));
            if (!percent.HasValue && !reset.HasValue)
                continue;
            var minutes = Count(item, local ? "window_minutes" : "windowDurationMins");
            var label = minutes switch
            {
                10_080 => "Weekly",
                > 0 and < 60 => $"{minutes} min",
                > 0 and <= 1440 when minutes % 60 == 0 => $"{minutes / 60}-hour",
                > 0 when minutes % 1440 == 0 => $"{minutes / 1440}-day",
                _ => key == "primary" ? "Primary limit" : "Secondary limit"
            };
            windows.Add(new(title.Length == 0 ? label : title + " · " + label, percent, reset));
        }
    }

    private static double? Percent(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var percent) && percent is >= 0 and <= 100
            ? percent : null;

    private static DateTimeOffset? UnixSeconds(long? value) =>
        value is > 0 and < 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(value.Value) : null;
}
