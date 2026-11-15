using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WindowsDock;

internal sealed record AssistantConnection(string Id, string Label, string Description, Uri? Endpoint, string Tool,
    string? TokenVariable = null);

internal sealed record AssistantContext(string Source, string Text)
{
    internal const int TextLimit = 8_000;
}

internal static partial class AssistantConnections
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private static readonly AssistantConnection Exa = new("exa", "Exa web search", "Sends the search terms to Exa.",
        new("https://mcp.exa.ai/mcp?tools=web_search_exa"), "web_search_exa");
    private static readonly AssistantConnection Planner = new("planner", "Dock Planner", "Reads your dock reminders and imported calendar on this PC.",
        null, "upcoming_schedule");
    private static readonly AssistantConnection Learn = new("learn", "Microsoft Learn", "Sends the search terms to Microsoft Learn.",
        new("https://learn.microsoft.com/api/mcp?maxTokenBudget=1800"), "microsoft_docs_search");
    private static readonly AssistantConnection GitHub = new("github", "GitHub PR reader", "Reads a pull request using GITHUB_MCP_TOKEN.",
        new("https://api.githubcopilot.com/mcp/x/pull_requests/readonly"), "pull_request_read", "GITHUB_MCP_TOKEN");

    internal static AssistantConnection[] For(string personaId) => personaId switch
    {
        "research" or "writing" => [Exa],
        "housekeeping" => [Planner],
        "coding" => [Learn, GitHub],
        "casual" => [Exa],
        _ => []
    };

    internal static bool IsAvailable(AssistantConnection connection) =>
        connection.TokenVariable == null || !string.IsNullOrWhiteSpace(ReadToken(connection.TokenVariable));

    internal static AssistantConnection[] Active(string personaId)
    {
        var enabled = Enabled();
        return For(personaId).Where(connection => enabled.Contains(connection.Id) && IsAvailable(connection)).ToArray();
    }

    internal static HashSet<string> Enabled() =>
        DockData.Read<HashSet<string>>("assistant-connections.json") ?? [Exa.Id, Planner.Id, Learn.Id];

    [GeneratedRegex(@"\A([A-Za-z0-9-]+)/([A-Za-z0-9_.-]+)#([1-9][0-9]{0,8})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestReference();

    internal static Dictionary<string, object?> Arguments(AssistantConnection connection, string query)
    {
        if (connection.Id == Planner.Id)
            return new() { ["days"] = query == "today" ? 1 : 7 };
        if (connection.Id == GitHub.Id)
        {
            var reference = PullRequestReference().Match(query.Trim());
            if (!reference.Success)
                throw new ArgumentException("Enter the pull request as owner/repository#123.");
            return new()
            {
                ["method"] = "get", ["owner"] = reference.Groups[1].Value, ["repo"] = reference.Groups[2].Value,
                ["pullNumber"] = int.Parse(reference.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            throw new ArgumentException("Enter search terms between 1 and 500 characters.");
        return new() { ["query"] = query.Trim() };
    }

    internal static async Task<AssistantContext> FetchAsync(string personaId, string connectionId, string query,
        CancellationToken cancellation)
    {
        var connection = For(personaId).FirstOrDefault(item => item.Id == connectionId) ??
            throw new ArgumentException("This connection is not assigned to this assistant.");
        if (!Enabled().Contains(connection.Id))
            throw new ArgumentException($"Enable {connection.Label} before fetching context.");
        var arguments = Arguments(connection, query);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        var token = timeout.Token;
        await using var client = await McpClient.CreateAsync(Transport(connection),
            new McpClientOptions { ClientInfo = new Implementation { Name = "Windows Dock", Version = "1.0" } },
            cancellationToken: token).ConfigureAwait(false);
        var catalog = await client.ListToolsAsync(cancellationToken: token).ConfigureAwait(false);
        if (catalog.All(tool => tool.Name != connection.Tool))
            throw new McpException($"{connection.Label} does not expose the expected read tool.");
        var result = await client.CallToolAsync(connection.Tool, arguments, cancellationToken: token).ConfigureAwait(false);
        if (result.IsError == true)
            throw new McpException($"{connection.Label} could not complete this request.");
        var text = string.Join("\n\n", result.Content.OfType<TextContentBlock>().Select(item => item.Text));
        if (string.IsNullOrWhiteSpace(text) && result.StructuredContent is { } structured)
            text = structured.GetRawText();
        if (string.IsNullOrWhiteSpace(text))
            throw new McpException($"{connection.Label} returned no readable text.");
        return new(connection.Label, Limit(text));
    }

    private static IClientTransport Transport(AssistantConnection connection)
    {
        if (connection.Endpoint == null)
            return new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Dock Planner",
            Command = Path.Combine(AppContext.BaseDirectory, "WindowsDock.exe"),
                Arguments = ["--planner-mcp"],
                InheritEnvironmentVariables = false
            });

        var headers = new Dictionary<string, string>();
        if (connection.TokenVariable is { } variable)
        {
            var token = ReadToken(variable);
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
                throw new ArgumentException($"Set {variable} to connect {connection.Label}.");
            headers["Authorization"] = "Bearer " + token;
        }
        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = connection.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = headers,
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        }, Http);
    }

    private static string? ReadToken(string name)
    {
        var process = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrEmpty(process) ? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)?.Trim() : process;
    }

    private static string Limit(string text)
    {
        if (text.Length <= AssistantContext.TextLimit)
            return text;
        var limit = AssistantContext.TextLimit - "\n[Result clipped]".Length;
        var length = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
        return text[..length] + "\n[Result clipped]";
    }
}
