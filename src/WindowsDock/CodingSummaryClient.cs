using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal sealed class CodingSummaryException(string message) : Exception(message);

internal static class CodingSummaryClient
{
    private static readonly JsonSerializerOptions InputJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] CodexOptions =
    [
        "--ask-for-approval", "never", "--disable", "shell_tool", "--disable", "unified_exec",
        "--disable", "hooks", "--disable", "plugins", "--disable", "remote_plugin",
        "--disable", "apps", "--disable", "multi_agent", "--disable", "memories", "--disable", "view_image",
        "--config", "web_search=\"disabled\"", "--config", "project_doc_max_bytes=0",
        "exec", "--json", "--sandbox", "read-only", "--skip-git-repo-check", "--ephemeral",
        "--ignore-user-config", "--ignore-rules"
    ];
    private static readonly string[] ClaudeOptions =
    [
        "--print", "--output-format", "json", "--no-session-persistence", "--max-turns", "1",
        "--tools", "", "--disallowedTools", "*", "--permission-mode", "dontAsk",
        "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--setting-sources", "",
        "--settings", "{\"disableAllHooks\":true,\"disableClaudeAiConnectors\":true,\"autoMemoryEnabled\":false}"
    ];

    internal static string? FindExecutable(AssistantProvider provider)
    {
        if (provider == AssistantProvider.CodexCli)
            return CodexUsageClient.FindExecutable();
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        return File.Exists(installed) ? installed : CodexUsageClient.FindOnPath("claude.exe");
    }

    internal static async Task<string> SummarizeAsync(AssistantProvider provider, string model, string instruction, string text,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var codex = provider == AssistantProvider.CodexCli;
        var name = codex ? "Codex" : "Claude Code";
        var executable = FindExecutable(provider) ??
            throw new CodingSummaryException($"Install the {name} CLI and sign in to use it here.");
        var workingDirectory = Directory.CreateTempSubdirectory("windows-dock-summary-").FullName;
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var variable in new[] { "CODEX_THREAD_ID", "CODEX_API_KEY", "OPENAI_API_KEY", "CLAUDECODE",
                "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SIMPLE", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" })
                start.Environment.Remove(variable);
            foreach (var option in codex ? CodexOptions : ClaudeOptions)
                start.ArgumentList.Add(option);
            if (model.Length != 0)
                start.ArgumentList.Add("--model=" + model);
            const string noTools = "Return only the summary. Do not call tools or read files.";
            if (codex)
                start.ArgumentList.Add("-");
            else
            {
                start.ArgumentList.Add("--system-prompt");
                start.ArgumentList.Add(instruction + " " + noTools);
            }
            var source = JsonSerializer.Serialize(new { sourceText = text }, InputJson);
            var input = codex ? $"{instruction} {noTools}\n\nSource material:\n{source}" : source;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(75));
            using var process = Process.Start(start) ?? throw new CodingSummaryException($"Could not start {name}.");
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
                    await process.StandardInput.FlushAsync(timeout.Token);
                }
                catch (IOException) { }
                try { process.StandardInput.Close(); }
                catch (IOException) { }
                await process.WaitForExitAsync(timeout.Token);
                var json = await output;
                var diagnostics = await errors;
                if (process.ExitCode != 0)
                    throw new CodingSummaryException(ExplainFailure(name, json + "\n" + diagnostics));
                return codex ? ReadCodex(json) : ReadClaude(json);
            }
            finally
            {
                if (!process.HasExited)
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            }
        }
        catch (Win32Exception)
        {
            throw new CodingSummaryException($"Could not start {name}. Check the local CLI installation.");
        }
        finally
        {
            try { Directory.Delete(workingDirectory); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string ReadCodex(string output)
    {
        using var reader = new StringReader(output);
        var answer = "";
        var complete = false;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var entry = JsonDocument.Parse(line);
            var root = entry.RootElement;
            switch (String(root, "type"))
            {
                case "item.completed":
                    var item = Property(root, "item");
                    switch (String(item, "type"))
                    {
                        case "agent_message": answer = String(item, "text") ?? ""; break;
                        case "command_execution" or "file_change" or "mcp_tool_call" or "web_search":
                            throw new CodingSummaryException("Codex tried to use a tool. Try a different summary provider.");
                    }
                    break;
                case "turn.completed": complete = true; break;
                case "turn.failed": throw new CodingSummaryException(ExplainFailure("Codex", line));
            }
        }
        return complete ? answer : "";
    }

    private static string ReadClaude(string output)
    {
        using var result = JsonDocument.Parse(output);
        var root = result.RootElement;
        if (Property(root, "is_error").ValueKind == JsonValueKind.True)
            throw new CodingSummaryException(ExplainFailure("Claude Code", output));
        return String(root, "type") == "result" ? String(root, "result") ?? "" : "";
    }

    private static string ExplainFailure(string name, string diagnostic)
    {
        if (diagnostic.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return $"Sign in to the {name} CLI, then try again.";
        if (diagnostic.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("usage limit", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return $"{name} has reached a usage limit. Choose another provider or try again later.";
        if (diagnostic.Contains("unexpected argument", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("unknown option", StringComparison.OrdinalIgnoreCase))
            return $"Update the {name} CLI and try again.";
        return $"{name} could not summarize this text. Check the CLI sign-in and model, or try another provider.";
    }
}
