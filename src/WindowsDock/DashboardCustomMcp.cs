using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ModelContextProtocol;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowCustomMcpConnections(AssistantPersona persona)
    {
        var (window, content, cancellation) = CreateDockPanel("Custom MCP connections", persona.Name, 502, 0xE8A7,
            AssistantInk, backAction: () => ShowAssistantConnections(persona), backLabel: "Back to connections");
        var saved = new ComboBox { DisplayMemberPath = nameof(CustomMcpConnection.Label) };
        AutomationProperties.SetName(saved, "Saved custom connections");
        var add = ActionButton("New connection");
        var remove = ActionButton("Remove connection");
        var available = new WrapPanel { Children = { add, remove } };
        var name = Field("Connection name", 64);
        var url = Field("MCP URL", 2048);
        url.Tag = "https://…/mcp";
        var key = new PasswordBox { MaxLength = 2048 };
        AutomationProperties.SetName(key, "MCP bearer token");
        var keep = new CheckBox { Content = "Keep the saved token", Visibility = Visibility.Collapsed };
        var discover = ActionButton("Discover read tools");
        var tools = new ComboBox { DisplayMemberPath = nameof(CustomMcpTool.Name) };
        AutomationProperties.SetName(tools, "MCP read tool");
        var schema = Field("Tool argument schema", 0);
        schema.IsReadOnly = true;
        schema.TextWrapping = TextWrapping.Wrap;
        schema.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        schema.Height = 66;
        schema.Visibility = Visibility.Collapsed;
        var automatic = new CheckBox { Content = $"Allow {persona.Name} to look up text with this tool",
            Margin = new Thickness(0, 6, 0, 0) };
        var scope = Text("Automatic lookup can send query text to this server and share its result with your chat provider.",
            11, AssistantSecondary);
        scope.TextWrapping = TextWrapping.Wrap;
        var save = ActionButton("Save connection");
        var request = Field("Tool arguments JSON", AssistantContext.TextLimit);
        request.Text = "{}";
        request.AcceptsReturn = true;
        request.TextWrapping = TextWrapping.Wrap;
        request.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        request.Height = 70;
        var fetch = ActionButton("Fetch context");
        var attach = ActionButton("Attach to draft");
        var response = Field("Custom tool result preview", AssistantContext.TextLimit);
        response.IsReadOnly = true;
        response.TextWrapping = TextWrapping.Wrap;
        response.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        response.Height = 120;
        response.Visibility = Visibility.Collapsed;
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var settings = new StackPanel
        {
            Children = { Labeled("Name", name), Labeled("Streamable HTTP URL", url), Labeled("Bearer token (optional)", key),
                keep, discover, Labeled("Approved read tool", tools), schema, automatic, scope, save },
            Margin = new Thickness(0, 8, 0, 8)
        };
        var call = new StackPanel
        {
            Children = { Labeled("Tool arguments", request), new WrapPanel { Children = { fetch, attach } }, response }
        };
        foreach (var element in new UIElement[] { saved, available, Card(settings), Card(call), status })
            content.Children.Add(element);

        CustomMcpConnection? editing = null;
        CustomMcpEndpoint? savedEndpoint = null;
        CustomMcpEndpoint? discoveredEndpoint = null;
        AssistantContext? result = null;
        var loading = false;
        var busy = false;
        var changed = false;
        Reload(null);
        add.Click += (_, _) => { saved.SelectedIndex = -1; Load(null); name.Focus(); };
        saved.SelectionChanged += (_, _) => { if (!loading) Load(saved.SelectedItem as CustomMcpConnection); };
        name.TextChanged += (_, _) => MarkChanged();
        url.TextChanged += (_, _) => EndpointChanged();
        key.PasswordChanged += (_, _) => EndpointChanged();
        keep.Checked += (_, _) => EndpointChanged();
        keep.Unchecked += (_, _) => EndpointChanged();
        automatic.Checked += (_, _) => MarkChanged();
        automatic.Unchecked += (_, _) => MarkChanged();
        request.TextChanged += (_, _) => ClearResult();
        tools.SelectionChanged += (_, _) =>
        {
            if (loading)
                return;
            ShowTool();
            MarkChanged();
        };

        discover.Click += async (_, _) => await WithNetworkAsync("Discovering tools…", async () =>
        {
            var endpoint = CurrentEndpoint();
            var allowed = await CustomMcpConnections.DiscoverAsync(endpoint, cancellation);
            if (cancellation.IsCancellationRequested)
                return;
            discoveredEndpoint = endpoint;
            loading = true;
            tools.ItemsSource = allowed;
            tools.SelectedItem = allowed.FirstOrDefault(item => item.Name == editing?.Tool) ?? allowed.FirstOrDefault();
            loading = false;
            ShowTool();
            changed = true;
            status.Text = allowed.Length == 0 ? "This server reports no read-only tools." :
                "Choose a tool and save it for this assistant.";
        });

        save.Click += (_, _) =>
        {
            try
            {
                if (tools.SelectedItem is not CustomMcpTool tool || discoveredEndpoint != CurrentEndpoint())
                    throw new ArgumentException("Discover and select a read tool for this connection first.");
                var connection = CustomMcpConnections.Save(persona.Id, name.Text, CurrentEndpoint(), tool,
                    automatic.IsChecked == true, editing?.Id);
                Reload(connection.Id);
                status.Text = "Connection saved.";
            }
            catch (ArgumentException error) { status.Text = error.Message; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
            { status.Text = "Could not save this connection."; }
        };

        remove.Click += (_, _) =>
        {
            if (editing == null || MessageBox.Show(window, $"Remove {editing.Label} from {persona.Name}?",
                "Remove connection", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                CustomMcpConnections.Remove(persona.Id, editing.Id);
                Reload(null);
                status.Text = "Connection removed.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "Could not remove the connection."; }
        };

        fetch.Click += async (_, _) => await WithNetworkAsync("Fetching context…", async () =>
        {
            if (editing == null || changed)
                return;
            result = await CustomMcpConnections.FetchAsync(persona.Id, editing.Id, request.Text, cancellation);
            if (cancellation.IsCancellationRequested)
                return;
            response.Text = result.Text;
            response.Visibility = Visibility.Visible;
            status.Text = "Review this result before attaching it to your next message.";
        });

        attach.Click += (_, _) =>
        {
            if (result == null)
                return;
            var slot = Array.IndexOf(AssistantPersonas.All, persona);
            var chat = assistantConversations[slot];
            if (chat.Context != null && MessageBox.Show(window, "Replace the context already attached to this draft?",
                "Attach MCP result", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            chat.Context = result;
            OpenAssistantConversation(slot);
        };
        window.Closed += (_, _) => { loading = true; key.Clear(); };
        window.Show();

        async Task WithNetworkAsync(string message, Func<Task> action)
        {
            if (busy)
                return;
            ClearResult();
            busy = true;
            status.Text = message;
            Update();
            try { await action(); }
            catch (ArgumentException error) { status.Text = error.Message; }
            catch (JsonException) { status.Text = "Check the JSON arguments or the server response."; }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { status.Text = "The MCP connection timed out."; }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is McpException or HttpRequestException or IOException or
                UnauthorizedAccessException or InvalidOperationException or CryptographicException)
            { status.Text = "Could not read from this connection. Check the URL, token, or approved tool."; }
            finally
            {
                busy = false;
                if (!cancellation.IsCancellationRequested)
                    Update();
            }
        }

        void Reload(string? id)
        {
            var all = CustomMcpConnections.For(persona.Id);
            var selected = id == null ? all.FirstOrDefault() : all.FirstOrDefault(item => item.Id == id);
            loading = true;
            saved.ItemsSource = all;
            saved.SelectedItem = selected;
            loading = false;
            Load(selected);
        }

        void Load(CustomMcpConnection? connection)
        {
            editing = connection;
            savedEndpoint = discoveredEndpoint = null;
            loading = true;
            name.Text = connection?.Label ?? "";
            url.Clear();
            key.Clear();
            tools.ItemsSource = null;
            automatic.IsChecked = false;
            keep.IsChecked = false;
            keep.Visibility = Visibility.Collapsed;
            status.Text = "";
            if (connection != null)
            {
                try
                {
                    savedEndpoint = discoveredEndpoint = CustomMcpConnections.Settings(connection);
                    url.Text = savedEndpoint.Address.AbsoluteUri;
                    if (savedEndpoint.Token != null)
                    {
                        keep.IsChecked = true;
                        keep.Visibility = Visibility.Visible;
                    }
                    tools.ItemsSource = new[] { new CustomMcpTool(connection.Tool, "", default, connection.QueryParameter) };
                    tools.SelectedIndex = 0;
                    automatic.IsChecked = connection.Automatic;
                }
                catch (Exception error) when (error is ArgumentException or IOException or CryptographicException or JsonException)
                { status.Text = "The saved connection settings could not be opened."; }
            }
            loading = false;
            changed = false;
            ShowTool();
            Update();
        }

        void ShowTool()
        {
            var tool = tools.SelectedItem as CustomMcpTool;
            request.Text = tool?.QueryParameter is { } parameter
                ? JsonSerializer.Serialize(new Dictionary<string, string> { [parameter] = "" }) : "{}";
            schema.Text = tool is { Schema.ValueKind: JsonValueKind.Object }
                ? tool.Description + "\n" + tool.Schema.GetRawText() : "";
            schema.Visibility = schema.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (tool?.QueryParameter == null)
                automatic.IsChecked = false;
        }

        CustomMcpEndpoint CurrentEndpoint()
        {
            var endpoint = CustomMcpConnections.Endpoint(url.Text, key.Password);
            if (endpoint.Token == null && savedEndpoint?.Address == endpoint.Address && keep.IsChecked == true)
                endpoint = endpoint with { Token = savedEndpoint.Token };
            return endpoint;
        }

        void EndpointChanged()
        {
            if (loading)
                return;
            discoveredEndpoint = null;
            tools.ItemsSource = null;
            MarkChanged();
        }

        void MarkChanged()
        {
            if (loading)
                return;
            changed = true;
            ClearResult();
        }

        void ClearResult()
        {
            result = null;
            response.Clear();
            response.Visibility = Visibility.Collapsed;
            Update();
        }

        void Update()
        {
            saved.IsEnabled = add.IsEnabled = name.IsEnabled = url.IsEnabled = key.IsEnabled = keep.IsEnabled = !busy;
            remove.IsEnabled = !busy && editing != null;
            discover.IsEnabled = !busy && url.Text.Trim().Length > 0;
            tools.IsEnabled = !busy && discoveredEndpoint != null;
            automatic.IsEnabled = !busy && tools.SelectedItem is CustomMcpTool { QueryParameter: not null };
            save.IsEnabled = !busy && changed && tools.SelectedItem is CustomMcpTool && !string.IsNullOrWhiteSpace(name.Text);
            request.IsEnabled = fetch.IsEnabled = !busy && editing != null && savedEndpoint != null && !changed;
            attach.IsEnabled = !busy && result != null;
        }
    }
}
