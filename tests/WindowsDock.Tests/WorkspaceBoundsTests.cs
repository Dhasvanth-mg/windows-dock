using System.Drawing;
using Xunit;

namespace WindowsDock.Tests;

public class WorkspaceBoundsTests
{
    [Fact]
    public void RestoresRelativeBoundsOnSecondaryMonitor()
    {
        var saved = new WorkspaceWindow("editor.exe", "Title", "Secondary", .25, .10, .5, .6, false);
        var area = new Rectangle(-1920, 40, 1600, 900);

        Assert.Equal(new Rectangle(-1520, 130, 800, 540), saved.BoundsFor(area));
    }

    [Fact]
    public void FitsTheLayoutWithinASmallerRemainingMonitor()
    {
        var saved = new WorkspaceWindow("editor.exe", "Title", "Disconnected", .8, .9, .5, .6, false);
        var area = new Rectangle(0, 0, 400, 260);

        Assert.Equal(new Rectangle(160, 100, 240, 160), saved.BoundsFor(area));
        Assert.Null((saved with { Width = double.NaN }).BoundsFor(area));
    }

    [Fact]
    public void GridFillsPartialRowsAndKeepsDifferentMonitorsSeparate()
    {
        WorkspaceSelection Pick(int index, string monitor) => new(new(index, $"App {index}", "editor.exe", 1),
            new("editor.exe", $"App {index}", monitor, .2, .1, .5, .6, true));
        var arranged = DockWorkspaces.Arrange([Pick(1, "First"), Pick(2, "First"), Pick(3, "First"), Pick(4, "Second")],
            WorkspaceTiling.Grid);
        Assert.Equal(new Rectangle(0, 0, 600, 400), arranged[0].Placement.BoundsFor(new Rectangle(0, 0, 1200, 800)));
        Assert.Equal(new Rectangle(600, 0, 600, 400), arranged[1].Placement.BoundsFor(new Rectangle(0, 0, 1200, 800)));
        Assert.Equal(new Rectangle(0, 400, 1200, 400), arranged[2].Placement.BoundsFor(new Rectangle(0, 0, 1200, 800)));
        Assert.Equal("Second", arranged[3].Placement.Monitor);
        Assert.Equal(1, arranged[3].Placement.Width);
        Assert.All(arranged, window => Assert.False(window.Placement.Maximized));
    }

    [Fact]
    public void FallbackNeverMovesAnExistingWindowWithAnUnrelatedTitle()
    {
        var saved = new WorkspaceWindow("editor.exe", "Saved project", "Secondary", .25, .10, .5, .6, false);
        var unrelated = new AppWindow(1, "Other project", "editor.exe", 1);
        var exact = new AppWindow(2, "Saved project", "editor.exe", 1);
        var fresh = new AppWindow(3, "Editor", "editor.exe", 1);
        HashSet<(nint, uint)> previous = [(1, 1), (2, 1)];

        Assert.Equal(exact, DockWorkspaces.FindWindow([unrelated, exact, fresh], saved, [], previous, exact: true));
        Assert.Null(DockWorkspaces.FindWindow([unrelated, exact], saved, [(2, 1)], previous, exact: false));
        Assert.Equal(fresh, DockWorkspaces.FindWindow([unrelated, exact, fresh], saved, [(2, 1)], previous, exact: false));
    }
}
