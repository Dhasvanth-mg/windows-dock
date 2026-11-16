using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

public sealed class OllamaTextModelTests
{
    [Fact]
    public async Task SendsTheConversationToAnInstalledLocalModel()
    {
        using var client = Client(out var fixture);
        var ollama = new OllamaTextModel(client);
        Assert.Equal(new[] { "tiny:latest" }, await ollama.ModelsAsync(CancellationToken.None));

        var reply = await ollama.ChatAsync("tiny:latest", "Answer briefly.",
            [(true, "Question"), (false, "Previous reply"), (true, "Follow-up")], CancellationToken.None);

        Assert.Equal("Local answer", reply);
        Assert.Equal(new[] { "/api/tags", "/api/tags", "/api/chat" }, fixture.Paths);
        using var json = JsonDocument.Parse(fixture.ChatBody!);
        var root = json.RootElement;
        Assert.Equal("tiny:latest", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("30s", root.GetProperty("keep_alive").GetString());
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, root.GetProperty("messages")
            .EnumerateArray().Select(message => message.GetProperty("role").GetString()));
    }

    [Theory]
    [InlineData("alias:latest")]
    [InlineData("large:120b-cloud")]
    [InlineData("missing:latest")]
    public async Task RefusesUninstalledOrRemoteModelsBeforePostingText(string model)
    {
        using var client = Client(out var fixture);
        var ollama = new OllamaTextModel(client);

        await Assert.ThrowsAsync<ArgumentException>(() => ollama.ChatAsync(model, "", [(true, "Local content")], CancellationToken.None));

        Assert.Equal(new[] { "/api/tags" }, fixture.Paths);
        Assert.Null(fixture.ChatBody);
    }

    private static HttpClient Client(out OllamaFixture fixture)
    {
        fixture = new OllamaFixture();
        return new HttpClient(fixture) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
    }

    private sealed class OllamaFixture : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal string? ChatBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.RequestUri!.IsLoopback);
            Assert.Null(request.Headers.Authorization);
            var path = request.RequestUri.AbsolutePath;
            Paths.Add(path);
            string json;
            if (path == "/api/tags")
            {
                json = """
                    {"models":[{"model":"tiny:latest"},{"model":"large:120b-cloud"},
                    {"model":"alias:latest","remote_host":"https://example.com"}]}
                    """;
            }
            else
            {
                Assert.Equal("/api/chat", path);
                ChatBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                json = """{"message":{"role":"assistant","content":"Local answer"},"done":true}""";
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
