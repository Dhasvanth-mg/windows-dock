using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WindowsDock;

internal static class DockMcp
{
    internal static int Run(bool trayMode)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "Windows Dock", Version = "1.0" },
            ToolCollection =
            [
                McpServerTool.Create(DockActions.StatusAsync, new McpServerToolCreateOptions
                {
                    Name = "dock_status",
                    Description = "Read Windows Dock volume, media outputs and presets, focus timer, radio states, widget layout, and sign-in startup.",
                    ReadOnly = true
                }),
                McpServerTool.Create(ApplyAsync, new McpServerToolCreateOptions
                {
                    Name = "dock_action",
                    Description = "Request an app or Windows control change. The user reviews the exact change on their Windows desktop before it runs. "
                        + "Pass the action name and a JSON object as the arguments.\n" + DockActions.ArgumentsGuide,
                    ReadOnly = false
                }),
                McpServerTool.Create(LookupAsync, new McpServerToolCreateOptions
                {
                    Name = "dock_lookup",
                    Description = "Read context through the coding role's enabled MCP connections. Sources: learn for Microsoft Learn, github for a PR referenced as owner/repository#123.",
                    ReadOnly = true
                })
            ]
        };
        var transport = new StreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
        var server = McpServer.Create(transport, options);
        try { server.RunAsync().GetAwaiter().GetResult(); }
        finally { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        return 0;

        async Task<string> ApplyAsync(string action, JsonElement arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var planned = DockActions.Plan(action, arguments, trayMode);
                var accepted = MessageBoxW(0, planned.Preview + "\n\nAllow this change?", "Windows Dock · External agent request",
                    0x00000004 | 0x00000100 | 0x00002000 | 0x00010000 | 0x00040000) == 6;
                if (!accepted)
                    return "The user did not approve the change.";
                token.ThrowIfCancellationRequested();
                return await planned.Execute(token);
            }
            catch (Exception error) when (DockActions.IsActionError(error))
            {
                return $"The change could not be confirmed. {error.Message}";
            }
        }
    }

    private static async Task<string> LookupAsync(string source, string query, CancellationToken token)
    {
        try { return (await AssistantConnections.FetchAsync("coding", source, query, token)).Text; }
        catch (Exception error) when (DockActions.IsActionError(error)) { throw new McpException(error.Message); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
