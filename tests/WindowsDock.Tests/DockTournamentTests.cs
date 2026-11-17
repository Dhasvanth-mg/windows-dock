using System.IO;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DockTournamentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-town-test-" + Guid.NewGuid().ToString("N"));

    public DockTournamentTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void TournamentResumesHandAndKeepsCashWalletSeparate()
    {
        var cash = DockCasino.Update(wallet => wallet with { Balance = 432.50m });
        var started = DockPokerTournament.Update(saved => DockPokerTournament.Start(saved, PokerFormat.Quick));
        var bracket = started.Current!;
        Assert.Contains(bracket.FinalOpponent, new[] { bracket.OtherFirst, bracket.OtherSecond });
        Assert.NotEqual(bracket.FirstOpponent, bracket.FinalOpponent);
        Assert.True(bracket.OtherMatchHands > 0);

        var raised = DockPokerTournament.Update(saved => saved with { Current = bracket with { TableHands = 3, Hands = 3 } });
        Assert.Equal(20, DockPokerTournament.BigBlind(raised.Current!.Format, raised.Current.TableHands));
        DockPokerTournament.Update(saved => DockPokerTournament.Deal(saved, bracket.Id, 3, Deck()));
        var live = DockPokerTournament.Load().Current!;
        var hand = live.Hand!.Poker!;
        Assert.Equal(240, live.PlayerChips + live.RivalChips + hand.Pot);
        Assert.Equal(20, hand.Blind);
        Assert.Throws<ArgumentException>(() => DockPokerTournament.Update(saved => DockPokerTournament.Deal(saved, bracket.Id, 3)));
        var after = DockPokerTournament.Update(saved => DockPokerTournament.Play(saved, live.Id, hand.Id, hand.Turn, HoldemMove.Fold, 0));
        Assert.Null(after.Current!.Hand);
        Assert.Equal(4, after.Current.Hands);
        Assert.Equal(240, after.Current.RivalChips + after.Current.PlayerChips);
        Assert.Throws<ArgumentException>(() => DockPokerTournament.Update(saved =>
            DockPokerTournament.Play(saved, bracket.Id, hand.Id, hand.Turn, HoldemMove.Fold, 0)));
        Assert.Equal(cash.Balance, DockCasino.Load().Balance);
        Assert.Empty(DockCasino.Load().History);
    }

    [Fact]
    public void ShortStackShowdownAdvancesBracketAndAwardsOneMedal()
    {
        var started = DockPokerTournament.Start(new(null, []), PokerFormat.Quick);
        var initial = started.Current!;
        var semifinal = started with { Current = initial with { RivalChips = 5, PlayerChips = 235 } };
        var final = DockPokerTournament.Deal(semifinal, initial.Id, 0, Deck());
        Assert.Equal(TournamentStage.Final, final.Current!.Stage);
        Assert.Equal(1, final.Current.Hands);
        Assert.Equal(120, final.Current.PlayerChips);
        Assert.Equal(120, final.Current.RivalChips);
        Assert.Equal(initial.FinalOpponent, final.Current.Opponent);
        Assert.Empty(final.Results);
        var nearFinish = final with { Current = final.Current with { RivalChips = 5, PlayerChips = 235 } };
        var winner = DockPokerTournament.Deal(nearFinish, initial.Id, 1, Deck());
        Assert.Equal(TournamentStage.Champion, winner.Current!.Stage);
        Assert.Equal(240, winner.Current.PlayerChips);
        Assert.Equal(2, Assert.Single(winner.Results).Hands);
        Assert.Throws<ArgumentException>(() => DockPokerTournament.Withdraw(winner, initial.Id));

        var nearLoss = final with { Current = final.Current with { RivalChips = 235, PlayerChips = 5 } };
        var lost = DockPokerTournament.Deal(nearLoss, initial.Id, 1, Deck(playerWins: false));
        Assert.Equal(TournamentStage.Eliminated, lost.Current!.Stage);
        Assert.Equal(initial.FinalOpponent, Assert.Single(lost.Results).Champion);
    }

    private static int[] Deck(bool playerWins = true) => playerWins
        ? [2, 0, 17, 12, 3, 11, 10, 9, 4, 16, 5, 32]
        : [0, 2, 12, 17, 3, 11, 10, 9, 4, 16, 5, 32];
}
