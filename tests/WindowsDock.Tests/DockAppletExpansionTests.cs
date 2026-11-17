using System.IO;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DockAppletExpansionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-applet-test-" + Guid.NewGuid().ToString("N"));

    public DockAppletExpansionTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void HeadsUpStreetOrderAndWalletSurviveReload()
    {
        var opened = DockCasino.Update(wallet => DockCasino.BeginHoldem(wallet, 10, Deck([2, 20, 32, 47, 38])));
        var first = opened.Round!.Poker!;
        Assert.Equal(995, opened.Balance);
        Assert.True(first.PlayerOnButton);
        Assert.Equal(new HoldemBet(5, 20, 1000, true), DockCasino.HoldemLimits(first));
        Assert.Throws<ArgumentException>(() => DockCasino.Update(wallet =>
            DockCasino.ContinueHoldem(wallet, first.Id, first.Turn, HoldemMove.Raise, 19)));
        var flop = Play(HoldemMove.Call);
        Assert.Equal(3, flop.Round!.Point);
        Assert.Equal(990, flop.Balance);
        Assert.Equal(0, DockCasino.HoldemLimits(flop.Round.Poker!).Call);
        Assert.Throws<ArgumentException>(() => DockCasino.Update(wallet =>
            DockCasino.ContinueHoldem(wallet, first.Id, first.Turn, HoldemMove.Fold)));
        Assert.Equal(4, Play(HoldemMove.Check).Round!.Point);
        Assert.Equal(5, Play(HoldemMove.Check).Round!.Point);
        var result = Play(HoldemMove.Check);
        Assert.Null(result.Round);
        Assert.Equal(1010, result.Balance);
        Assert.Equal(20, Assert.Single(result.History).Returned);
        Assert.Contains(result.History[0].Moves!, line => line.StartsWith("River", StringComparison.Ordinal));
        Assert.Equal(1, CasinoStats.For(result).Rivals[HoldemRival.Mara].Wins);
        var second = DockCasino.Update(wallet => DockCasino.BeginHoldem(wallet, 10, Deck([2, 20, 32, 47, 38])));
        Assert.False(second.Round!.Poker!.PlayerOnButton);
    }

    [Fact]
    public void AllInTieAndUncalledRaiseSettleExactlyOnce()
    {
        var wallet = new CasinoWallet(10, 10, null, []);
        var royal = DockCasino.BeginHoldem(wallet, 10, Deck([0, 9, 10, 11, 12], player: [22, 23], opponent: [1, 17]));
        var first = royal.Round!.Poker!;
        Assert.False(DockCasino.HoldemLimits(first).CanRaise);
        var split = DockCasino.ContinueHoldem(royal, first.Id, first.Turn, HoldemMove.Call);
        Assert.Null(split.Round);
        Assert.Equal(10, split.Balance);
        Assert.Equal("Split pot", split.History[0].Detail.Split(" · ")[0]);
        Assert.Equal(1, CasinoStats.For(split).Overall.Pushes);
        Assert.Contains(split.History[0].Moves!, move => move.StartsWith("River", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => DockCasino.ContinueHoldem(split, first.Id, first.Turn, HoldemMove.Call));

        var opened = DockCasino.BeginHoldem(wallet with { Balance = 100 }, 10, Deck([2, 20, 32, 47, 38]));
        var hand = opened.Round!.Poker!;
        var settled = DockCasino.ContinueHoldem(opened, hand.Id, hand.Turn, HoldemMove.Raise, 100);
        Assert.Null(settled.Round);
        Assert.Equal(110, settled.Balance);
        Assert.Equal(10, settled.History[0].Stake);
        Assert.Equal(20, settled.History[0].Returned);
        Assert.Contains("90 uncalled chips returned", settled.History[0].Detail);
    }

    [Fact]
    public void StatisticsInheritPriorHistoryAndOutlastTheLedger()
    {
        var old = new CasinoEntry(CasinoGame.Blackjack, 10, 20, 1010, "Existing round", DateTimeOffset.UnixEpoch);
        var wallet = new CasinoWallet(1010, 1000, null, [old]);
        for (var index = 0; index < 520; index++)
            wallet = DockCasino.Begin(wallet, CasinoGame.Slots, 1, "", "");
        var last = DockCasino.Update(_ => DockCasino.Claim(wallet, new(2026, 11, 17)));
        Assert.Equal(500, last.History.Length);
        Assert.DoesNotContain(last.History, entry => entry == old);
        var statistics = CasinoStats.For(DockCasino.Load());
        Assert.Equal(521, statistics.Overall.Rounds);
        Assert.Equal(520, statistics.Tables[CasinoGame.Slots].Rounds);
        Assert.Equal(10, statistics.Tables[CasinoGame.Blackjack].Net);
        Assert.Equal(last.Balance - last.Initial - 100, statistics.Overall.Net);
        Assert.Contains("Table regular", statistics.Milestones());
    }

    private static CasinoWallet Play(HoldemMove move)
    {
        var state = DockCasino.Load().Round!.Poker!;
        return DockCasino.Update(wallet => DockCasino.ContinueHoldem(wallet, state.Id, state.Turn, move));
    }

    private static int[] Deck(int[] board, int[]? player = null, int[]? opponent = null)
    {
        player ??= [11, 25];
        opponent ??= [1, 17];
        int[] cards = [opponent[0], player[0], opponent[1], player[1], -1, board[0], board[1], board[2], -1, board[3], -1, board[4]];
        return [.. cards, .. Enumerable.Range(0, 52).Except(cards)];
    }
}
