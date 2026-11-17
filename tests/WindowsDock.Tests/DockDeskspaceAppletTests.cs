using System.IO;
using System.Text.Json;
using Xunit;

namespace WindowsDock.Tests;

[Collection("DockData")]
public sealed class DockDeskspaceAppletTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-desk-test-" + Guid.NewGuid().ToString("N"));

    public DockDeskspaceAppletTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void PeopleAndProjectCanvasesHaveIndependentContents()
    {
        var room = DockDeskspaces.Add("Studio");
        var network = DockDeskspaces.AddMap(room.Id, "Team");
        var person = DockDeskspaces.SavePerson(null, "Avery", "Runs reviews", "avery@example.test", "5550101", "https://example.test");
        var first = DockDeskspaces.Node(person.Name, "", -40, 20, person.Id);
        var second = DockDeskspaces.Node("Harper", "", 128, 170, DockDeskspaces.SavePerson(null, "Harper", "").Id);
        DockDeskspaces.ChangeMap(room.Id, network.Id, saved => DockDeskspaces.AddEdge(saved with { Nodes = [first, second] },
            first.Id, second.Id, "Project lead"));

        var canvas = DockDeskspaces.AddCanvas(room.Id, "Exploration");
        var sticky = DockCanvas.Create(DeskItemKind.Sticky, 11, 39, "Prototype");
        var chart = DockCanvas.Create(DeskItemKind.Chart, 120, 80, "Before,12\nAfter,-3");
        var arrow = DockCanvas.Create(DeskItemKind.Arrow, 0, 0) with { From = sticky.Id, To = chart.Id, Stroke = DeskStroke.Dashed };
        DockDeskspaces.ChangeCanvas(room.Id, canvas.Id, saved => saved with { Items = [sticky, chart, arrow] });
        var memo = DockNotes.Add(DockNotes.Sticky);
        DockDeskspaces.Change(room.Id, saved => saved with { Links = [DockDeskspaces.Link("", "https://example.test/board")], Notes = [memo.Id] });

        var reloaded = DockDeskspaces.Get(room.Id);
        Assert.Equal("Project lead", Assert.Single(Assert.Single(reloaded.Maps).Edges).Label);
        Assert.Equal("avery@example.test", DockDeskspaces.People().Single(contact => contact.Id == person.Id).Email);
        Assert.Equal("example.test", Assert.Single(reloaded.Links).Title);
        Assert.Equal(memo.Id, Assert.Single(reloaded.Notes));
        var restored = Assert.Single(reloaded.Canvases!);
        Assert.Equal(DeskStroke.Dashed, restored.Items[2].Stroke);
        Assert.Equal([("Before", 12d), ("After", -3d)], DockCanvas.Chart(restored.Items[1].Text));
        Assert.Equal(new[] { "one", "two", "" }, DockCanvas.Table("one\ttwo\t\r\n")[0]);
        DockDeskspaces.ChangeCanvas(room.Id, canvas.Id, saved => DockCanvas.Remove(saved, sticky.Id));
        DockDeskspaces.ChangeMap(room.Id, network.Id, saved => DockDeskspaces.RemoveNode(saved, first.Id));
        Assert.Equal(chart.Id, Assert.Single(Assert.Single(DockDeskspaces.Get(room.Id).Canvases!).Items).Id);
        Assert.Empty(Assert.Single(DockDeskspaces.Get(room.Id).Maps).Edges);
        Assert.Throws<ArgumentException>(() => DockDeskspaces.Link("", "file:///C:/Windows/notepad.exe"));
    }

    [Fact]
    public void CasinoResumesARoundAndSettlesOnce()
    {
        DockCasino.Update(wallet => DockCasino.Begin(wallet, CasinoGame.Holdem, 40, "", ""));
        Assert.Equal(960, DockCasino.Load().Balance);
        Assert.Throws<ArgumentException>(() => DockCasino.Update(wallet => DockCasino.Continue(wallet, "Showdown", new HashSet<int>())));
        foreach (var action in new[] { "Flop", "Turn", "River", "Showdown" })
            DockCasino.Update(wallet => DockCasino.Continue(wallet, action, new HashSet<int>()));
        var saved = DockCasino.Load();
        Assert.Null(saved.Round);
        var settled = Assert.Single(saved.History);
        Assert.Equal(40, settled.Stake);
        Assert.Equal(960 + settled.Returned, saved.Balance);
        Assert.Throws<ArgumentException>(() => DockCasino.Update(wallet => DockCasino.Begin(wallet, CasinoGame.EuropeanRoulette, 10, "Number", "00")));
        Assert.Equal(saved.Balance, DockCasino.Load().Balance);
        var today = new DateOnly(2026, 11, 17);
        DockCasino.Update(wallet => DockCasino.Claim(wallet, today));
        Assert.Throws<ArgumentException>(() => DockCasino.Update(wallet => DockCasino.Claim(wallet, today)));
        Assert.Equal(saved.Balance + 100, DockCasino.Load().Balance);
    }

    [Fact]
    public void TableRulesHandleWheelAndFaceCards()
    {
        Assert.Equal(20, DockCasino.BlackjackValue([0, 13, 7]));
        Assert.True(DockCasino.BankerDraws(6, 7));
        Assert.False(DockCasino.BankerDraws(6, 8));
        Assert.Equal((6, -1), DockCasino.PassLine(0, 6));
        Assert.Equal((0, 2), DockCasino.PassLine(6, 6));
        Assert.Equal((0, 0), DockCasino.PassLine(6, 7));
        Assert.Equal(("Royal flush", 800), DockCasino.VideoHand([13, 22, 23, 24, 25]));
        Assert.True(DockCasino.HoldemScore([0, 1, 2, 3, 4, 20, 50]) >
            DockCasino.HoldemScore([11, 24, 37, 50, 7, 19, 42]));
        Assert.True(DockCasino.HoldemScore([1, 2, 3, 4, 5, 20, 50]) >
            DockCasino.HoldemScore([0, 1, 2, 3, 4, 20, 50]));
    }

    [Fact]
    public void TownEvolvesEvenWhenPlayerDoesNothing()
    {
        var real = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        var world = DockLife.Create("Sam", "Riverside", real, fixedSeed: 41);
        var later = world.At.AddYears(3);
        var skipped = DockLife.Advance(world, later);
        var inSteps = DockLife.Advance(DockLife.Advance(world, world.At.AddYears(1).AddHours(4)), later);
        Assert.Equal(JsonSerializer.Serialize(skipped), JsonSerializer.Serialize(inSteps));
        Assert.Contains(skipped.People, person => person.Worked >= 500);
        Assert.Contains(skipped.History, item => item.Text.Contains("changed careers", StringComparison.Ordinal) ||
            item.Text.Contains("became partners", StringComparison.Ordinal));
        Assert.Equal(world.Cash, skipped.Cash);
        var fast = DockLife.SetSpeed(world, 10, real);
        Assert.Equal(fast.At.AddMinutes(10), DockLife.Tick(fast, real.AddMinutes(1)).At);
        var study = DockLife.Act(skipped, "study", null);
        var shift = DockLife.Act(study, "work", null);
        Assert.Equal(skipped.At.AddHours(11), shift.At);
        Assert.Equal(skipped.Cash + 57, shift.Cash);
        DockLife.SaveNew(shift);
        Assert.Equal(shift.Cash, Assert.Single(DockLife.Load()).Cash);
    }
}
