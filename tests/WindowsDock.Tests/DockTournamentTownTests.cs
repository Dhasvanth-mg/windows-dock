using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DockTournamentTownTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-town-test-" + Guid.NewGuid().ToString("N"));

    public DockTournamentTownTests() => DockData.RootOverride = root;

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

    [Fact]
    public void ShiftsAndSchoolDaysFollowTheScheduledHour()
    {
        var world = DockLife.Create("Quinn", "Greenhills", DateTimeOffset.UnixEpoch, fixedSeed: 41);
        var teacher = world.People[0];
        var student = world.People[1] with { Job = "Student", Birth = new DateOnly(2014, 1, 1) };
        world = world with { People = [teacher, student] };
        var works = DockLife.Duty(teacher, DateOnly.FromDateTime(world.At.UtcDateTime), DockLife.Venues(world.World))!;
        Assert.Equal("Public school", DockLife.Presence(world with { At = world.At.AddHours(works.Starts - 8) }, teacher).Place);
        var byThree = DockLife.Advance(world, world.At.AddHours(7));
        Assert.Equal(1, byThree.People[1].SchoolDays);
        Assert.Equal(0, byThree.People[0].Worked);
        var after = DockLife.Advance(byThree, world.At.AddHours(works.Ends - 8));
        Assert.Equal(1, after.People[0].Worked);
        Assert.Equal(teacher.Savings + 22, after.People[0].Savings);
        Assert.Equal("At home", DockLife.Presence(after, after.People[0]).Activity);
        Assert.Throws<ArgumentException>(() => DockLife.Act(world with { At = world.At.AddHours(12) }, "buy groceries", null));
    }

    [Fact]
    public void OlderSaveDoesNotPayTheAlreadyCreditedShiftAgain()
    {
        var world = DockLife.Create("Quinn", "Greenhills", DateTimeOffset.UnixEpoch, fixedSeed: 41);
        var worker = world.People[0] with { Worked = 50 };
        var old = world with { At = world.At.AddHours(3), People = [worker], ScheduledShifts = false };
        var upgraded = DockLife.Advance(old, old.At.AddHours(8));
        Assert.True(upgraded.ScheduledShifts);
        Assert.Equal(50, upgraded.People[0].Worked);
        Assert.Equal(worker.Savings, upgraded.People[0].Savings);
        var next = DockLife.Advance(upgraded, upgraded.At.AddDays(1));
        Assert.Equal(51, next.People[0].Worked);
        Assert.Equal(worker.Savings + 22, next.People[0].Savings);
    }

    [Fact]
    public void SkipsAndReopeningPreserveTownFriendshipsAndGatherings()
    {
        var world = DockLife.Create("Quinn", "Greenhills", DateTimeOffset.UnixEpoch, fixedSeed: 41);
        var people = world.People.Take(4).Select(person => person with { Birth = new DateOnly(2004, 1, 1) }).ToArray();
        world = world with { People = people };
        var gather = DockLife.NextGathering(world.World, world.At);
        var start = new DateTimeOffset(gather.Date.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero);
        var atGathering = DockLife.Advance(world, start);
        Assert.True(DockLife.CanAttend(atGathering));
        var attended = DockLife.Act(atGathering, "attend gathering", null);
        Assert.Equal(start.AddHours(1), attended.At);
        Assert.False(DockLife.CanAttend(attended));
        Assert.Throws<ArgumentException>(() => DockLife.Act(attended, "attend gathering", null));
        Assert.Contains(attended.People, person => person.Affinity > 0);

        var first = DockLife.Advance(world, start.AddDays(98));
        var second = DockLife.Advance(atGathering, start.AddDays(98));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        var bond = first.TownBonds!.First(item => item.Meetings >= 2);
        var peopleGraph = DockLife.Relationships(first, bond.First);
        Assert.Contains(peopleGraph, item => item.Id == bond.Second);
        DockLife.SaveNew(first);
        Assert.Equal(bond.Meetings, DockLife.Load().Single().TownBonds!.Single(item => item.First == bond.First && item.Second == bond.Second).Meetings);
    }

    private static int[] Deck(bool playerWins = true) => playerWins
        ? [2, 0, 17, 12, 3, 11, 10, 9, 4, 16, 5, 32]
        : [0, 2, 12, 17, 3, 11, 10, 9, 4, 16, 5, 32];
}
