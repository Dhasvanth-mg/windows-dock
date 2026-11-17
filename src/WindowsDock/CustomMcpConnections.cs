using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace WindowsDock;

internal sealed record CustomMcpConnection(string Id, string Role, string Label, string Tool, bool Automatic,
    string? QueryParameter, byte[] Settings);
internal sealed record CustomMcpEndpoint(Uri Address, string? Token);
internal sealed record CustomMcpTool(string Name, string Description, JsonElement Schema, string? QueryParameter);

internal static class CustomMcpConnections
{
    private const string StateFile = "custom-mcp-connections.json";
    private const int PerRoleLimit = 12;

    internal static CustomMcpConnection[] For(string role) => (DockData.Read<CustomMcpConnection[]>(StateFile) ?? [])
        .Where(item => item.Role == role).OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();

    internal static CustomMcpEndpoint Endpoint(string address, string? token)
    {
        if (address.Length > 2048 || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("Enter an HTTPS MCP URL, or an HTTP URL on localhost.");
        token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        if (token is { Length: > 2048 } || token?.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) == true)
            throw new ArgumentException("Enter the bearer token without spaces or line breaks.");
        return new(endpoint, token);
    }

    internal static CustomMcpEndpoint Settings(CustomMcpConnection saved)
    {
        var settings = JsonSerializer.Deserialize<CustomMcpEndpoint>(DockCredentials.UnprotectText(saved.Settings)) ??
            throw new IOException("The connection settings could not be read.");
        return Endpoint(settings.Address.AbsoluteUri, settings.Token);
    }

    internal static CustomMcpConnection Save(string role, string label, CustomMcpEndpoint endpoint,
        CustomMcpTool tool, bool automatic, string? id)
    {
        label = label.Trim();
        if (!AssistantPersonas.All.Any(item => item.Id == role) || label.Length is < 1 or > 64 || label.Any(char.IsControl))
            throw new ArgumentException("Choose a name up to 64 characters for the connection.");
        if (automatic && tool.QueryParameter == null)
            throw new ArgumentException("Automatic lookup requires a read tool with one text argument.");
        var encrypted = DockCredentials.ProtectText(JsonSerializer.Serialize(Endpoint(endpoint.Address.AbsoluteUri, endpoint.Token)));
        var connection = new CustomMcpConnection(id ?? "mcp-" + Guid.NewGuid().ToString("N"), role, label, tool.Name, automatic,
            tool.QueryParameter, encrypted);
        DockData.Update<CustomMcpConnection[]>(StateFile, all =>
        {
            var remaining = (all ?? []).Where(item => item.Id != connection.Id || item.Role != role).ToArray();
            if (remaining.Count(item => item.Role == role) >= PerRoleLimit)
                throw new ArgumentException($"This assistant can have up to {PerRoleLimit} custom connections.");
            return remaining.Append(connection).ToArray();
        });
        return connection;
    }

    internal static void Remove(string role, string id) => DockData.Update<CustomMcpConnection[]>(StateFile, all =>
        (all ?? []).Where(connection => connection.Role != role || connection.Id != id).ToArray());

    internal static async Task<CustomMcpTool[]> DiscoverAsync(CustomMcpEndpoint endpoint, CancellationToken cancellation)
    {
        using var timeout = Timeout(cancellation);
        await using var client = await ServerAsync(() => ConnectAsync(endpoint, timeout.Token)).ConfigureAwait(false);
        return await ServerAsync(() => ReadToolsAsync(client, timeout.Token)).ConfigureAwait(false);
    }

    internal static async Task<AssistantContext> LookupAsync(string role, string id, string query, CancellationToken cancellation)
    {
        var saved = Get(role, id);
        if (!saved.Automatic || saved.QueryParameter == null)
            throw new ArgumentException("This connection is limited to manual requests.");
        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            throw new ArgumentException("Enter search terms between 1 and 500 characters.");
        return await RunAsync(saved, new Dictionary<string, object?> { [saved.QueryParameter] = query.Trim() },
            automatic: true, cancellation).ConfigureAwait(false);
    }

