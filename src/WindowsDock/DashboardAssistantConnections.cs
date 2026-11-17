using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ModelContextProtocol;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowAssistantConnections(AssistantPersona persona, bool quick = false)
    {
        var tint = AssistantInk;
        var title = quick ? AssistantQuickLabel(persona) : "MCP connections";
        var (window, content, cancellation) = CreateDockPanel(title, persona.Label, 458, 0xE8A7, tint,
            backAction: ShowAssistantWidget, backLabel: $"Back to {persona.Name}");
        var connections = AssistantConnections.For(persona.Id);
        var chat = assistantConversations[assistantRole];
        var enabled = AssistantConnections.Enabled();
        var choices = new ComboBox { DisplayMemberPath = nameof(AssistantConnection.Label) };
        AutomationProperties.SetName(choices, "MCP connection");
        choices.ItemsSource = connections;
        choices.SelectedIndex = 0;
        content.Children.Add(choices);
        if (!quick)
            content.Children.Add(ActionButton("Custom connections", () => ShowCustomMcpConnections(persona)));
        var description = Text("", 11, AssistantSecondary);
        description.Margin = new Thickness(0, 6, 0, 4);
        description.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(description);
        var allow = new CheckBox { Content = $"Enable for {persona.Name}", Margin = new Thickness(0, 0, 0, 8) };
        content.Children.Add(allow);
        var query = Field("Connection query", 500);
        query.Margin = new Thickness(0, 0, 0, 6);
        content.Children.Add(query);
        var ranges = new ComboBox { ItemsSource = new[] { "Today", "7 days including today" }, SelectedIndex = quick ? 0 : 1,
            Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(ranges, "Schedule range");
        content.Children.Add(ranges);
        var actions = new WrapPanel();
        var fetch = AccentButton(quick ? (persona.Id == "research" ? "Search" : "Read schedule") : "Fetch context",
            0xE721, tint);
        fetch.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(fetch);
        var attach = ActionButton(chat.Context == null ? "Attach to draft" : "Replace attached context");
        actions.Children.Add(attach);
        content.Children.Add(actions);
        var status = Text("", 11, AssistantSecondary);
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
        AssistantContext? result = null;
        var changing = false;
        var busy = false;
        choices.SelectionChanged += (_, _) => ShowConnection();
        query.TextChanged += (_, _) => ClearQueryResult();
        ranges.SelectionChanged += (_, _) => ClearQueryResult();
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
            AssistantContext? fetched = null;
            string? failure = null;
            var input = connection.Id == "planner" && ranges.SelectedIndex == 0 ? "today" : query.Text;
            try { fetched = await AssistantConnections.FetchAsync(persona.Id, connection.Id, input, cancellation); }
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
            if (quick && string.IsNullOrWhiteSpace(chat.Draft))
                chat.Draft = persona.Id == "research"
                    ? $"Help me answer this using the attached search results. Which sources are strongest?\n\n{query.Text.Trim()}"
                    : ranges.SelectedIndex == 0
                        ? "Help me plan today using the attached schedule. What should come first?"
                        : "Look at the next seven days. Which days need breathing room or prep time?";
            ShowAssistantWidget();
        };
        ShowConnection();
        if (quick)
            window.Loaded += (_, _) => (query.Visibility == Visibility.Visible ? (Control)query : ranges).Focus();
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
            ranges.Visibility = connection.Id == "planner" ? Visibility.Visible : Visibility.Collapsed;
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
                    {
                        chat.Context = null;
                        attach.Content = "Attach to draft";
                        AutomationProperties.SetName(attach, "Attach to draft");
                        attach.ToolTip = "Attach to draft";
                    }
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

        void ClearQueryResult()
        {
            ClearResult();
            status.Text = "";
            UpdateActions();
        }

        void UpdateActions()
        {
            if (choices.SelectedItem is not AssistantConnection connection)
                return;
            var available = AssistantConnections.IsAvailable(connection);
            choices.IsEnabled = query.IsEnabled = ranges.IsEnabled = !busy;
            allow.IsEnabled = !busy && available;
            fetch.IsEnabled = !busy && available && enabled.Contains(connection.Id) &&
                (connection.Endpoint == null || !string.IsNullOrWhiteSpace(query.Text));
            attach.IsEnabled = !busy && result != null;
        }
    }
}
