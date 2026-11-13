using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace WindowsDock;

public partial class MainWindow
{
    private Window? assistant;

    private void AddAssistantWidget() => AddButton("Assistant", "✦", OpenAssistant);

    private void OpenAssistant()
    {
        if (assistant != null)
        {
            assistant.Activate();
            return;
        }

        var panel = new StackPanel { Margin = new Thickness(18) };
        var provider = new ComboBox { ItemsSource = new[] { "Gemini", "Groq" }, SelectedIndex = 0 };
        var model = new TextBox { MaxLength = 100 };
        var key = new PasswordBox { MaxLength = 500 };
        var transcript = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var prompt = new TextBox { TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 70, MaxLength = 8000 };
        Add("Provider", provider);
        Add("Model ID", model);
        Add("API key for this window", key);
        Add("Conversation", transcript);
        Add("Message", prompt);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Send shares this conversation with the selected provider." };
        panel.Children.Add(status);
        var send = new Button { Content = "Send", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10) };
        panel.Children.Add(send);

        var history = new List<(bool User, string Text)>();
        var lifetime = new System.Threading.CancellationTokenSource();
        provider.SelectionChanged += (_, _) => ClearConversation();
        model.TextChanged += (_, _) => ClearConversation();
        key.PasswordChanged += (_, _) => ClearConversation();

        send.Click += async (_, _) =>
        {
            var text = prompt.Text.Trim();
            var modelId = model.Text.Trim();
            if (text.Length == 0 || modelId.Length == 0 || key.Password.Length == 0)
            {
                status.Text = "Enter a model ID, API key, and message.";
                return;
            }
            var requestHistory = history.TakeLast(16).Append((User: true, Text: text)).ToArray();
            send.IsEnabled = provider.IsEnabled = model.IsEnabled = key.IsEnabled = false;
            status.Text = "Waiting for a response…";
            try
            {
                var response = await TextModelClient.SendChat((string)provider.SelectedItem == "Gemini", modelId,
                    key.Password.Trim(), requestHistory, lifetime.Token);
                history.Clear();
                history.AddRange(requestHistory);
                history.Add((false, response));
                transcript.Text = string.Join("\n\n", history.Select(turn => $"{(turn.User ? "You" : "Assistant")}\n{turn.Text}"));
                transcript.ScrollToEnd();
                if (prompt.Text.Trim() == text)
                    prompt.Clear();
                status.Text = "";
            }
            catch (HttpRequestException error)
            {
                status.Text = error.StatusCode is { } code
                    ? $"Provider returned HTTP {(int)code}. Check the model, key, and usage limits."
                    : "Could not reach the provider.";
            }
            catch (OperationCanceledException) { status.Text = "Request canceled or timed out."; }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                status.Text = "The provider did not return a usable response.";
            }
            finally { send.IsEnabled = provider.IsEnabled = model.IsEnabled = key.IsEnabled = true; }
        };

        var window = new Window { Title = "Dock Assistant", Content = panel, Width = 430,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true, Owner = this, WindowStartupLocation = WindowStartupLocation.Manual };
        assistant = window;
        window.Loaded += (_, _) =>
        {
            var area = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var anchor = PointToScreen(new Point(ActualWidth / 2, 0));
            var x = (int)(anchor.X - window.ActualWidth * dpi.DpiScaleX / 2);
            var y = (int)(anchor.Y - (window.ActualHeight + 10) * dpi.DpiScaleY);
            SetWindowPos(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0,
                Math.Max(area.Left, x), Math.Max(area.Top, y), 0, 0, 0x15);
        };
        window.Closed += (_, _) =>
        {
            lifetime.Cancel();
            lifetime.Dispose();
            key.Clear();
            assistant = null;
        };
        window.Show();

        void Add(string name, Control control)
        {
            panel.Children.Add(new TextBlock { Text = name });
            control.Margin = new Thickness(0, 2, 0, 9);
            System.Windows.Automation.AutomationProperties.SetName(control, name);
            panel.Children.Add(control);
        }

        void ClearConversation()
        {
            history.Clear();
            transcript.Clear();
        }
    }
}
