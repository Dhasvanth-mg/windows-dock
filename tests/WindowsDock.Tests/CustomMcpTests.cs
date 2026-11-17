using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class CustomMcpTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-mcp-" + Guid.NewGuid().ToString("N"));

    public CustomMcpTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("http://example.test/mcp")]
    [InlineData("https://user:password@example.test/mcp")]
    [InlineData("https://example.test/mcp#fragment")]
    [InlineData("file:///C:/Windows")]
    public void RejectsUnusableEndpoints(string address) =>
        Assert.Throws<ArgumentException>(() => CustomMcpConnections.Endpoint(address, null));

    [Fact]
    public async Task ScopesLookupsToTheAssignedRoleAndEncryptsSettings()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""");
        var tool = new CustomMcpTool("records_search", "Search", schema.RootElement, CustomMcpConnections.QueryParameter(schema.RootElement));
        var endpoint = CustomMcpConnections.Endpoint("http://127.0.0.1:1234/mcp", "fixture-value-123");
        var saved = CustomMcpConnections.Save("research", "Record search", endpoint, tool, automatic: false, null);
        Assert.DoesNotContain(CustomMcpConnections.For("casual"), item => item.Id == saved.Id);
        Assert.DoesNotContain(AssistantConnections.Active("research"), item => item.Id == saved.Id);
        var stored = Assert.Single(CustomMcpConnections.For("research"));
        Assert.Equal(endpoint, CustomMcpConnections.Settings(stored));
        var metadata = File.ReadAllText(DockData.FilePath("custom-mcp-connections.json"));
        Assert.DoesNotContain("fixture-value-123", metadata);
        Assert.DoesNotContain("127.0.0.1", metadata);
        await Assert.ThrowsAsync<ArgumentException>(() => CustomMcpConnections.LookupAsync("research", saved.Id, "test", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => CustomMcpConnections.FetchAsync("casual", saved.Id, "{}", CancellationToken.None));
        CustomMcpConnections.Save("research", "Renamed", endpoint, tool, automatic: true, saved.Id);
        Assert.Contains(AssistantConnections.Active("research"), item => item.Id == saved.Id);
        CustomMcpConnections.Remove("research", saved.Id);
        Assert.Empty(CustomMcpConnections.For("research"));
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"q":{"type":"string"}}}""", "q")]
    [InlineData("""{"type":"object","properties":{"q":{"type":"number"}}}""", null)]
    [InlineData("""{"type":"object","properties":{"q":{"type":"string"},"where":{"type":"string"}}}""", null)]
    [InlineData("""{"type":"object","properties":{"q":{"type":"string"}},"required":["extra"]}""", null)]
    public void AutomaticLookupRequiresASingleStringField(string json, string? expected)
    {
        using var schema = JsonDocument.Parse(json);
        Assert.Equal(expected, CustomMcpConnections.QueryParameter(schema.RootElement));
    }
}
