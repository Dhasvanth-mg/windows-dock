using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal sealed record CodingConnection(Uri Address, string Bearer)
{
    private const string FileName = "coding-live-connection.dat";
    private sealed record Settings(string Address, string Bearer);

    internal static CodingConnection Parse(string address, string bearer)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback ||
            endpoint.Scheme is not ("ws" or "wss") || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0 || endpoint.Port == 0)
            throw new ArgumentException("Enter a local WebSocket address such as ws://127.0.0.1:4500.");
        bearer = bearer.Trim();
        if (bearer.Length > 4096 || bearer.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("Enter a bearer token without spaces.");
        return new(endpoint, bearer);
    }

    internal static CodingConnection? Load()
    {
        try
        {
            var saved = DockCredentials.ReadPrivateText(FileName);
            return saved != null && JsonSerializer.Deserialize<Settings>(saved) is { } value
                ? Parse(value.Address, value.Bearer) : null;
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { return null; }
    }

    internal void Save() => DockCredentials.WritePrivateText(FileName, JsonSerializer.Serialize(new Settings(Address.ToString(), Bearer)));

    internal static void Forget() => System.IO.File.Delete(DockData.FilePath(FileName));
}

internal sealed record CodingLiveState(string Status, string? ActiveTurn = null, bool NeedsApproval = false)
{
    internal bool CanSend => Status == "idle" || ActiveTurn != null;
}

internal static class CodingSessionControls
{
    internal const int DraftLimit = 12_000;
    internal static readonly string[] CodexEfforts = ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"];
    internal static readonly string[] ClaudeEfforts = ["low", "medium", "high"];

    internal static async Task ProbeAsync(CodingConnection connection, CancellationToken token)
    {
        using var server = await CodingControlServer.ConnectAsync(connection, token).ConfigureAwait(false);
    }

    internal static async Task<string[]> ModelsAsync(CodingConnection connection, CancellationToken token)
    {
        using var server = await CodingControlServer.ConnectAsync(connection, token).ConfigureAwait(false);
        var response = await server.CallAsync("model/list", new { limit = 100, includeHidden = false }, token).ConfigureAwait(false);
        var data = Property(response, "data");
        return data.ValueKind != JsonValueKind.Array ? [] : data.EnumerateArray()
            .Select(value => String(value, "model")).OfType<string>().Where(ValidModel).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static async Task<CodingLiveState> InspectAsync(CodingConnection connection, string threadId, CancellationToken token)
    {
        using var server = await CodingControlServer.ConnectAsync(connection, token).ConfigureAwait(false);
        return await InspectAsync(server, threadId, token).ConfigureAwait(false);
    }

    private static async Task<CodingLiveState> InspectAsync(CodingControlServer server, string threadId, CancellationToken token)
    {
        var response = await server.CallAsync("thread/read", new { threadId, includeTurns = false }, token).ConfigureAwait(false);
        var thread = Property(response, "thread");
        if (String(thread, "id") != threadId)
            throw new CodingControlException("The app server didn't confirm the selected chat.");
        var status = Property(thread, "status");
        var type = String(status, "type") ?? "unknown";
        if (type != "active")
            return new(type);
        var flags = Property(status, "activeFlags");
        var waiting = flags.ValueKind == JsonValueKind.Array && flags.EnumerateArray().Any(flag =>
            flag.ValueKind == JsonValueKind.String && flag.GetString() == "waitingOnApproval");
        try
        {
            var recent = await server.CallAsync("thread/turns/list",
                new { threadId, limit = 1, itemsView = "notLoaded", sortDirection = "desc" }, token).ConfigureAwait(false);
            var turns = Property(recent, "data");
            if (turns.ValueKind == JsonValueKind.Array && turns.GetArrayLength() != 0 && String(turns[0], "status") == "inProgress")
                return new(type, String(turns[0], "id"), waiting);
        }
        catch (CodingControlException error) when (error.Code == -32601) { }
        return new(type, NeedsApproval: waiting);
    }

    internal static async Task SendAsync(CodingConnection connection, string threadId, string? expectedTurnId, string text,
        string model, string effort, CancellationToken token)
    {
        var draft = Input(text);
        var (chosenModel, chosenEffort) = Choices(AgentProvider.Codex, model, effort);
        using var server = await CodingControlServer.ConnectAsync(connection, token).ConfigureAwait(false);
        await CheckTurnAsync(server, threadId, expectedTurnId, token).ConfigureAwait(false);
        if (expectedTurnId != null)
            await server.CallAsync("turn/steer", new { threadId, expectedTurnId, input = draft }, token).ConfigureAwait(false);
        else
            await server.CallAsync("turn/start", new { threadId, input = draft, model = chosenModel, effort = chosenEffort }, token).ConfigureAwait(false);
    }

    internal static async Task ApplyAsync(CodingConnection connection, string threadId, string? expectedTurnId, string model,
        string effort, CancellationToken token)
    {
        var (chosenModel, chosenEffort) = Choices(AgentProvider.Codex, model, effort);
        if (chosenModel == null && chosenEffort == null)
            throw new ArgumentException("Choose a model or thinking level first.");
        using var server = await CodingControlServer.ConnectAsync(connection, token).ConfigureAwait(false);
        await CheckTurnAsync(server, threadId, expectedTurnId, token).ConfigureAwait(false);
        if (expectedTurnId == null)
            await server.CallAsync("thread/settings/update", new { threadId, model = chosenModel, effort = chosenEffort }, token).ConfigureAwait(false);
        else
            await server.CallAsync("turn/settings/update", new { threadId, turnId = expectedTurnId, model = chosenModel, effort = chosenEffort }, token).ConfigureAwait(false);
    }

    internal static async Task StopAsync(CodingConnection connection, string threadId, string turnId, CancellationToken token)
    {
        using var server = await CodingControlServer.ConnectAsync(connection, token).ConfigureAwait(false);
        await CheckTurnAsync(server, threadId, turnId, token).ConfigureAwait(false);
        await server.CallAsync("turn/interrupt", new { threadId, turnId }, token).ConfigureAwait(false);
    }

    private static async Task CheckTurnAsync(CodingControlServer server, string threadId, string? expectedTurn, CancellationToken token)
    {
        var state = await InspectAsync(server, threadId, token).ConfigureAwait(false);
        if (!state.CanSend || state.ActiveTurn != expectedTurn)
            throw new CodingControlException("The chat state changed. Refresh it before trying again.");
    }

    internal static object[] Input(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > DraftLimit)
            throw new ArgumentException($"Enter a message up to {DraftLimit:N0} characters.");
        return [new { type = "text", text = text.Trim() }];
    }

    internal static (string? Model, string? Effort) Choices(AgentProvider provider, string model, string effort)
    {
        model = model.Trim();
        if (model.Length > 0 && !ValidModel(model))
            throw new ArgumentException("Enter a model ID or alias from your provider.");
        var efforts = provider == AgentProvider.Codex ? CodexEfforts : ClaudeEfforts;
        if (effort.Length > 0 && !efforts.Contains(effort))
            throw new ArgumentException("Choose a thinking level from the list.");
        return (model.Length == 0 ? null : model, effort.Length == 0 ? null : effort);
    }

    private static bool ValidModel(string model) => model.Length is > 0 and <= 128 &&
        model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':' or '[' or ']');
}
