using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WindowsDock;

internal enum AgentProvider { Claude, Codex }
internal enum AgentState { Recent, Ready, Working, Attention, Finished, Failed, Interrupted, QuotaReached }

internal sealed record AgentSession(AgentProvider Provider, string Id, string Title, string Project,
    AgentState State, string Step, DateTimeOffset Updated, string? DesktopId = null,
    string? Model = null, string? Effort = null, string? LastReplySessionId = null, string? LastReplyId = null,
    string? ProjectPath = null);

internal static partial class AgentSessions
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    internal static string StatusDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsDock", "AgentStatus");

    internal static string ConfigPath(AgentProvider provider) => provider == AgentProvider.Claude
        ? Path.Combine(Profile, ".claude", "settings.json")
        : Path.Combine(CodexHome, "hooks.json");

    internal static string CodexHome => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } path &&
        Path.IsPathFullyQualified(path) ? path : Path.Combine(Profile, ".codex");

    public static IReadOnlyList<AgentSession> Read()
    {
        var sessions = new Dictionary<(AgentProvider, string), AgentSession>();
        ReadClaude(sessions);
        ReadCodex(sessions);
        var now = DateTimeOffset.UtcNow;
        foreach (var path in Files(StatusDirectory, "*.json", false, 80))
        {
            TryRead(() =>
            {
                using var stream = Open(path);
                if (stream.Length > 16_384)
                    return;
                var record = JsonSerializer.Deserialize<AgentSession>(stream);
                if (record == null || !Enum.IsDefined(record.Provider) || !Enum.IsDefined(record.State) ||
                    string.IsNullOrEmpty(record.Id) || record.Updated < now.AddDays(-3) || record.Updated > now.AddMinutes(1))
                    return;
                var key = (record.Provider, record.Id);
                if (sessions.TryGetValue(key, out var known))
                {
                    var newer = record.Updated >= known.Updated || known.State == AgentState.Recent;
                    sessions[key] = known with
                    {
                        State = newer ? record.State : known.State,
                        Step = newer ? record.Step : known.Step,
                        Updated = newer ? record.Updated : known.Updated,
                        Model = newer ? record.Model ?? known.Model : known.Model,
                        Effort = newer ? record.Effort ?? known.Effort : known.Effort,
                        ProjectPath = known.ProjectPath ?? record.ProjectPath
                    };
                }
                else
                    sessions[key] = record;
            });
        }

        return sessions.Values.Select(session => Expire(session, now))
            .OrderBy(session => session.State switch
            {
                AgentState.Attention or AgentState.QuotaReached => 0, AgentState.Working => 1, _ => 2
            })
            .ThenByDescending(session => session.Updated).Take(48).ToArray();
    }

    private static AgentSession Expire(AgentSession session, DateTimeOffset now) =>
        (session.State == AgentState.Working && session.Updated < now.AddMinutes(-20)) ||
        (session.State == AgentState.Attention && session.Updated < now.AddHours(-2))
            ? session with { State = AgentState.Recent, Step = "Last status expired" } : session;

    private static void ReadClaude(Dictionary<(AgentProvider, string), AgentSession> sessions)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude", "claude-code-sessions");
        foreach (var path in Files(root, "local_*.json", true, 24))
        {
            TryRead(() =>
            {
                using var stream = Open(path);
                if (stream.Length > 8_000_000)
                    return;
                using var doc = JsonDocument.Parse(stream);
                var item = doc.RootElement;
                if (item.TryGetProperty("isArchived", out var archived) && archived.ValueKind == JsonValueKind.True)
                    return;
                var desktopId = String(item, "sessionId");
                var id = String(item, "cliSessionId") ?? desktopId;
                if (string.IsNullOrWhiteSpace(id))
                    return;
                var project = ProjectName(String(item, "cwd"));
                var title = Clip(String(item, "title"), project);
                var updated = item.TryGetProperty("lastActivityAt", out var activity) && activity.ValueKind == JsonValueKind.Number &&
                    activity.TryGetInt64(out var ms) && ms is > 0 and < 253402300799999
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : File.GetLastWriteTimeUtc(path);
                var summary = Property(item, "postTurnSummary");
                var lastReply = String(item, "lastAssistantUuid");
                var finished = lastReply != null && lastReply == String(summary, "summarizes_uuid") &&
                    String(summary, "status_category") is "completed" or "review_ready";
                sessions[(AgentProvider.Claude, id)] = new(AgentProvider.Claude, id, title, project,
                    finished ? AgentState.Finished : AgentState.Recent, finished ? "Reply ready" : "Desktop chat",
                    updated, desktopId, String(item, "model"), String(item, "effort"),
                    String(item, "lastAssistantCliSessionId"), lastReply, String(item, "cwd"));
            });
        }
    }

    private static void ReadCodex(Dictionary<(AgentProvider, string), AgentSession> sessions)
    {
        var titles = new Dictionary<string, AgentSession>(StringComparer.Ordinal);
        var index = Path.Combine(CodexHome, "session_index.jsonl");
        TryRead(() =>
        {
            foreach (var line in Tail(index, 192_000))
            {
                TryRead(() =>
                {
                    using var doc = JsonDocument.Parse(line);
                    var item = doc.RootElement;
                    var id = String(item, "id");
                    if (string.IsNullOrWhiteSpace(id) || !DateTimeOffset.TryParse(String(item, "updated_at"), out var at))
                        return;
                    titles[id] = new(AgentProvider.Codex, id, Clip(String(item, "thread_name"), "Codex chat"),
                        "", AgentState.Recent, "Desktop chat", at);
                });
            }
        });
        foreach (var (id, session) in titles.OrderByDescending(pair => pair.Value.Updated).Take(24))
            sessions[(AgentProvider.Codex, id)] = session;

        foreach (var file in Files(Path.Combine(CodexHome, "sessions"), "rollout-*.jsonl", true, 12))
        {
            TryRead(() =>
            {
                // IDs in rollout filenames are UUIDs. Only correlate records already in the local desktop index.
                var basename = Path.GetFileNameWithoutExtension(file);
                if (basename.Length < 36)
                    return;
                var id = basename[^36..];
                var key = (AgentProvider.Codex, id);
                if (!Guid.TryParse(id, out _) || !sessions.TryGetValue(key, out var session))
                    return;

                AgentSession? latest = null;
                string? model = null;
                string? effort = null;
                string? cwd = null;
                foreach (var line in Tail(file, 768_000).Reverse())
                {
                    if (model == null && line.Contains("\"turn_context\"", StringComparison.Ordinal))
                    {
                        TryRead(() =>
                        {
                            using var doc = JsonDocument.Parse(line);
                            var item = doc.RootElement;
                            if (String(item, "type") == "turn_context" && item.TryGetProperty("payload", out var context))
                            {
                                model = String(context, "model");
                                effort = String(context, "effort") ?? String(context, "reasoning_effort");
                                cwd = String(context, "cwd");
                            }
                        });
                    }
                    if (latest != null && model != null)
                        break;
                    if (latest != null)
                        continue;
                    if (!line.Contains("\"event_msg\"", StringComparison.Ordinal) ||
                        !line.Contains("\"task_", StringComparison.Ordinal) &&
                        !line.Contains("\"turn_aborted\"", StringComparison.Ordinal) &&
                        !line.Contains("\"usage_limit_reached\"", StringComparison.Ordinal))
                        continue;
                    TryRead(() =>
                    {
                        using var doc = JsonDocument.Parse(line);
                        var item = doc.RootElement;
                        if (String(item, "type") != "event_msg" || !item.TryGetProperty("payload", out var payload) ||
                            !DateTimeOffset.TryParse(String(item, "timestamp"), out var at))
                            return;
                        var state = String(payload, "type") switch
                        {
                            "task_started" => AgentState.Working,
                            "task_complete" => AgentState.Finished,
                            "turn_aborted" => AgentState.Interrupted,
                            "error" when IsCodexQuotaError(payload) => AgentState.QuotaReached,
                            _ => AgentState.Recent
                        };
                        if (state != AgentState.Recent)
                            latest = session with
                            {
                                State = state, Updated = at,
                                Step = state switch
                                {
                                    AgentState.Working => "Turn in progress",
                                    AgentState.Finished => "Reply ready",
                                    AgentState.QuotaReached => "Usage limit reached",
                                    _ => "Turn stopped"
                                }
                            };
                    });
                }
                sessions[key] = (latest ?? session) with
                {
                    Model = model, Effort = effort,
                    Project = cwd == null ? session.Project : ProjectName(cwd), ProjectPath = cwd
                };
            });
        }
    }

    private static bool IsCodexQuotaError(JsonElement payload) =>
        String(payload, "codex_error_info") is "usage_limit_reached" or "UsageLimitReached" ||
        Property(Property(payload, "codex_error_info"), "usage_limit_reached").ValueKind != JsonValueKind.Undefined;

    internal static void Receive(string providerName, TextReader input, TextWriter output, bool approvals)
    {
        if (!Enum.TryParse<AgentProvider>(providerName, true, out var provider) || !Enum.IsDefined(provider))
            return;
        const int maxInput = 4_000_000;
        var buffer = new char[8192];
        var json = new StringBuilder();
        for (var count = input.Read(buffer); count > 0; count = input.Read(buffer))
        {
            if (json.Length + count > maxInput)
                return;
            json.Append(buffer, 0, count);
        }
        using var doc = JsonDocument.Parse(json.ToString());
        var item = doc.RootElement;
        var id = String(item, "session_id");
        var hook = String(item, "hook_event_name");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 160)
            return;
        var transition = hook switch
        {
            "SessionStart" => (AgentState.Ready, "Session opened"),
            "UserPromptSubmit" => (AgentState.Working, "Responding"),
            "PermissionRequest" => (AgentState.Attention, "Approval requested"),
            "Notification" when String(item, "notification_type") == "permission_prompt" => (AgentState.Attention, "Approval requested"),
            "PreToolUse" or "PostToolUse" or "PostToolUseFailure" => (AgentState.Working, ToolLabel(String(item, "tool_name"))),
            "Stop" => (AgentState.Finished, "Turn ended"),
            "StopFailure" => (AgentState.Failed, "Turn failed"),
            "Interrupt" => (AgentState.Interrupted, "Turn interrupted"),
            "SessionEnd" => (AgentState.Recent, "Session closed"),
            _ => ((AgentState State, string Step)?)null
        };
        if (transition is not { } status)
            return;

        var project = ProjectName(String(item, "cwd"));
        var record = new AgentSession(provider, id, project, project, status.Item1, status.Item2, DateTimeOffset.UtcNow,
            Model: String(item, "model"), ProjectPath: String(item, "cwd"));
        Directory.CreateDirectory(StatusDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider + ":" + id)));
        var path = Path.Combine(StatusDirectory, hash + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(record));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        if (hook is "SessionStart" or "UserPromptSubmit")
            foreach (var old in Files(StatusDirectory, "*.json", false, 200).Skip(80))
                TryRead(() => { if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-3)) File.Delete(old); });
        if (approvals && hook == "PermissionRequest")
            AgentApprovals.Offer(provider, id, project, item, output);
    }

    private static string ToolLabel(string? tool) => tool switch
    {
        "Edit" or "Write" or "apply_patch" or "MultiEdit" => "Editing files",
        "Read" or "Glob" or "Grep" => "Reading project",
        "Bash" or "PowerShell" => "Running a tool",
        "WebSearch" or "WebFetch" => "Looking up information",
        _ => "Using tools"
    };

    private static string ProjectName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Local session";
        return Clip(path.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault(), "Local session");
    }

    private static string Clip(string? text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback :
        new string(text.Trim().Take(140).Select(character => char.IsControl(character) ? ' ' : character).ToArray());

    internal static string? String(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    internal static JsonElement Property(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) ? value : default;

    internal static long? Count(JsonElement item, string key) =>
        Property(item, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var count) && count >= 0
            ? count : null;

    private static IEnumerable<string> Files(string directory, string pattern, bool recursive, int limit)
    {
        try
        {
            if (!Directory.Exists(directory))
                return [];
            var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 6 };
            return new DirectoryInfo(directory).EnumerateFiles(pattern, options).OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(limit).Select(file => file.FullName).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static string[] Tail(string path, int bytes)
    {
        using var stream = Open(path);
        var offset = Math.Max(0, stream.Length - bytes);
        stream.Position = offset;
        var buffer = new byte[Math.Min(bytes, stream.Length)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, false);
        return Encoding.UTF8.GetString(buffer, 0, read).Split('\n').Skip(offset > 0 ? 1 : 0).ToArray();
    }

    private static void TryRead(Action read)
    {
        try { read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
}
