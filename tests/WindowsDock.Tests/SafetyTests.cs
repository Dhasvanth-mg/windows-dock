using System.IO;
using System.Text.Json;
using WindowsDock;
using Xunit;

namespace WindowsDock.Tests;

public sealed class SafetyTests
{
    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("MY_API_KEY")]
    [InlineData("db_password")]
    [InlineData("AWS_REGION")]
    [InlineData("AZURE_CLIENT_ID")]
    [InlineData("GOOGLE_APPLICATION_CREDENTIALS")]
    [InlineData("SERVICE_SECRET")]
    [InlineData("DEPLOY_PAT")]
    [InlineData("CODEX_THREAD_ID")]
    public void SecretLookingVariablesAreScrubbed(string name) => Assert.True(DockCodingJob.IsSecretVariable(name));

    [Theory]
    [InlineData("PATH")]
    [InlineData("USERPROFILE")]
    [InlineData("GIT_AUTHOR_NAME")]
    [InlineData("SSH_AUTH_SOCK")]
    [InlineData("TEMP")]
    public void OrdinaryVariablesAreKept(string name) => Assert.False(DockCodingJob.IsSecretVariable(name));

    [Fact]
    public void WorkspaceMustBeAGitRootAndNotASystemFolder()
    {
        var project = Path.Combine(Path.GetTempPath(), "windows-dock-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var plain = Path.Combine(Path.GetTempPath(), "windows-dock-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(plain);
        try
        {
            Assert.True(DockCodingJob.IsWorkspace(project));
            Assert.True(DockCodingJob.IsWorkspace(project + Path.DirectorySeparatorChar));
            Assert.False(DockCodingJob.IsWorkspace(plain));
            Assert.False(DockCodingJob.IsWorkspace("relative\\path"));
            Assert.False(DockCodingJob.IsWorkspace(@"\\server\share"));
            Assert.False(DockCodingJob.IsWorkspace(Path.GetPathRoot(project)!));
            Assert.False(DockCodingJob.IsWorkspace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            Assert.False(DockCodingJob.IsWorkspace(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        }
        finally
        {
            Directory.Delete(project, true);
            Directory.Delete(plain, true);
        }
    }

    [Fact]
    public void RewritingJsonWithCommentsIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => AgentHookSetup.EnsureRewritable("{\n // keep me\n \"model\": \"x\" }"));
        Assert.Throws<InvalidOperationException>(() => AgentHookSetup.EnsureRewritable("{ /* c */ \"model\": \"x\" }"));
    }

    [Fact]
    public void RewritingPlainJsonIsAllowed()
    {
        AgentHookSetup.EnsureRewritable(null);
        AgentHookSetup.EnsureRewritable("{ \"model\": \"x\", }");
        AgentHookSetup.EnsureRewritable("{}");
    }

    [Theory]
    [InlineData("set_volume", "{\"percent\":101}")]
    [InlineData("set_volume", "{\"percent\":\"loud\"}")]
    [InlineData("start_timer", "{\"minutes\":0}")]
    [InlineData("add_reminder", "{\"title\":\"x\",\"date\":\"tomorrow\"}")]
    [InlineData("set_radio", "{\"radio\":\"wifi\"}")]
    [InlineData("delete_everything", "{}")]
    public void InvalidDockActionsAreRejectedBeforeAnythingRuns(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(() => DockActions.Plan(name, document.RootElement, false));
    }

    [Fact]
    public void ValidDockActionIsPlannedWithoutRunning()
    {
        using var document = JsonDocument.Parse("{\"percent\":40}");
        var plan = DockActions.Plan("set_volume", document.RootElement, false);
        Assert.Equal("Set output volume to 40%", plan.Preview);
    }
}
