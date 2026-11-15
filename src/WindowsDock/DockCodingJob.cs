using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsDock;

internal static class DockCodingJob
{
    internal static bool IsWorkspace(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(path))
            return false;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetPathRoot(full)?.Length == full.Length || ProtectedFolders().Any(folder =>
                string.Equals(full, folder, StringComparison.OrdinalIgnoreCase)))
            return false;
        return Directory.Exists(Path.Combine(full, ".git")) || File.Exists(Path.Combine(full, ".git"));
    }

    // A drive root, the user profile, and system folders are never a project, even when they hold a .git entry.
    private static IEnumerable<string> ProtectedFolders() => new[]
    {
        Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Windows, Environment.SpecialFolder.System,
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.CommonApplicationData
    }.Select(Environment.GetFolderPath).Where(folder => folder.Length > 0).Select(Path.TrimEndingDirectorySeparator);

    /// <summary>True for environment variables that commonly hold credentials. Coding jobs never inherit them.</summary>
    internal static bool IsSecretVariable(string name) =>
        name is "CODEX_THREAD_ID" or "CLAUDECODE" ||
        new[] { "TOKEN", "SECRET", "PASSWORD", "PASSWD", "CREDENTIAL", "COOKIE", "PRIVATE_KEY", "CONNECTION_STRING" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)) ||
        new[] { "_KEY", "_PAT", "_AUTH" }.Any(end => name.EndsWith(end, StringComparison.OrdinalIgnoreCase)) ||
        new[] { "AWS_", "AZURE_", "GOOGLE_APPLICATION_CREDENTIALS" }
            .Any(start => name.StartsWith(start, StringComparison.OrdinalIgnoreCase));

    internal static async Task<string> RunAsync(string workspace, string task, CancellationToken cancellation)
    {
        if (!IsWorkspace(workspace))
            throw new ArgumentException("Choose the root folder of a local Git workspace.");
        var executable = CodingExecutable();
        if (executable == null)
            return await DockWorkspaceEditor.RunAsync(workspace, task, cancellation);
        try { return await RunShellAsync(executable, workspace, task, cancellation); }
        catch (InvalidOperationException error) when (error.Message.StartsWith("Workspace shell unavailable:", StringComparison.Ordinal))
        {
            return await DockWorkspaceEditor.RunAsync(workspace, task, cancellation);
        }
    }

    private static async Task<string> RunShellAsync(string executable, string workspace, string task, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var flag in new[]
        {
            "--ask-for-approval", "never", "--disable", "hooks", "--disable", "plugins", "--disable", "remote_plugin",
            "--disable", "apps", "--disable", "multi_agent", "--disable", "memories", "--disable", "unified_exec",
            "-c", "web_search=\"disabled\"", "-c", "sandbox_workspace_write.network_access=false",
            "-c", "windows.sandbox=\"unelevated\"",
            "exec", "--sandbox", "workspace-write", "--json", "--ephemeral", "--ignore-user-config", "-"
        })
            start.ArgumentList.Add(flag);
        foreach (var variable in start.Environment.Keys.Where(IsSecretVariable).ToArray())
            start.Environment.Remove(variable);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0");
        start.Environment.TryGetValue("PATH", out var environmentPath);
        var localPath = environmentPath?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !path.Contains("\\WindowsApps", StringComparison.OrdinalIgnoreCase)) ?? [];
        start.Environment["PATH"] = string.Join(Path.PathSeparator, localPath.Prepend(powershell));

        cancellation.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(40));
        var token = timeout.Token;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the coding CLI.");
        try
        {
            var output = ReadOutputAsync(process.StandardOutput, token);
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, token);
            const string instruction = """
                Work only inside the selected project. Follow the project's instructions. Make the requested change and verify it.
                Leave changes in the working tree for the user to review. Do not stage, commit, publish, change permissions,
                install software, call external services, or modify files outside the selected workspace.
                Report changed files, checks run, and anything that remains. Use the owner's attribution rules.

                Task:
                """;
            try { await process.StandardInput.WriteAsync((instruction + "\n" + task).AsMemory(), token); }
            catch (IOException) { }
            try { process.StandardInput.Close(); }
            catch (IOException) { }
            await process.WaitForExitAsync(token);
            var (reply, failure, complete, usedShell) = await output;
            await errors;
            if (!usedShell && failure?.Contains("no package identity", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("Workspace shell unavailable: " + failure);
            if (process.ExitCode != 0 || !complete || failure != null)
                throw new InvalidOperationException(failure ?? "The coding CLI did not finish. Check its sign-in, quota, and workspace sandbox.");
            return string.IsNullOrWhiteSpace(reply) ? "The coding run finished. Review the workspace diff for changes." : reply;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("The coding job reached its 40-minute limit. Check the workspace for partial changes.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            }
        }
    }

    private static string? CodingExecutable()
    {
        var configured = DockData.Read<string>("coding-cli.json");
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64",
            "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        var onPath = CodexUsageClient.FindOnPath("codex.exe");
        return new[] { configured, installed, onPath }.FirstOrDefault(path =>
            path is { Length: > 0 } && Path.IsPathFullyQualified(path) &&
            !path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
    }

    private static async Task<(string? Reply, string? Failure, bool Complete, bool UsedShell)> ReadOutputAsync(StreamReader output, CancellationToken token)
    {
        string? last = null, failure = null;
        var complete = false;
        var usedShell = false;
        while (await output.ReadLineAsync(token) is { } line)
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                switch (AgentSessions.String(root, "type"))
                {
                    case "item.completed":
                        if (root.TryGetProperty("item", out var item))
                        {
                            var itemType = AgentSessions.String(item, "type");
                            if (itemType == "agent_message" && AgentSessions.String(item, "text") is { } text)
                                last = text[..Math.Min(text.Length, 48_000)];
                            else if (itemType == "command_execution")
                            {
                                var exited = AgentSessions.Count(item, "exit_code");
                                var failed = AgentSessions.String(item, "status") == "failed" || (exited.HasValue && exited != 0);
                                failure = failed ? AgentSessions.String(item, "aggregated_output") ?? "The workspace command failed." : null;
                                usedShell |= exited.HasValue && failure?.Contains("no package identity", StringComparison.OrdinalIgnoreCase) != true;
                            }
                            else if (itemType == "file_change")
                                usedShell = true;
                        }
                        break;
                    case "turn.completed": complete = true; break;
                    case "turn.failed":
                        if (root.TryGetProperty("error", out var error))
                            failure = AgentSessions.String(error, "message") ?? failure;
                        break;
                    case "error": failure = AgentSessions.String(root, "message") ?? failure; break;
                }
            }
            catch (JsonException) { }
        }
        return (last, failure is { } info ? info[..Math.Min(info.Length, 600)] : null, complete, usedShell);
    }
}
