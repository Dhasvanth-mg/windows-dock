using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal sealed class CodingControlException(string message, long? code = null) : Exception(message)
{
    internal long? Code { get; } = code;
}

internal sealed class CodingControlServer : IDisposable
{
    private const int MessageLimit = 6 * 1024 * 1024;
    private static readonly HttpMessageInvoker LocalHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false
    });
    private readonly ClientWebSocket? socket;
    private readonly Process? process;
    private readonly Task? stderr;
    private readonly byte[] buffer = new byte[8192];
    private int nextId;

    private CodingControlServer(ClientWebSocket socket) => this.socket = socket;

    private CodingControlServer(Process process)
    {
        this.process = process;
        stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    }

    internal static async Task<CodingControlServer> ConnectAsync(CodingConnection connection, CancellationToken token)
    {
        var ws = new ClientWebSocket();
        if (connection.Bearer.Length != 0)
            ws.Options.SetRequestHeader("Authorization", "Bearer " + connection.Bearer);
        var server = new CodingControlServer(ws);
        try
        {
            await ws.ConnectAsync(connection.Address, LocalHttp, token).ConfigureAwait(false);
            await server.InitializeAsync(token).ConfigureAwait(false);
            return server;
        }
        catch (WebSocketException)
        {
            server.Dispose();
            throw new CodingControlException("Couldn't connect to the shared Codex app server. Check its address and token.");
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    internal static CodingControlServer StartCopyServer()
    {
        var path = CodexUsageClient.FindExecutable() ?? throw new CodingControlException("Install the Codex CLI to continue a copy.");
        var start = new ProcessStartInfo(path)
        {
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var variable in new[] { "CODEX_THREAD_ID", "CODEX_API_KEY", "OPENAI_API_KEY" })
            start.Environment.Remove(variable);
        foreach (var option in new[] { "app-server", "--stdio", "--disable", "shell_tool", "--disable", "unified_exec",
            "--disable", "hooks", "--disable", "plugins", "--disable", "apps", "--disable", "code_mode",
            "--disable", "multi_agent", "--disable", "memories", "--config", "web_search=\"disabled\"",
            "--config", "mcp_servers={}", "--config", "project_doc_max_bytes=0" })
            start.ArgumentList.Add(option);
        try { return new(Process.Start(start) ?? throw new CodingControlException("Couldn't start the Codex CLI.")); }
        catch (Win32Exception) { throw new CodingControlException("Couldn't start the Codex CLI. Check its installation."); }
    }

    internal async Task InitializeAsync(CancellationToken token)
    {
        await CallAsync("initialize", new
        {
            clientInfo = new { name = "windows-dock", version = "0.1.0" },
            capabilities = new { experimentalApi = true }
        }, token).ConfigureAwait(false);
        await SendAsync(new { method = "initialized" }, token).ConfigureAwait(false);
    }

    internal async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken token,
        Action<JsonElement>? observe = null)
    {
        var id = ++nextId;
        await SendAsync(new { method, id, @params = parameters }, token).ConfigureAwait(false);
        while (true)
        {
            using var message = await ReceiveAsync(token).ConfigureAwait(false);
            var value = message.RootElement;
            if (String(value, "method") != null)
            {
                observe?.Invoke(value);
                await RejectCopyRequestAsync(value, token).ConfigureAwait(false);
                continue;
            }
            if (Count(value, "id") != id)
                continue;
            var error = Property(value, "error");
            if (error.ValueKind == JsonValueKind.Object)
                throw RequestError(method, error);
            return Property(value, "result").Clone();
        }
    }

    internal async Task ObserveAsync(Action<JsonElement> observe, CancellationToken token)
    {
        using var message = await ReceiveAsync(token).ConfigureAwait(false);
        observe(message.RootElement);
        await RejectCopyRequestAsync(message.RootElement, token).ConfigureAwait(false);
    }

    private async ValueTask RejectCopyRequestAsync(JsonElement value, CancellationToken token)
    {
        var id = Property(value, "id");
        if (process != null && String(value, "method") != null && id.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            await SendAsync(new { id, error = new { code = -32601, message = "This dock copy does not handle tool requests." } }, token).ConfigureAwait(false);
    }

    private async ValueTask SendAsync(object value, CancellationToken token)
    {
        try
        {
            if (socket != null)
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value).AsMemory(), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            else
            {
                await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or WebSocketException)
        { throw new CodingControlException("The Codex connection closed. Check the chat before sending again."); }
    }

    private async Task<JsonDocument> ReceiveAsync(CancellationToken token)
    {
        try
        {
            if (socket == null)
            {
                var line = await process!.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null)
                    throw new CodingControlException("The Codex CLI closed. Check its sign-in and local session availability.");
                if (line.Length > MessageLimit)
                    throw new CodingControlException("Codex returned more data than this view can open.");
                return JsonDocument.Parse(line);
            }
            using var bytes = new MemoryStream();
            ValueWebSocketReceiveResult block;
            do
            {
                block = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (block.MessageType != WebSocketMessageType.Text)
                    throw new CodingControlException("The Codex connection closed or returned an unexpected message.");
                if (bytes.Length + block.Count > MessageLimit)
                    throw new CodingControlException("Codex returned more data than this view can open.");
                bytes.Write(buffer, 0, block.Count);
            } while (!block.EndOfMessage);
            return JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length));
        }
        catch (JsonException) { throw new CodingControlException("Codex returned an unreadable response."); }
        catch (Exception error) when (error is IOException or WebSocketException)
        { throw new CodingControlException("The Codex connection closed. Check the chat before sending again."); }
    }

    private static CodingControlException RequestError(string method, JsonElement error)
    {
        var rawCode = Property(error, "code");
        var code = rawCode.ValueKind == JsonValueKind.Number && rawCode.TryGetInt64(out var number) ? number : (long?)null;
        if (code == -32601)
            return new("This app-server version doesn't support that control.", code);
        var detail = String(error, "message") ?? "";
        if (detail.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || detail.Contains("usage limit", StringComparison.OrdinalIgnoreCase))
            return new("Codex has reached a usage limit.", code);
        return new(method == "thread/read" ? "This chat isn't available on the connected app server." :
            "Codex declined the action. Refresh the chat and check the model or thinking choices.", code);
    }

    public void Dispose()
    {
        socket?.Dispose();
        if (process == null)
            return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
        process.Dispose();
        _ = stderr?.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
