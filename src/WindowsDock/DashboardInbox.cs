using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowActivityInbox()
    {
        var (window, content, cancellation) = CreateDockPanel("Activity inbox", "", 492, 0xEA8F, pink, widget: true);
        BuildPinnedActivity(window, content, cancellation);
        window.Show();
    }

    private void BuildPinnedActivity(Window window, StackPanel content, System.Threading.CancellationToken cancellation)
    {
        var seen = DockData.Read<DateTimeOffset>("activity-seen.json");
        var unreadOnly = false;
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var toggle = ActionButton("Unread only");
        var mark = AccentButton("Mark all read", 0xE73E, pink);
        var status = Text("", 11, subdued);
        status.Margin = new Thickness(0, 0, 0, 6);
        var rows = new StackPanel();
        actions.Children.Add(toggle);
        mark.Margin = new Thickness(5, 0, 0, 0);
        actions.Children.Add(mark);
        content.Children.Add(actions);
        content.Children.Add(status);
        content.Children.Add(rows);
        InboxItem[]? displayed = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => Render();
        cancellation.Register(timer.Stop);
        toggle.Click += (_, _) =>
        {
            unreadOnly = !unreadOnly;
            toggle.Content = unreadOnly ? "Show all" : "Unread only";
            System.Windows.Automation.AutomationProperties.SetName(toggle, (string)toggle.Content);
            Render(force: true);
        };
        mark.Click += (_, _) =>
        {
            var latest = displayed?.FirstOrDefault()?.At ?? seen;
            try
            {
                DockData.Save("activity-seen.json", latest);
                seen = latest;
                Render(force: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "Could not save your read position.";
            }
        };
        Render();
        timer.Start();

        void Render(bool force = false)
        {
            var saved = DockData.Read<InboxItem[]>("activity-inbox.json") ?? [];
            if (!force && displayed != null && displayed.SequenceEqual(saved))
                return;
            displayed = saved;
            var unread = saved.Count(item => item.At > seen);
            status.Text = unread == 0 ? "You’re caught up." : $"{unread} unread";
            mark.IsEnabled = unread > 0;
            rows.Children.Clear();
            foreach (var item in saved.Where(item => !unreadOnly || item.At > seen))
            {
                var (label, tint) = StateBadge(item.State);
                var time = item.At.ToLocalTime().ToString("g");
                var detail = $"{ProviderName(item.Provider)} · {label} · {time}";
                var button = WidgetTile(new(item.Title, detail, "", item.State switch
                {
                    AgentState.Attention => 0xEA8F,
                    AgentState.QuotaReached => 0xE916,
                    _ => 0xE73E
                }, tint, () =>
                {
                    if (item.ChatId.Length == 0)
                    {
                        ShowAgentUsage();
                        return;
                    }
                    var current = RankedSessions(pendingApprovals).FirstOrDefault(chat =>
                        chat.Provider == item.Provider && chat.Id == item.ChatId);
                    if (current != null)
                        OpenPaletteChat(current);
                    else
                    {
                        window.Close();
                        OpenAgent(new(item.Provider, item.ChatId, item.Title, item.Project, item.State, label,
                            item.At, item.DesktopId));
                    }
                }));
                if (item.At > seen)
                    button.BorderBrush = AgentArt.Wash(tint, 115);
                button.Margin = new Thickness(0, 0, 0, 5);
                rows.Children.Add(button);
            }
        }
    }
}
