using System.IO;
using WindowsDock;
using Xunit;

namespace WindowsDock.Tests;

public sealed class SnakeGameTests
{
    [Fact]
    public void StartsMovingRightAndWrapsAroundTheEdge()
    {
        var game = new SnakeGame(seed: 1);
        var row = game.Head.Y;
        var seen = new HashSet<int>();
        // Walk more than a full row. With no turns the snake only dies by hitting itself, which a straight line cannot do.
        for (var step = 0; step < SnakeGame.Columns + 5; step++)
        {
            Assert.True(game.Step());
            Assert.Equal(row, game.Head.Y);
            Assert.InRange(game.Head.X, 0, SnakeGame.Columns - 1);
            seen.Add(game.Head.X);
        }
        Assert.Equal(SnakeGame.Columns, seen.Count);
        Assert.False(game.Over);
    }

    [Fact]
    public void IgnoresReversalsAndDiagonals()
    {
        var game = new SnakeGame(seed: 2);
        game.Turn(-1, 0);
        game.Turn(1, 1);
        game.Turn(0, 0);
        var before = game.Head;
        Assert.True(game.Step());
        Assert.Equal(((before.X + 1) % SnakeGame.Columns, before.Y), game.Head);
    }

    [Fact]
    public void EatingGrowsTheSnakeScoresAndSpeedsItUp()
    {
        var game = new SnakeGame(seed: 3);
        var length = game.Body.Count();
        var slowest = game.Interval;
        Steer(game, game.Food);
        Assert.Equal(1, game.Score);
        Assert.Equal(length + 1, game.Body.Count());
        Assert.True(game.Interval < slowest);
        Assert.DoesNotContain(game.Food, game.Body);
    }

    [Fact]
    public void RunningIntoItsOwnBodyEndsTheGame()
    {
        var game = new SnakeGame(seed: 4);
        // Five segments are the fewest that can close a loop on themselves.
        while (game.Body.Count() < 5)
            Steer(game, game.Food);
        // Head off in a known direction so the square below never asks for a reversal, then turn three times.
        var away = game.Heading.Y == 0 ? (0, 1) : (1, 0);
        game.Turn(away.Item1, away.Item2);
        Assert.True(game.Step());
        var (x, y) = game.Heading;
        for (var turn = 0; turn < 3 && !game.Over; turn++)
        {
            (x, y) = (-y, x);
            game.Turn(x, y);
            game.Step();
        }
        Assert.True(game.Over);
        Assert.False(game.Step());
    }

    // Moves the head onto a cell one axis at a time, going the long way round when a move would be a reversal.
    private static void Steer(SnakeGame game, (int X, int Y) target)
    {
        for (var guard = 0; guard < 400 && game.Head != target && !game.Over; guard++)
        {
            var (dx, dy) = (target.X - game.Head.X, target.Y - game.Head.Y);
            var want = dy != 0 ? (0, Math.Sign(dy)) : (Math.Sign(dx), 0);
            if (want == (-game.Heading.X, -game.Heading.Y))
                want = want.Item1 == 0 ? (1, 0) : (0, 1);
            game.Turn(want.Item1, want.Item2);
            game.Step();
        }
        Assert.False(game.Over);
    }
}

public sealed class CopycatGameTests
{
    [Fact]
    public void RepeatingTheSequenceScoresARoundAndTheNextRoundIsLonger()
    {
        var game = new CopycatGame(seed: 5);
        for (var round = 1; round <= 4; round++)
        {
            game.Extend();
            Assert.Equal(round, game.Sequence.Count);
            for (var index = 0; index < game.Sequence.Count; index++)
            {
                var step = game.Press(game.Sequence[index]);
                Assert.Equal(index == game.Sequence.Count - 1 ? CopycatStep.RoundComplete : CopycatStep.Right, step);
            }
            Assert.Equal(round, game.Score);
        }
        Assert.All(game.Sequence, player => Assert.InRange(player, 0, CopycatGame.Players - 1));
    }

    [Fact]
    public void AWrongPressEndsTheGameAndKeepsTheScore()
    {
        var game = new CopycatGame(seed: 6);
        game.Extend();
        Assert.Equal(CopycatStep.RoundComplete, game.Press(game.Sequence[0]));
        game.Extend();
        Assert.Equal(CopycatStep.Wrong, game.Press((game.Sequence[0] + 1) % CopycatGame.Players));
        Assert.True(game.Over);
        Assert.Equal(1, game.Score);
        Assert.Equal(CopycatStep.Wrong, game.Press(game.Sequence[0]));
        game.Extend();
        Assert.Equal(2, game.Sequence.Count);
    }
}