    internal static async Task<AssistantContext> FetchAsync(string role, string id, string input, CancellationToken cancellation)
    {
        var saved = Get(role, id);
        if (input.Length > AssistantContext.TextLimit)
            throw new ArgumentException("Tool arguments are limited to 8,000 characters.");
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input,
            new JsonDocumentOptions { MaxDepth = 12 });
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Enter the tool arguments as a JSON object.");
        var arguments = json.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => (object?)item.Value.Clone());
        return await RunAsync(saved, arguments, automatic: false, cancellation).ConfigureAwait(false);
    }

    private static CustomMcpConnection Get(string role, string id) => For(role).FirstOrDefault(item => item.Id == id) ??
        throw new ArgumentException("This connection is unavailable for this assistant.");

    private static async Task<AssistantContext> RunAsync(CustomMcpConnection saved, Dictionary<string, object?> args,
        bool automatic, CancellationToken cancellation)
    {
        using var timeout = Timeout(cancellation);
        var endpoint = Settings(saved);
        await using var client = await ServerAsync(() => ConnectAsync(endpoint, timeout.Token)).ConfigureAwait(false);
        var tools = await ServerAsync(() => ReadToolsAsync(client, timeout.Token)).ConfigureAwait(false);
        var tool = tools.FirstOrDefault(item => item.Name == saved.Tool);
        if (tool == null || (automatic && tool.QueryParameter != saved.QueryParameter))
            throw new McpException("The approved read tool changed. Rediscover and review it before using this connection.");
        if (!For(saved.Role).Any(connection => connection.Id == saved.Id && connection.Tool == saved.Tool &&
            connection.Settings.AsSpan().SequenceEqual(saved.Settings) && (!automatic || connection.Automatic)))
            throw new McpException("This connection changed while the request was pending.");
        return await ServerAsync(() => AssistantConnections.ReadResultAsync(client, tool.Name, args, saved.Label,
            timeout.Token, endpoint.Token)).ConfigureAwait(false);
    }

    private static async Task<CustomMcpTool[]> ReadToolsAsync(McpClient client, CancellationToken token)
    {
        var tools = await client.ListToolsAsync(cancellationToken: token).ConfigureAwait(false);
        return tools.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true && tool.Name.Length <= 128)
            .Select(tool => new CustomMcpTool(tool.Name, tool.Description ?? "", tool.JsonSchema.Clone(), QueryParameter(tool.JsonSchema)))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal).Take(80).ToArray();
    }

    internal static string? QueryParameter(JsonElement schema)
    {
        var properties = AgentSessions.Property(schema, "properties");
        if (properties.ValueKind != JsonValueKind.Object || AgentSessions.String(schema, "type") != "object")
            return null;
        var single = properties.EnumerateObject().Take(2).ToArray();
        if (single.Length != 1 || AgentSessions.String(single[0].Value, "type") != "string")
            return null;
        var required = AgentSessions.Property(schema, "required");
        return required.ValueKind == JsonValueKind.Array &&
            required.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || item.GetString() != single[0].Name)
            ? null : single[0].Name;
    }

    private static CancellationTokenSource Timeout(CancellationToken cancellation)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        return timeout;
    }

    private static Task<McpClient> ConnectAsync(CustomMcpEndpoint endpoint, CancellationToken token) =>
        AssistantConnections.CreateAsync(AssistantConnections.HttpTransport(endpoint.Address, endpoint.Token), token);

    private static async Task<T> ServerAsync<T>(Func<Task<T>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (Exception error) when (error is McpException or HttpRequestException or IOException or JsonException or InvalidOperationException)
        { throw new McpException("The custom MCP server did not complete the request. Check the connection settings."); }
    }
}
