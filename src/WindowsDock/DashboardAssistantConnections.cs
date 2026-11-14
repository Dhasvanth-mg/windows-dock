using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ModelContextProtocol;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowAssistantConnections(AssistantPersona persona)
    {
        var tint = PersonaTint(persona);
        var (window, content, cancellation) = CreateDockPanel("MCP connections", persona.Label, 458, 0xE8A7, tint,
            backAction: ShowAssistantWidget, backLabel: $"Back to {persona.Name}");
        var connections = AssistantConnections.For(persona.Id);
        var chat = assistantConversations[assistantRole];
        var enabled = AssistantConnections.Enabled();
        var choices = new ComboBox { DisplayMemberPath = nameof(AssistantConnection.Label) };
        AutomationProperties.SetName(choices, "MCP connection");
        choices.ItemsSource = connections;
        choices.SelectedIndex = 0;
        content.Children.Add(choices);
        var description = Text("", 11, subdued);
        description.Margin = new Thickness(0, 6, 0, 4);
        description.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(description);
        var allow = new CheckBox { Content = $"Enable for {persona.Name}", Margin = new Thickness(0, 0, 0, 8) };
        content.Children.Add(allow);
        var query = Field("Connection query", 500);
        query.Margin = new Thickness(0, 0, 0, 6);
        content.Children.Add(query);
        var actions = new WrapPanel();
        var fetch = AccentButton("Fetch context", 0xE721, tint);
        fetch.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(fetch);
        var attach = ActionButton("Attach to draft");
        actions.Children.Add(attach);
        content.Children.Add(actions);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 0);
        content.Children.Add(status);
        var preview = Field("Tool result preview", 0);
        preview.IsReadOnly = true;
        preview.TextWrapping = TextWrapping.Wrap;
        preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        preview.Height = 126;
        preview.Margin = new Thickness(0, 6, 0, 0);
        preview.Visibility = Visibility.Collapsed;
        content.Children.Add(preview);
        AssistantMcpResult? result = null;
        var changing = false;
        var busy = false;
        choices.SelectionChanged += (_, _) => ShowConnection();
        query.TextChanged += (_, _) =>
        {
            ClearResult();
            status.Text = "";
            UpdateActions();
        };
        allow.Checked += (_, _) => SaveAllowed(true);
        allow.Unchecked += (_, _) => SaveAllowed(false);
        fetch.Click += async (_, _) =>
        {
            if (choices.SelectedItem is not AssistantConnection connection || !fetch.IsEnabled)
                return;
            ClearResult();
            busy = true;
            UpdateActions();
            status.Text = $"Reading from {connection.Label}…";
            AssistantMcpResult? fetched = null;
            string? failure = null;
            try { fetched = await AssistantConnections.FetchAsync(persona.Id, connection.Id, query.Text, cancellation); }
            catch (ArgumentException error) { failure = error.Message; }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { failure = "The connection timed out."; }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is McpException or HttpRequestException or IOException or
                System.ComponentModel.Win32Exception or InvalidOperationException or System.Text.Json.JsonException)
            {
                failure = $"Could not read from {connection.Label}. Check the connection and try again.";
            }
            if (cancellation.IsCancellationRequested)
                return;
            await window.Dispatcher.InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested)
                    return;
                busy = false;
                result = fetched;
                if (fetched != null)
                {
                    preview.Text = fetched.Text;
                    preview.Visibility = Visibility.Visible;
                }
                status.Text = failure ?? "Review the result before attaching it to a message.";
                UpdateActions();
            });
        };
        attach.Click += (_, _) =>
        {
            if (result == null)
                return;
            chat.Context = result;
            ShowAssistantWidget();
        };
        ShowConnection();
        window.Show();

        void ShowConnection()
        {
            if (choices.SelectedItem is not AssistantConnection connection)
                return;
            changing = true;
            allow.IsChecked = enabled.Contains(connection.Id);
            changing = false;
            description.Text = connection.Description;
            query.Text = "";
            query.Tag = connection.Id == "github" ? "owner/repository#123" : "Search terms";
            query.Visibility = connection.Endpoint == null ? Visibility.Collapsed : Visibility.Visible;
            status.Text = AssistantConnections.IsAvailable(connection) ? "" : "Set GITHUB_MCP_TOKEN to enable this connection.";
            ClearResult();
            UpdateActions();
        }

        void SaveAllowed(bool value)
        {
            if (changing || choices.SelectedItem is not AssistantConnection connection)
                return;
            var next = new HashSet<string>(enabled);
            if (value)
                next.Add(connection.Id);
            else
                next.Remove(connection.Id);
            try
            {
                DockData.Save("assistant-connections.json", next);
                enabled = next;
                status.Text = AssistantConnections.IsAvailable(connection) ? "" : "Set GITHUB_MCP_TOKEN to enable this connection.";
                if (!value)
                {
                    if (chat.Context?.Source == connection.Label)
                        chat.Context = null;
                    ClearResult();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                changing = true;
                allow.IsChecked = enabled.Contains(connection.Id);
                changing = false;
                status.Text = "Could not save this connection setting.";
            }
            UpdateActions();
        }

        void ClearResult()
        {
            result = null;
            preview.Text = "";
            preview.Visibility = Visibility.Collapsed;
        }

        void UpdateActions()
        {
            if (choices.SelectedItem is not AssistantConnection connection)
                return;
            var available = AssistantConnections.IsAvailable(connection);
            choices.IsEnabled = query.IsEnabled = !busy;
            allow.IsEnabled = !busy && available;
            fetch.IsEnabled = !busy && available && enabled.Contains(connection.Id) &&
                (connection.Endpoint == null || !string.IsNullOrWhiteSpace(query.Text));
            attach.IsEnabled = !busy && result != null;
        }
    }
}
