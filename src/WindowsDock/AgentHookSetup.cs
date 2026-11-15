using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsDock;

internal static class AgentHookSetup
{
    private const string Marker = "--agent-status ";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] CommonEvents =
        ["SessionStart", "SessionEnd", "UserPromptSubmit", "PermissionRequest", "PreToolUse", "PostToolUse", "Stop"];

    public static bool IsInstalled(AgentProvider provider, bool approvals = false)
    {
        try
        {
            var config = ReadConfig(AgentSessions.ConfigPath(provider));
            return config["hooks"] is JsonObject events &&
                events.Any(item => item.Value is JsonArray groups && groups.Any(group =>
                    group is JsonObject value && value["hooks"] is JsonArray handlers &&
                    handlers.Any(handler => IsOurHandler(handler, provider) &&
                        (!approvals || (provider == AgentProvider.Codex
                            ? DecodeCommand(handler!["command"]!.GetValue<string>()).Contains("--approval", StringComparison.Ordinal)
                            : handler!["args"] is JsonArray { Count: 3 })))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    public static void SetInstalled(AgentProvider provider, bool install, bool approvals = false)
    {
        var path = AgentSessions.ConfigPath(provider);
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        var config = ParseConfig(original);
        if (config["hooks"] is not JsonObject events)
        {
            if (config["hooks"] != null)
                throw new InvalidOperationException("The provider’s hooks setting is not an object.");
            if (!install)
                return;
            events = new JsonObject();
            config["hooks"] = events;
        }

        var changed = RemoveOurHandlers(events, provider);
        if (install)
        {
            // BaseDirectory also works for single-file publishes, where Assembly.Location is empty.
            var executable = Path.Combine(AppContext.BaseDirectory, "WindowsDock.exe");
            if (!File.Exists(executable))
                throw new InvalidOperationException("The Windows Dock executable could not be found.");
            var arguments = Marker + provider.ToString().ToLowerInvariant();
            var eventsToAdd = provider == AgentProvider.Claude ? CommonEvents.Append("StopFailure") : CommonEvents.Append("Interrupt");
            foreach (var name in eventsToAdd)
            {
                if (events[name] is not JsonArray groups)
                {
                    if (events[name] != null)
                        throw new InvalidOperationException($"The existing {name} hooks have an unsupported format.");
                    groups = new JsonArray();
                    events[name] = groups;
                }
                var relayApproval = approvals && name == "PermissionRequest";
                // A cold PowerShell start (Codex) can take over two seconds, so status events get a larger budget.
                var handler = new JsonObject { ["type"] = "command", ["timeout"] = relayApproval ? 34 : 5 };
                if (provider == AgentProvider.Claude)
                {
                    handler["command"] = executable;
                    handler["args"] = relayApproval
                        ? new JsonArray("--agent-status", "claude", "--approval") : new JsonArray("--agent-status", "claude");
                }
                else
                {
                    var flag = relayApproval ? " --approval" : "";
                    var script = $$"""
                        $start = New-Object System.Diagnostics.ProcessStartInfo
                        $start.FileName = '{{executable.Replace("'", "''")}}'
                        $start.Arguments = '{{arguments.TrimStart()}}{{flag}}'
                        $start.UseShellExecute = $false
                        $start.CreateNoWindow = $true
                        $start.RedirectStandardInput = $true
                        $start.RedirectStandardOutput = $true
                        $process = [System.Diagnostics.Process]::Start($start)
                        try {
                            $process.StandardInput.Write([Console]::In.ReadToEnd())
                            $process.StandardInput.Close()
                            [Console]::Out.Write($process.StandardOutput.ReadToEnd())
                            $process.WaitForExit()
                        } finally {
                            if (!$process.HasExited) { $process.Kill() }
                            $process.Dispose()
                        }
                        """;
                    var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                    handler["command"] = $"powershell.exe -NoProfile -NonInteractive -EncodedCommand {encoded}";
                    handler["commandWindows"] = handler["command"]!.DeepClone();
                }
                groups.Add(new JsonObject { ["hooks"] = new JsonArray(handler) });
            }
            changed = true;
        }
        if (!changed)
            return;
        if (!install && events.Count == 0)
            config.Remove("hooks");

        EnsureRewritable(original);
        SaveConfig(path, original, config.ToJsonString(JsonOptions) + Environment.NewLine);
    }

    /// <summary>
    /// Rewriting a JSON config drops its comments. Refuse instead of removing them without telling the user.
    /// </summary>
    internal static void EnsureRewritable(string? original)
    {
        if (original == null)
            return;
        try
        {
            using var _ = JsonDocument.Parse(original, new JsonDocumentOptions
                { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "The provider settings contain comments that Windows Dock would remove. Remove them or edit the file by hand.");
        }
    }

    private const int BackupsKept = 3;

    internal static void SaveConfig(string path, string? original, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".windows-dock-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, contents);
            if (original == null)
                File.Move(temp, path);
            else
            {
                if (!string.Equals(original, File.ReadAllText(path), StringComparison.Ordinal))
                    throw new IOException("The provider settings changed. Try again.");
                var backup = path + ".windows-dock-" + Guid.NewGuid().ToString("N") + ".bak";
                File.Replace(temp, path, backup);
                DockData.KeepNewest(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".windows-dock-*.bak", BackupsKept);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static JsonObject ReadConfig(string path) => ParseConfig(File.Exists(path) ? File.ReadAllText(path) : null);

    internal static JsonObject ParseConfig(string? original) => original == null ? new JsonObject() :
        JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new InvalidOperationException("The provider settings are not a JSON object.");

    private static bool RemoveOurHandlers(JsonObject events, AgentProvider provider)
    {
        var changed = false;
        foreach (var name in events.Select(item => item.Key).ToArray())
        {
            if (events[name] is not JsonArray groups)
                continue;
            var changedEvent = false;
            for (var group = groups.Count - 1; group >= 0; group--)
            {
                if (groups[group] is not JsonObject value || value["hooks"] is not JsonArray handlers)
                    continue;
                var removed = false;
                for (var index = handlers.Count - 1; index >= 0; index--)
                {
                    if (!IsOurHandler(handlers[index], provider))
                        continue;
                    handlers.RemoveAt(index);
                    removed = true;
                }
                if (removed && handlers.Count == 0)
                    groups.RemoveAt(group);
                changedEvent |= removed;
            }
            if (changedEvent && groups.Count == 0)
                events.Remove(name);
            changed |= changedEvent;
        }
        return changed;
    }

    private static bool IsOurHandler(JsonNode? node, AgentProvider provider)
    {
        if (node is not JsonObject value || value["command"] is not JsonValue command ||
            !command.TryGetValue<string>(out var text))
            return false;
        text = DecodeCommand(text);
        if (!text.Contains("WindowsDock.exe", StringComparison.OrdinalIgnoreCase))
            return false;
        return provider == AgentProvider.Codex
            ? text.Contains(Marker + "codex", StringComparison.Ordinal)
            : value["args"] is JsonArray { Count: 2 or 3 } args &&
                args[0]?.ToString() == "--agent-status" && args[1]?.ToString() == "claude";
    }

    private static string DecodeCommand(string command)
    {
        const string prefix = "powershell.exe -NoProfile -NonInteractive -EncodedCommand ";
        if (!command.StartsWith(prefix, StringComparison.Ordinal))
            return command;
        try { return System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(command[prefix.Length..])); }
        catch (FormatException) { return command; }
    }
}
