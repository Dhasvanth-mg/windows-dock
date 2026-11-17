using System.IO;
using System.Threading;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DockPokerReviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-poker-test-" + Guid.NewGuid().ToString("N"));

    public DockPokerReviewTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void PracticeHandAndReplayLeaveOtherModesUnchanged()
    {
        DockCasino.Update(wallet => wallet with { Balance = 210 });
        var tournament = DockPokerTournament.Update(saved => DockPokerTournament.Start(saved, PokerFormat.Quick)).Current!;
        var opened = DockPokerPractice.Update(wallet => DockCasino.BeginHoldem(wallet, 10,
            [2, 0, 17, 12, 3, 11, 10, 9, 4, 16, 5, 32]));
        var held = DockPokerPractice.Load().Round!.Poker!;
        Assert.Equal(995, opened.Balance);
        Assert.Throws<ArgumentException>(() => DockPokerPractice.Update(DockPokerPractice.Refill));
        DockPokerPractice.Update(wallet => DockCasino.ContinueHoldem(wallet, held.Id, held.Turn, HoldemMove.Fold));
        var saved = DockPokerPractice.Load();
        var entry = Assert.Single(saved.History);
        Assert.Equal(5, entry.Stake);
        var replay = entry.Replay!;
        Assert.Equal(new[] { 0, 12 }, replay.Player);
        Assert.Empty(replay.Board);
        Assert.Null(replay.RevealedRival);
        Assert.All(replay.Steps, item => Assert.Equal(0, item.BoardCount));
        Assert.Equal(15, replay.Steps[0].Pot);
        Assert.Equal(210, DockCasino.Load().Balance);
        Assert.Equal(tournament.Id, DockPokerTournament.Load().Current!.Id);
        Assert.Equal(0, DockPokerTournament.Load().Current!.Hands);
    }

    [Fact]
    public void ShowdownStoresPublicStreetsAndTournamentHand()
    {
        var started = DockPokerTournament.Start(new(null, []), PokerFormat.Quick);
        var initial = started.Current!;
        var shortStack = started with { Current = initial with { RivalChips = 5, PlayerChips = 235 } };
        var final = DockPokerTournament.Deal(shortStack, initial.Id, 0,
            [2, 0, 17, 12, 3, 11, 10, 9, 4, 16, 5, 32]);
        var hand = Assert.Single(final.Replays!).Replay!;
        Assert.Equal(new[] { 0, 3, 4, 5, 5 }, hand.Steps.Select(step => step.BoardCount));
        Assert.Equal(new[] { 2, 17 }, hand.RevealedRival);
        Assert.Equal(TournamentStage.Final, final.Current!.Stage);
    }

    [Fact]
    public void EquityUsesPublicInformationAndHandlesExactSplits()
    {
        var tiedRiver = DockPokerPractice.Estimate([13, 14], [0, 9, 10, 11, 12], CancellationToken.None);
        Assert.True(tiedRiver.Exact);
        Assert.Equal(990, tiedRiver.Trials);
        Assert.Equal(990, tiedRiver.Ties);
        Assert.Equal(.5, tiedRiver.Equity);
        var nuts = DockPokerPractice.Estimate([0, 12], [11, 10, 9, 16, 32], CancellationToken.None);
        Assert.Equal(1, nuts.Equity);
        var sample = DockPokerPractice.Estimate([0, 12], [11, 10, 9], CancellationToken.None);
        Assert.False(sample.Exact);
        Assert.Equal(750, sample.Trials);
        Assert.Equal(sample, DockPokerPractice.Estimate([0, 12], [11, 10, 9], CancellationToken.None));
        Assert.Throws<ArgumentException>(() => DockPokerPractice.Estimate([0, 0], [], CancellationToken.None));
    }
}
