using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static WindowsDock.AgentSessions;

namespace WindowsDock;

internal static class CodingSessionCopies
{
    private const string FileName = "coding-session-copies.json";
    private sealed record Copy(AgentProvider Provider, string Source, string Id);

    internal static string? SavedId(AgentSession session) => (DockData.Read<Copy[]>(FileName) ?? [])
        .FirstOrDefault(copy => copy.Provider == session.Provider && copy.Source == session.Id &&
            IsDifferentSession(copy.Id, session.Id))?.Id;

    internal static bool IsDifferentSession(string? candidate, string source) =>
        Guid.TryParseExact(candidate, "D", out var id) && Guid.TryParseExact(source, "D", out var parent) && id != parent;

    internal static async Task<string> SendAsync(AgentSession session, string draft, string model, string effort,
        CancellationToken token)
    {
        if (!Guid.TryParseExact(session.Id, "D", out _))
            throw new CodingControlException("This chat doesn't have a local session to copy.");
        var input = CodingSessionControls.Input(draft);
        var (chosenModel, chosenEffort) = CodingSessionControls.Choices(session.Provider, model, effort);
        var copyId = SavedId(session);
        if (session.Provider == AgentProvider.Claude)
        {
            var folder = session.ProjectPath;
            if (folder == null || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(folder))
                throw new CodingControlException("This Claude chat needs its original local project folder to continue a copy.");
            var (id, reply) = await CodingSummaryClient.ContinueClaudeCopyAsync(copyId ?? session.Id, copyId == null, folder,
                chosenModel, chosenEffort, draft.Trim(), token).ConfigureAwait(false);
            Save(session, id);
            return DockDeskspaces.Clip(reply, SummaryLimit);
        }

        using var server = CodingControlServer.StartCopyServer();
        await server.InitializeAsync(token).ConfigureAwait(false);
        var history = copyId == null
            ? await server.CallAsync("thread/fork", new
            {
                threadId = session.Id, excludeTurns = true, sandbox = "read-only", approvalPolicy = "never",
                developerInstructions = "Answer in text. Do not call tools or change files.", deferGoalContinuation = true
            }, token).ConfigureAwait(false)
            : await server.CallAsync("thread/resume", new
            {
                threadId = copyId, excludeTurns = true, sandbox = "read-only", approvalPolicy = "never",
                developerInstructions = "Answer in text. Do not call tools or change files."
            }, token).ConfigureAwait(false);
        var threadId = String(Property(history, "thread"), "id");
        if (!IsDifferentSession(threadId, session.Id) || copyId != null && threadId != copyId)
            throw new CodingControlException("Codex didn't confirm the separate session.");
        if (copyId == null)
            Save(session, threadId!);

        string? turnId = null, replyText = null, end = null;
        var started = await server.CallAsync("turn/start", new { threadId, input, model = chosenModel, effort = chosenEffort },
            token, OnEvent).ConfigureAwait(false);
        var startedId = String(Property(started, "turn"), "id");
        if (startedId == null || turnId != null && turnId != startedId)
            throw new CodingControlException("Codex didn't confirm the turn in the copy.");
        turnId = startedId;
        while (end == null)
            await server.ObserveAsync(OnEvent, token).ConfigureAwait(false);
        if (end != "completed")
            throw new CodingControlException("The copy didn't finish. Check the provider sign-in or usage and try again.");
        return replyText ?? "";

        void OnEvent(JsonElement value)
        {
            var method = String(value, "method");
            if (method is not ("item/completed" or "turn/completed"))
                return;
            var parameters = Property(value, "params");
            if (String(parameters, "threadId") != threadId)
                return;
            var turn = Property(parameters, "turn");
            var eventTurn = method == "turn/completed" ? String(turn, "id") : String(parameters, "turnId");
            if (eventTurn == null || turnId != null && eventTurn != turnId)
                return;
            turnId ??= eventTurn;
            if (method == "turn/completed")
            {
                end = String(turn, "status") ?? "failed";
                return;
            }
            var item = Property(parameters, "item");
            if (String(item, "type") == "agentMessage" && String(item, "phase") != "commentary" && String(item, "text") is { } answer)
                replyText = DockDeskspaces.Clip(answer, SummaryLimit);
        }
    }

    private static void Save(AgentSession session, string id) => DockData.Update<Copy[]>(FileName,
        current => [.. (current ?? []).Where(copy => copy.Provider != session.Provider || copy.Source != session.Id),
            new Copy(session.Provider, session.Id, id)]);
}
