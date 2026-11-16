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
}
