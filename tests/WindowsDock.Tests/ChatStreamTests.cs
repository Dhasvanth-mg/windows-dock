using System.IO;
using Xunit;

namespace WindowsDock.Tests;

public sealed class ChatStreamTests
{
    [Theory]
    [InlineData("ClaudeApi", "{\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}",
        "{\"type\":\"message_stop\"}")]
    [InlineData("Gemini", "{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"Internal\"},{\"text\":\"Hello\"}]}}]}",
        "{\"candidates\":[{\"finishReason\":\"STOP\"}]}")]
    [InlineData("Grok", "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}", "[DONE]")]
    [InlineData("Groq", "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}", "[DONE]")]
    public async Task ReadsCompletedProviderText(string provider, string chunk, string end)
    {
        using var stream = new StringReader($"data: {chunk}\n\ndata: {end}\n");
        var deltas = new List<string>();
        Assert.Equal("Hello", await ChatStream.ReadEventsAsync(stream, Enum.Parse<AssistantProvider>(provider),
            deltas.Add, CancellationToken.None));
        Assert.Equal("Hello", deltas[^1]);
    }

    [Fact]
    public async Task ADisconnectedStreamDoesNotBecomeACompletedReply()
    {
        using var stream = new StringReader("data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"}}]}\n");
        await Assert.ThrowsAsync<IOException>(() =>
            ChatStream.ReadEventsAsync(stream, AssistantProvider.Groq, _ => { }, CancellationToken.None));
    }
}
