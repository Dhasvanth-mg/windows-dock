using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsDock;

internal sealed class ChatStream(Action<string>? preview)
{
    private readonly StringBuilder text = new();
    private long lastPreview = Stopwatch.GetTimestamp();

    internal void Append(string? chunk)
    {
        if (string.IsNullOrEmpty(chunk) || text.Length >= 40_000)
            return;
        text.Append(chunk, 0, Math.Min(40_000 - text.Length, chunk.Length));
        if (preview != null && Stopwatch.GetElapsedTime(lastPreview) >= TimeSpan.FromMilliseconds(90))
        {
            lastPreview = Stopwatch.GetTimestamp();
            preview(text.ToString());
        }
    }

    internal string Complete()
    {
        var reply = text.ToString();
        preview?.Invoke(reply);
        return reply;
    }

    internal static async Task<string> ReadEventsAsync(TextReader reader, AssistantProvider provider,
        Action<string> preview, CancellationToken cancellation)
    {
        var text = new ChatStream(preview);
        var finished = false;
        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var payload = line.AsMemory(5).TrimStart();
            if (payload.Span.SequenceEqual("[DONE]"))
                return text.Complete();
            using var message = JsonDocument.Parse(payload);
            var data = message.RootElement;
            if (AgentSessions.Property(data, "error").ValueKind == JsonValueKind.Object)
                throw new IOException("The provider reported an error during the reply.");

            if (provider == AssistantProvider.ClaudeApi)
            {
                if (AgentSessions.String(data, "type") == "message_stop")
                    return text.Complete();
                var delta = AgentSessions.Property(data, "delta");
                if (AgentSessions.String(delta, "type") == "text_delta")
                    text.Append(AgentSessions.String(delta, "text"));
                continue;
            }

            var choices = AgentSessions.Property(data, provider == AssistantProvider.Gemini ? "candidates" : "choices");
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                continue;
            var choice = choices[0];
            if (provider == AssistantProvider.Gemini)
            {
                text.Append(TextModelClient.ReadGeminiCandidate(choice));
                var reason = AgentSessions.String(choice, "finishReason");
                finished |= reason is { Length: > 0 } and not "FINISH_REASON_UNSPECIFIED";
            }
            else
            {
                text.Append(AgentSessions.String(AgentSessions.Property(choice, "delta"), "content"));
                finished |= AgentSessions.String(choice, "finish_reason") is { Length: > 0 };
            }
        }
        if (finished)
            return text.Complete();
        throw new IOException("The provider disconnected before the reply finished.");
    }
}
