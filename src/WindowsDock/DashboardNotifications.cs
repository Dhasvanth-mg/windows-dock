using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowNotificationWidget()
    {
        var (window, content, cancellation) = CreateDockPanel("Notifications", "", 466, 0xEA8F, pink, widget: true);
        var status = Text("", 11, subdued);
        status.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(status);
        var list = new StackPanel();
        content.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var fetch = ActionButton("Read notifications");
        fetch.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(fetch);
        var digest = AccentButton("Summarize selected", 0xEAB7, pink);
        digest.Margin = new Thickness(0, 0, 5, 4);
        actions.Children.Add(digest);
        actions.Children.Add(ActionButton("Paste text", () => ShowTextSummary("Notification summary", "", "Notifications to summarize", "")));
        content.Children.Add(actions);
        var notifications = new List<(string Text, CheckBox Check)>();
        fetch.Click += (_, _) => _ = LoadAsync(true);
        digest.Click += (_, _) => Summarize();
        window.Show();
        _ = LoadAsync(false);

        async Task LoadAsync(bool requestPermission)
        {
            fetch.IsEnabled = digest.IsEnabled = false;
            notifications.Clear();
            list.Children.Clear();
            try
            {
                var listener = UserNotificationListener.Current;
                var access = listener.GetAccessStatus();
                if (access == UserNotificationListenerAccessStatus.Unspecified && requestPermission)
                    access = await listener.RequestAccessAsync().AsTask(cancellation);
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    status.Text = access == UserNotificationListenerAccessStatus.Denied
                        ? "Windows notification access is off. You can still paste text to summarize."
                        : "Allow Windows notification access to see recent alerts.";
                    return;
                }
                var items = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellation);
                foreach (var item in items.OrderByDescending(item => item.CreationTime).Take(18))
                {
                    try
                    {
                        var binding = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                        var text = binding == null ? "" : string.Join(" · ", binding.GetTextElements().Select(element => element.Text));
                        if (string.IsNullOrWhiteSpace(text))
                            continue;
                        var title = item.AppInfo?.DisplayInfo.DisplayName ?? "Windows";
                        var display = title + " · " + (text.Length > 170 ? text[..169] + "…" : text);
                        var labels = new StackPanel();
                        labels.Children.Add(Text(title, 11, strong: true));
                        var label = Text(text.Length > 170 ? text[..169] + "…" : text, 11, subdued);
                        label.TextWrapping = TextWrapping.Wrap;
                        labels.Children.Add(label);
                        var check = new CheckBox { Content = labels, ToolTip = title + "\n" + text };
                        System.Windows.Automation.AutomationProperties.SetName(check, display);
                        notifications.Add((title + ": " + text, check));
                        var card = Card(check, pink);
                        card.Padding = new Thickness(8, 6, 8, 6);
                        card.Margin = new Thickness(0, 0, 0, 5);
                        list.Children.Add(card);
                        check.Checked += (_, _) => UpdateDigest();
                        check.Unchecked += (_, _) => UpdateDigest();
                    }
                    catch (Exception error) when (error is COMException or UnauthorizedAccessException) { }
                }
                status.Text = notifications.Count == 0 ? "No readable notifications in Windows." :
                    "Select the alerts to include in a summary.";
                status.Margin = new Thickness(0, 0, 0, 8);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                status.Text = "Windows notification access is unavailable here. Paste text to summarize it.";
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    fetch.Content = notifications.Count > 0 ? "Refresh" : "Read notifications";
                    System.Windows.Automation.AutomationProperties.SetName(fetch, (string)fetch.Content);
                    fetch.IsEnabled = true;
                    UpdateDigest();
                }
            }
        }

        void UpdateDigest() => digest.IsEnabled = notifications.Any(item => item.Check.IsChecked == true);

        void Summarize()
        {
            var text = string.Join("\n\n", notifications.Where(item => item.Check.IsChecked == true).Select(item => item.Text));
            if (text.Length > AgentSessions.SummaryLimit)
            {
                var length = char.IsHighSurrogate(text[AgentSessions.SummaryLimit - 1])
                    ? AgentSessions.SummaryLimit - 1 : AgentSessions.SummaryLimit;
                text = text[..length];
            }
            ShowTextSummary("Notification summary", "", "Notifications to summarize", text);
        }
    }
}
