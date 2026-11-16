using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsDock;

internal sealed class OllamaTextModel(HttpClient http)
{
    internal static OllamaTextModel Local { get; } = new(new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
    })
    {
        BaseAddress = new Uri("http://127.0.0.1:11434/"),
        Timeout = TimeSpan.FromMinutes(2),
        MaxResponseContentBufferSize = 512 * 1024
    });

    internal async Task<string[]> ModelsAsync(CancellationToken cancellation)
    {
        using var response = await http.GetAsync("api/tags", cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellation).ConfigureAwait(false);
        if (json == null || AgentSessions.Property(json.RootElement, "models") is not { ValueKind: JsonValueKind.Array } models)
            throw new JsonException("Ollama did not return a model list.");
        return models.EnumerateArray().Where(model => !IsRemote(model))
            .Select(model => AgentSessions.String(model, "model") ?? AgentSessions.String(model, "name"))
            .OfType<string>().Where(IsLocalName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal async Task<string> ChatAsync(string model, string instructions,
        IReadOnlyList<(bool User, string Text)> turns, CancellationToken cancellation)
    {
        var installed = await ModelsAsync(cancellation).ConfigureAwait(false);
        if (!installed.Contains(model, StringComparer.Ordinal))
            throw new ArgumentException("Choose an installed local Ollama model and refresh the list if it changed.");
        var messages = turns.Select(turn => new { role = turn.User ? "user" : "assistant", content = turn.Text });
        using var response = await http.PostAsJsonAsync("api/chat", new
        {
            model,
            messages = messages.Prepend(new { role = "system", content = instructions }),
            stream = false,
            keep_alive = "30s",
            options = new { num_predict = 1024 }
        }, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellation).ConfigureAwait(false);
        if (json == null || IsRemote(json.RootElement))
            throw new InvalidOperationException("Ollama did not return a local reply.");
        return AgentSessions.String(AgentSessions.Property(json.RootElement, "message"), "content") ?? "";
    }

    private static bool IsRemote(JsonElement model) =>
        !string.IsNullOrEmpty(AgentSessions.String(model, "remote_model")) ||
        !string.IsNullOrEmpty(AgentSessions.String(model, "remote_host"));

    private static bool IsLocalName(string name) => name.Length is > 0 and <= 128 &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':') &&
        !name.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase);
}