[Collection("DockData")]
public sealed class DockNotesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "windows-dock-tests-" + Guid.NewGuid().ToString("N"));

    public DockNotesTests() => DockData.RootOverride = root;

    public void Dispose()
    {
        DockData.RootOverride = null;
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    [Fact]
    public void TheFirstStickyIsPinnedAndPinningAnotherMovesThePin()
    {
        var first = DockNotes.Add(DockNotes.Sticky, "milk");
        var second = DockNotes.Add(DockNotes.Sticky, "call the bank", "mint");
        Assert.Equal(first.Id, DockNotes.ForToday(DockNotes.Load())!.Id);

        DockNotes.Change(second.Id, note => note with { Pinned = true });

        var notes = DockNotes.Load();
        Assert.Single(notes, note => note.Pinned);
        Assert.Equal(second.Id, DockNotes.ForToday(notes)!.Id);
        Assert.Equal("mint", notes.Single(note => note.Id == second.Id).Color);
    }

    [Fact]
    public void WithNothingPinnedTodayShowsTheMostRecentStickyAndNeverAMemo()
    {
        var old = DockNotes.Add(DockNotes.Sticky, "old");
        DockNotes.Change(old.Id, note => note with { Pinned = false, Updated = DateTimeOffset.UtcNow.AddDays(-2) });
        var recent = DockNotes.Add(DockNotes.Sticky, "recent");
        DockNotes.Change(recent.Id, note => note with { Pinned = false });
        DockNotes.Add(DockNotes.Memo, "a long memo");

        Assert.Equal(recent.Id, DockNotes.ForToday(DockNotes.Load())!.Id);
        Assert.Null(DockNotes.ForToday(DockNotes.Load().Where(note => note.Kind == DockNotes.Memo)));
    }

    [Fact]
    public void ChangeCannotAlterIdentityAndDeletesOnNull()
    {
        var memo = DockNotes.Add(DockNotes.Memo, "Title line\nbody");
        DockNotes.Change(memo.Id, note => note with { Id = Guid.NewGuid(), Kind = DockNotes.Sticky, Text = "  \n Renamed \nmore" });

        var saved = Assert.Single(DockNotes.Load());
        Assert.Equal(memo.Id, saved.Id);
        Assert.Equal(DockNotes.Memo, saved.Kind);
        Assert.Equal("Renamed", DockNotes.Title(saved));

        DockNotes.Change(memo.Id, _ => null);
        Assert.Empty(DockNotes.Load());
        DockNotes.Change(memo.Id, note => note with { Text = "ghost" });
        Assert.Empty(DockNotes.Load());
    }

    [Fact]
    public void LongTextIsClippedAndTitlesAreShortened()
    {
        var note = DockNotes.Add(DockNotes.Memo, new string('x', DockNotes.TextLimit + 50));
        Assert.Equal(DockNotes.TextLimit, DockNotes.Load().Single().Text.Length);
        Assert.Equal(61, DockNotes.Title(note).Length);
        Assert.Equal("Empty note", DockNotes.Title(note with { Text = " \n " }));
    }

    [Fact]
    public void PaperColoursCycleAndUnknownOnesFallBack()
    {
        var seen = new List<string>();
        var paper = DockNotes.Papers[0].Id;
        for (var index = 0; index < DockNotes.Papers.Length; index++)
        {
            seen.Add(paper);
            paper = DockNotes.NextPaper(paper);
        }
        Assert.Equal(DockNotes.Papers.Select(item => item.Id), seen);
        Assert.Equal(DockNotes.Papers[0].Id, paper);
        Assert.Equal(DockNotes.Papers[0].Hex, DockNotes.PaperHex("no-such-paper"));
        Assert.Throws<ArgumentException>(() => DockNotes.Add("poster"));
    }
}

public sealed class DockAccentTests
{
    [Fact]
    public void UnknownOrMissingAccentFallsBackToTheFirstAndLookupIgnoresCase()
    {
        Assert.Equal(DashboardWindow.Accents[0], DashboardWindow.AccentById(null));
        Assert.Equal(DashboardWindow.Accents[0], DashboardWindow.AccentById("chartreuse"));
        Assert.Equal("pool", DashboardWindow.AccentById("POOL").Id);
        Assert.Equal(DashboardWindow.Accents.Length, DashboardWindow.Accents.Select(item => item.Id).Distinct().Count());
    }
}
