using System.Net;
using System.Net.Http;
using System.Web;
using Xunit;

namespace WindowsDock.Tests;

public sealed class PlanSignInTests
{
    [Fact]
    public async Task ClosingPanelLeavesLoopbackSignInAvailableAndStateIsStillChecked()
    {
        using var dock = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var panel = new CancellationTokenSource();
        var browser = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var signIn = new PlanSignIn();
        var attempt = signIn.Start(null, dock.Token, async (_, token) =>
        {
            var reply = await ChatGptPlan.AuthorizeAsync(null, "urn:uuid:test", url => browser.SetResult(new Uri(url)), token);
            return new(reply.Client, reply.Code, "", "", "", default);
        });
        var authorization = HttpUtility.ParseQueryString((await browser.Task.WaitAsync(dock.Token)).Query);
        var redirect = authorization["redirect_uri"];
        Assert.NotNull(redirect);
        Assert.True(new Uri(redirect).IsLoopback);
        Assert.Equal("S256", authorization["code_challenge_method"]);

        var panelObserver = attempt.WaitAsync(panel.Token);
        panel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => panelObserver);
        Assert.Same(attempt, signIn.Active);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var rejected = await http.GetAsync(redirect + "?state=invalid&code=synthetic&client_id=oaiapp_test", dock.Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var accepted = await http.GetAsync(redirect + "?state=" + Uri.EscapeDataString(authorization["state"]!) +
            "&code=synthetic&client_id=oaiapp_test", dock.Token);
        Assert.True(accepted.IsSuccessStatusCode);
        Assert.Equal("synthetic", (await attempt).Subject);
    }

    [Fact]
    public async Task CancelStopsTheActiveAttemptAndOnlyOneBrowserFlowCanBeInFlight()
    {
        using var dock = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var signIn = new PlanSignIn();
        var attempt = signIn.Start(null, dock.Token, async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        });
        Assert.Same(attempt, signIn.Start(null, dock.Token, (_, _) => throw new InvalidOperationException()));
        signIn.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Null(signIn.Active);
    }
}
