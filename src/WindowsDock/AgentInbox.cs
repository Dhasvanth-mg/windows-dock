namespace WindowsDock;

internal sealed record InboxItem(AgentProvider Provider, string ChatId, string? DesktopId, string Title, string Project,
    AgentState State, DateTimeOffset At, Guid? ApprovalId = null)
{
    internal (AgentProvider, string, AgentState, DateTimeOffset, Guid?) Key => (Provider, ChatId, State, At, ApprovalId);
}

internal static class AgentInbox
{
    private const int Limit = 80;
    internal static InboxItem[] Merge(InboxItem[] previous, IReadOnlyList<AgentSession> sessions,
        AgentApproval[] approvals, DateTimeOffset? quotaSince, DateTimeOffset now)
    {
        var fresh = sessions.Where(chat => chat.State is AgentState.Finished or AgentState.QuotaReached ||
                chat.State == AgentState.Attention && !approvals.Any(item => item.Provider == chat.Provider && item.SessionId == chat.Id))
            .Select(chat => new InboxItem(chat.Provider, chat.Id, chat.DesktopId, chat.Title, chat.Project, chat.State, chat.Updated));
        var requests = approvals.Select(request =>
        {
            var chat = sessions.FirstOrDefault(item => item.Provider == request.Provider && item.Id == request.SessionId);
            return new InboxItem(request.Provider, request.SessionId, chat?.DesktopId, chat?.Title ?? request.Project,
                request.Project, AgentState.Attention, request.Expires.AddSeconds(-30), request.Id);
        });
        var merged = previous.Concat(fresh).Concat(requests);
        if (quotaSince.HasValue)
            merged = merged.Append(new(AgentProvider.Codex, "", null, "Codex account limit reached", "", AgentState.QuotaReached, quotaSince.Value));
        return merged
            .Where(item => item.At >= now.AddDays(-3) && item.At <= now.AddMinutes(1))
            .DistinctBy(item => item.Key)
            .OrderByDescending(item => item.At).Take(Limit).ToArray();
    }
}
