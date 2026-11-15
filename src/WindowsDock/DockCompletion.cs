using System.IO;
using System.Threading;

namespace WindowsDock;

internal static class DockCompletion
{
    internal const string Marker = "agent-completion.json";

    internal static void Signal(int controllerPid)
    {
        try { DockData.Save(Marker, DateTimeOffset.UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        if (controllerPid > 0 && EventWaitHandle.TryOpenExisting(
            AgentGrid.SessionObject(controllerPid, "AgentCompletion"), out var completion))
            using (completion) completion.Set();
    }
}
