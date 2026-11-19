using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

public sealed class CodingSessionTests
{
    private const string ThreadId = "10000000-0000-4000-8000-000000000001";

    [Fact]
    public async Task IdleChatReceivesTheFollowUpWithItsSelectedModel()
    {
        await using var server = new LocalServer();
        await CodingSessionControls.SendAsync(server.Connection, ThreadId, null, "Review the draft", "test-model", "high", server.Token);
        var sent = Assert.Single(server.Calls, call => call.Method == "turn/start").Parameters;
        Assert.Equal(ThreadId, sent.GetProperty("threadId").GetString());
        Assert.Equal("test-model", sent.GetProperty("model").GetString());
        Assert.Equal("high", sent.GetProperty("effort").GetString());
        Assert.Equal("Review the draft", sent.GetProperty("input")[0].GetProperty("text").GetString());
        Assert.DoesNotContain(server.Calls, call => call.Method is "thread/resume" or "thread/fork");
    }

    [Fact]
    public async Task SteeringTargetsTheObservedTurnWithoutChangingItsSettings()
    {
        await using var server = new LocalServer { Status = "active", ActiveTurn = "turn-1" };
        await CodingSessionControls.SendAsync(server.Connection, ThreadId, "turn-1", "Focus on the parser", "test-model", "high", server.Token);
        var sent = Assert.Single(server.Calls, call => call.Method == "turn/steer").Parameters;
        Assert.Equal("turn-1", sent.GetProperty("expectedTurnId").GetString());
        Assert.False(sent.TryGetProperty("model", out _));
        Assert.False(sent.TryGetProperty("effort", out _));
        Assert.DoesNotContain(server.Calls, call => call.Method == "turn/start");
    }

    [Theory]
    [InlineData("idle", null, "thread/settings/update")]
    [InlineData("active", "turn-1", "turn/settings/update")]
    public async Task ModelChangesUseTheAppropriateSettingsScope(string status, string? turn, string method)
    {
        await using var server = new LocalServer { Status = status, ActiveTurn = turn };
        await CodingSessionControls.ApplyAsync(server.Connection, ThreadId, turn, "test-model", "medium", server.Token);
        var sent = Assert.Single(server.Calls, call => call.Method == method).Parameters;
        Assert.Equal("test-model", sent.GetProperty("model").GetString());
        Assert.Equal("medium", sent.GetProperty("effort").GetString());
        Assert.Equal(turn != null, sent.TryGetProperty("turnId", out var receivedTurn));
        if (turn != null)
            Assert.Equal(turn, receivedTurn.GetString());
    }

    [Fact]
    public async Task AChangedTurnRequiresAnotherRefresh()
    {
        await using var server = new LocalServer { Status = "active", ActiveTurn = "turn-2" };
        await Assert.ThrowsAsync<CodingControlException>(() => CodingSessionControls.SendAsync(
            server.Connection, ThreadId, "turn-1", "A stale follow-up", "", "", server.Token));
        await Assert.ThrowsAsync<CodingControlException>(() => CodingSessionControls.StopAsync(
            server.Connection, ThreadId, "turn-1", server.Token));
        Assert.DoesNotContain(server.Calls, call => call.Method.StartsWith("turn/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOlderServerCanShowStatusWithoutEnablingAnUnidentifiedTurn()
    {
        await using var server = new LocalServer { Status = "active", MissingTurnList = true };
        var state = await CodingSessionControls.InspectAsync(server.Connection, ThreadId, server.Token);
        Assert.Equal("active", state.Status);
        Assert.False(state.CanSend);
        Assert.Null(state.ActiveTurn);
    }

    [Fact]
    public async Task RequestsMustConfirmTheSelectedChatIdentity()
    {
        await using var server = new LocalServer { ReportedThread = "another-chat" };
        await Assert.ThrowsAsync<CodingControlException>(() => CodingSessionControls.ApplyAsync(
            server.Connection, ThreadId, null, "test-model", "", server.Token));
        Assert.DoesNotContain(server.Calls, call => call.Method.Contains("update", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ws://example.test:4500/")]
    [InlineData("ws://127.0.0.1:4500/?token=secret")]
    [InlineData("ws://user:secret@127.0.0.1:4500/")]
    [InlineData("https://127.0.0.1:4500/")]
    public void ConnectionSettingsRejectUnexpectedEndpoints(string address) =>
        Assert.Throws<ArgumentException>(() => CodingConnection.Parse(address, ""));

    private sealed record Call(string Method, JsonElement Parameters);

    private sealed class LocalServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(20));
        private readonly List<Task> connections = [];
        private readonly Task accept;
        internal ConcurrentQueue<Call> Calls { get; } = new();
        internal CodingConnection Connection { get; }
        internal CancellationToken Token => lifetime.Token;
        internal string Status { get; init; } = "idle";
        internal string? ActiveTurn { get; init; }
        internal string ReportedThread { get; init; } = ThreadId;
        internal bool MissingTurnList { get; init; }

        internal LocalServer()
        {
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            var number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            listener.Prefixes.Add($"http://127.0.0.1:{number}/");
            listener.Start();
            Connection = CodingConnection.Parse($"ws://127.0.0.1:{number}/", "");
            accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    var context = await listener.GetContextAsync().WaitAsync(Token);
                    connections.Add(ServeAsync(context));
                }
            }
            catch (Exception error) when (error is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
        }

        private async Task ServeAsync(HttpListenerContext context)
        {
            var accepted = await context.AcceptWebSocketAsync(null);
            using var socket = accepted.WebSocket;
            var buffer = new byte[32 * 1024];
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(buffer.AsMemory(), Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        return;
                    Assert.True(received.EndOfMessage);
                    using var parsed = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                    var request = parsed.RootElement;
                    var method = request.GetProperty("method").GetString()!;
                    if (!request.TryGetProperty("id", out var id))
                        continue;
                    var parameters = request.TryGetProperty("params", out var supplied) ? supplied.Clone() : default;
                    Calls.Enqueue(new(method, parameters));
                    object result = method switch
                    {
                        "initialize" => new { userAgent = "test" },
                        "thread/read" => new { thread = new { id = ReportedThread, status = new { type = Status, activeFlags = Array.Empty<string>() } } },
                        "thread/turns/list" => new { data = ActiveTurn == null ? Array.Empty<object>() : new object[] { new { id = ActiveTurn, status = "inProgress" } } },
                        _ => new { }
                    };
                    var message = method == "thread/turns/list" && MissingTurnList
                        ? JsonSerializer.SerializeToUtf8Bytes(new { id, error = new { code = -32601, message = "Method not found" } })
                        : JsonSerializer.SerializeToUtf8Bytes(new { id, result });
                    await socket.SendAsync(message.AsMemory(), WebSocketMessageType.Text, true, Token);
                }
            }
            catch (Exception error) when (error is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            listener.Close();
            await accept;
            await Task.WhenAll(connections);
            lifetime.Dispose();
        }
    }
}
