using System.IO;
using System.Windows.Threading;

namespace WindowsDock;

/// <summary>Where the dashboard was when it was put away: the page, which assistant and chat, and how far down.</summary>
internal sealed record DashboardViewState(string Page, int AssistantRole, Guid? Chat, double Scroll, DateTimeOffset Saved);

// The dashboard keeps its page while it is only hidden, but a hidden dashboard closes itself after a short idle spell
// to give its memory back. So the spot is written down when it hides and picked up again on the next open, and
// reopening lands exactly where you left, even in a fresh process.
public partial class DashboardWindow
{
    private const string ViewStateFile = "dashboard-view.json";
    private static readonly TimeSpan ViewStateLifetime = TimeSpan.FromHours(12);

    private void SaveViewState()
    {
        if (!pageInitialized || compactAgents || controllerPid <= 0)
            return;
        var chat = currentPage == DashboardPage.Assistant ? assistantConversations[assistantRole] : null;
        var state = new DashboardViewState(currentPage.ToString(), assistantRole,
            chat is { Turns.Count: > 0 } ? chat.Id : null, PageScroll.VerticalOffset, DateTimeOffset.UtcNow);
        try { DockData.Save(ViewStateFile, state); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Picks up the saved spot for a dashboard that has not shown a page yet. Returns the scroll offset to restore
    /// once the page is laid out, or null when there is nothing recent to resume.
    /// </summary>
    private double? RestoreViewState()
    {
        if (pageInitialized || controllerPid <= 0)
            return null;
        var state = DockData.Read<DashboardViewState>(ViewStateFile);
        if (state == null || DateTimeOffset.UtcNow - state.Saved > ViewStateLifetime ||
            !Enum.TryParse<DashboardPage>(state.Page, out var page) || !Enum.IsDefined(page))
            return null;
        lastDashboardPage = page;
        if (state.AssistantRole >= 0 && state.AssistantRole < assistantConversations.Length)
            assistantRole = state.AssistantRole;
        if (page == DashboardPage.Assistant && state.Chat is { } id &&
            assistantConversations[assistantRole].Turns.Count == 0)
        {
            try
            {
                if (DockChats.Load().FirstOrDefault(item => item.Id == id && !item.Hidden) is { } saved)
                    assistantConversations[assistantRole] = ResumedConversation(saved);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return Math.Max(0, state.Scroll);
    }

    private static AssistantConversation ResumedConversation(SavedChat saved)
    {
        var source = Array.FindIndex(AssistantSources, item => item.Provider == saved.Provider);
        var conversation = new AssistantConversation { Id = saved.Id, SourceIndex = Math.Max(0, source), Saved = saved };
        conversation.Turns.AddRange(saved.Turns);
        return conversation;
    }

    private void RestoreScroll(double? offset)
    {
        if (offset is not > 0)
            return;
        Dispatcher.InvokeAsync(() => PageScroll.ScrollToVerticalOffset(offset.Value), DispatcherPriority.Loaded);
    }
}
