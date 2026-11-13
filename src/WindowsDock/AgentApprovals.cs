using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsDock;

internal sealed record AgentApproval(Guid Id, AgentProvider Provider, string SessionId, string Project,
    string Tool, string Details, string? WorkingDirectory, DateTimeOffset Expires);

internal static class AgentApprovals
{
    private static string DirectoryPath => Path.Combine(AgentSessions.StatusDirectory, "Approvals");
    private static string PathFor(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".json");
    private static string PipeFor(Guid id) => "WindowsDock.Approval." + id.ToString("N");

    internal static void Offer(AgentProvider provider, string sessionId, string project, JsonElement input, TextWriter output)
    {
        var tool = AgentSessions.String(input, "tool_name");
        if (string.IsNullOrWhiteSpace(tool) || tool is "AskUserQuestion" or "ExitPlanMode" or "request_permissions" ||
            !input.TryGetProperty("tool_input", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
            return;

        // Display the exact request. Leave larger or unsupported requests with the provider.
        var details = JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true });
        if (details.Length > 8192)
            return;

        var id = Guid.NewGuid();
        Directory.CreateDirectory(DirectoryPath);
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1))
                try { File.Delete(file); } catch (IOException) { }
        using var pipe = new NamedPipeServerStream(PipeFor(id), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new AgentApproval(id, provider, sessionId, project, tool, details,
            AgentSessions.String(input, "cwd"), DateTimeOffset.UtcNow.AddSeconds(30));
        var path = PathFor(id);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(request));
            File.Move(temp, path);
            pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            var decision = new byte[1];
            if (pipe.ReadAsync(decision.AsMemory(), timeout.Token).AsTask().GetAwaiter().GetResult() != 1 ||
                DateTimeOffset.UtcNow >= request.Expires || decision[0] is not (1 or 2))
                return;
            var behavior = decision[0] == 1 ? "allow" : "deny";
            output.WriteLine(JsonSerializer.Serialize(new
            {
                hookSpecificOutput = new { hookEventName = "PermissionRequest", decision = new { behavior } }
            }));
            output.Flush();
            pipe.WriteByte(decision[0]);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            DeleteRequest(temp);
            DeleteRequest(path);
        }
    }

    private static void DeleteRequest(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static AgentApproval[] Read()
    {
        if (!Directory.Exists(DirectoryPath))
            return [];
        var now = DateTimeOffset.UtcNow;
        var result = new List<AgentApproval>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json").Take(100))
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 32_768)
                        continue;
                    var request = JsonSerializer.Deserialize<AgentApproval>(stream);
                    if (request != null && request.Expires > now && request.Expires <= now.AddSeconds(35) &&
                        request.Id.ToString("N") == Path.GetFileNameWithoutExtension(path) &&
                        Enum.IsDefined(request.Provider) && !string.IsNullOrWhiteSpace(request.Tool) &&
                        !string.IsNullOrWhiteSpace(request.Details))
                        result.Add(request);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return result.OrderBy(request => request.Expires).ToArray();
    }

    internal static async Task<bool> DecideAsync(AgentApproval request, bool allow)
    {
        if (request.Expires <= DateTimeOffset.UtcNow)
            return false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var pipe = new NamedPipeClientStream(".", PipeFor(request.Id), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            var decision = allow ? (byte)1 : (byte)2;
            await pipe.WriteAsync(new[] { decision }, timeout.Token);
            var ack = new byte[1];
            return await pipe.ReadAsync(ack.AsMemory(), timeout.Token) == 1 && ack[0] == decision;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
