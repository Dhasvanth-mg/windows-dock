using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowAssistantWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Assistant", "", 488, 0xEAB7, purple, widget: true);
        var history = new List<(bool User, string Text)>();
        var providers = new ComboBox { ItemsSource = new[] { "Gemini", "Groq" }, SelectedIndex = summaryGemini ? 0 : 1 };
        AutomationProperties.SetName(providers, "Assistant provider");
        var model = Field("Assistant model ID", 128);
        var key = new PasswordBox { MaxLength = 512 };
        AutomationProperties.SetName(key, "Assistant API key");
        var settings = Columns(.85, 1.4);
        Put(settings, Labeled("Provider", providers), 0);
        Put(settings, Labeled("Model ID", model), 1);
        content.Children.Add(settings);
        var credentials = Labeled("API key for this dock session", key);
        credentials.Margin = new Thickness(0, 7, 0, 0);
        content.Children.Add(credentials);

        var messages = new StackPanel();
        var conversation = new ScrollViewer { Content = messages, MaxHeight = 218, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        content.Children.Add(conversation);
        var prompt = Field("Message", 8000);
        prompt.AcceptsReturn = true;
        prompt.TextWrapping = TextWrapping.Wrap;
        prompt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        prompt.Height = 68;
        prompt.Tag = "Write a message";
        prompt.Margin = new Thickness(0, 9, 0, 0);
        content.Children.Add(prompt);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 6);
        content.Children.Add(status);

        var actions = new WrapPanel();
        var send = AccentButton("Send message", 0xE724, purple);
        send.ToolTip = "Send message (Ctrl+Enter)";
        send.Margin = new Thickness(0, 0, 5, 0);
        var clear = ActionButton("New chat");
        clear.Click += (_, _) =>
        {
            history.Clear();
            RenderConversation();
            SetBusy(false);
            SetDisclosure();
            prompt.Focus();
        };
        actions.Children.Add(send);
        actions.Children.Add(clear);
        content.Children.Add(actions);
        providers.SelectionChanged += (_, _) => SetProvider();
        send.Click += async (_, _) => await SendAsync();
        prompt.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && send.IsEnabled)
            {
                args.Handled = true;
                await SendAsync();
            }
        };
        window.Loaded += (_, _) => prompt.Focus();
        window.Closed += (_, _) => key.Clear();
        SetProvider();
        SetBusy(false);
        window.Show();

        StackPanel Labeled(string label, Control control)
        {
            var group = new StackPanel();
            group.Children.Add(Text(label, 11, subdued));
            control.Margin = new Thickness(0, 3, 0, 0);
            group.Children.Add(control);
            return group;
        }

        void SetProvider()
        {
            var index = providers.SelectedIndex;
            model.Text = index == 0 ? TextModelClient.GeminiSummaryModel : TextModelClient.GroqSummaryModel;
            key.Clear();
            SetDisclosure();
        }

        void SetDisclosure()
        {
            var index = providers.SelectedIndex;
            status.Text = $"Send shares this conversation with {providers.SelectedItem}.";
            if (summaryKeys[index] != null || EnvironmentKey(index) != null)
                status.Text += " A key is available.";
        }

        void SetBusy(bool busy)
        {
            send.IsEnabled = key.IsEnabled = !busy;
            providers.IsEnabled = model.IsEnabled = !busy && history.Count == 0;
            clear.IsEnabled = !busy && history.Count > 0;
            prompt.IsReadOnly = busy;
        }

        void RenderConversation()
        {
            messages.Children.Clear();
            conversation.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (user, text) in history)
            {
                var line = new StackPanel();
                var name = user ? "You" : "Assistant";
                var tint = user ? accent : purple;
                line.Children.Add(Text(name, 10, tint, true));
                var message = Field(name + " message", 0);
                message.Text = text;
                message.IsReadOnly = true;
                message.TextWrapping = TextWrapping.Wrap;
                message.Background = System.Windows.Media.Brushes.Transparent;
                message.BorderBrush = System.Windows.Media.Brushes.Transparent;
                message.Padding = new Thickness(0, 3, 0, 0);
                line.Children.Add(message);
                var card = Card(line, tint);
                card.Padding = new Thickness(8, 6, 8, 6);
                card.Margin = new Thickness(user ? 27 : 0, 0, user ? 0 : 27, 5);
                messages.Children.Add(card);
            }
            conversation.ScrollToEnd();
        }

        async Task SendAsync()
        {
            var index = providers.SelectedIndex;
            var message = prompt.Text.Trim();
            var modelId = model.Text.Trim();
            var typed = key.Password.Trim();
            var apiKey = typed.Length == 0 ? ReadSessionKey(index) ?? EnvironmentKey(index) : typed;
            if (message.Length == 0 || modelId.Length == 0 || string.IsNullOrWhiteSpace(apiKey))
            {
                status.Text = "Enter a message, model ID, and API key.";
                return;
            }
            if (apiKey.Any(char.IsWhiteSpace) || modelId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '/' or ':')))
            {
                status.Text = "Check the model ID and API key.";
                return;
            }
            var turns = history.TakeLast(16).Append((User: true, Text: message)).ToArray();
            SetBusy(true);
            status.Text = $"Waiting for {providers.SelectedItem}…";
            try
            {
                var response = await TextModelClient.SendChat(index == 0, modelId, apiKey, turns, cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
                if (string.IsNullOrWhiteSpace(response))
                {
                    status.Text = "The provider returned an empty reply.";
                    return;
                }
                if (typed.Length > 0)
                    RememberSessionKey(index, key);
                history.Clear();
                history.AddRange(turns);
                history.Add((false, response.Trim()));
                prompt.Clear();
                RenderConversation();
                SetDisclosure();
            }
            catch (HttpRequestException error)
            {
                status.Text = error.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Check that your API key can access this model.",
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound => "Check the model ID and provider settings.",
                    HttpStatusCode.TooManyRequests => "The provider is rate limiting requests. Try again later.",
                    _ => "Could not reach the provider. Try again."
                };
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                status.Text = "The provider took too long to respond.";
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
            {
                status.Text = "The provider did not return a readable reply.";
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    SetBusy(false);
                    prompt.Focus();
                }
            }
        }
    }
}
