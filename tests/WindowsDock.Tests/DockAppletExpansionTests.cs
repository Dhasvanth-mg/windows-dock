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

    [Fact]
    public void LifeChoicesCreatePersistentFamilyLinksAndMemories()
    {
        var real = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        var world = DockLife.Create("Sam", "Riverside", real, fixedSeed: 41);
        var guest = world.People[0];
        world = world with { People = [guest] };
        Assert.DoesNotContain(DockLife.SocialChoices(world, guest), item => item.Command == "date");
        Assert.Throws<ArgumentException>(() => DockLife.Act(world with { Location = "Market District" }, "talk", guest.Id));
        foreach (var action in new[] { "help", "help", "date", "date", "become partners", "date", "marry", "adopt" })
            world = DockLife.Act(world, action, guest.Id);
        Assert.True(world.Married);
        Assert.Equal(64, world.Cash);
        Assert.Equal(2, world.At.Hour);
        var child = world.People.Single(person => person.Id != guest.Id);
        Assert.Contains(DockLife.Relationships(world, world.Id), item => item.Id == guest.Id && item.Kind == "Spouse");
        Assert.Contains(DockLife.Relationships(world, guest.Id), item => item.Id == child.Id && item.Kind == "Child");
        Assert.Equal(2, DockLife.Relationships(world, child.Id).Count(item => item.Kind == "Parent"));
        Assert.Contains(world.People.Single(person => person.Id == guest.Id).Memories!, item => item.Text.Contains("got married"));
        DockLife.SaveNew(world);
        var saved = Assert.Single(DockLife.Load());
        Assert.Equal(child.Id, saved.People.Single(person => person.Id != guest.Id).Id);
        Assert.Equal(guest.Id, saved.Partner);
        var broken = DockLife.Act(saved, "break up", guest.Id);
        Assert.Null(broken.Partner);
        Assert.False(broken.Married);
        Assert.Contains(DockLife.Relationships(broken, broken.Id), item => item.Id == child.Id && item.Kind == "Child");
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
