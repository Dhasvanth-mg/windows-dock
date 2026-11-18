using System.Text.Json;
using System.Windows;
using Xunit;

namespace WindowsDock.Tests;

public sealed class CanvasGeometryTests
{
    private static DeskBoardItem Box(double x, double y, double rotation = 0) =>
        DockCanvas.Create(DeskItemKind.Rectangle, x, y) with { Width = 100, Height = 60, Rotation = rotation };

    [Fact]
    public void AnchorsSitMidSideAndTurnWithTheItem()
    {
        var box = Box(0, 0);
        Assert.Equal(new Point(50, 0), DockCanvasGeometry.Anchor(box, DeskAnchor.Top));
        Assert.Equal(new Point(100, 30), DockCanvasGeometry.Anchor(box, DeskAnchor.Right));
        var turned = DockCanvasGeometry.Anchor(box with { Rotation = 90 }, DeskAnchor.Right);
        Assert.Equal(50, turned.X, 6);
        Assert.Equal(80, turned.Y, 6);
    }

    [Fact]
    public void DroppedArrowClipsToTheNearestPoint()
    {
        var box = Box(0, 0);
        Assert.Equal(DeskAnchor.Left, DockCanvasGeometry.Nearest(box, new Point(-20, 35)));
        Assert.Equal(DeskAnchor.Bottom, DockCanvasGeometry.Nearest(box, new Point(55, 90)));
    }

    [Fact]
    public void AutomaticEndsFaceEachOtherAndChosenEndsAreKept()
    {
        var left = Box(0, 0);
        var right = Box(300, 0);
        Assert.Equal((DeskAnchor.Right, DeskAnchor.Left), DockCanvasGeometry.Ends(left, DeskAnchor.Auto, right, DeskAnchor.Auto));
        Assert.Equal((DeskAnchor.Bottom, DeskAnchor.Left), DockCanvasGeometry.Ends(left, DeskAnchor.Bottom, right, DeskAnchor.Auto));
        Assert.Equal((DeskAnchor.Top, DeskAnchor.Top), DockCanvasGeometry.Ends(left, DeskAnchor.Top, right, DeskAnchor.Top));
    }

    [Fact]
    public void ElbowRunsOnlyStraightAcrossOrDown()
    {
        var points = DockCanvasGeometry.Elbow(new Point(100, 30), new Vector(1, 0), new Point(300, 130), new Vector(-1, 0));
        Assert.Equal(new Point(100, 30), points[0]);
        Assert.Equal(new Point(300, 130), points[^1]);
        for (var index = 1; index < points.Length; index++)
            Assert.True(points[index].X == points[index - 1].X || points[index].Y == points[index - 1].Y);
    }

    [Theory]
    [InlineData(43, 45)]
    [InlineData(38, 38)]
    [InlineData(-10, 350)]
    [InlineData(359, 0)]
    public void AnglesSnapNearFifteenDegreeSteps(double raw, double expected) =>
        Assert.Equal(expected, DockCanvasGeometry.SnapAngle(raw));

    [Fact]
    public void ShadesRunLightToDarkThenMuted()
    {
        var shades = DockCanvasGeometry.Shades("#F2B04A");
        Assert.Equal(10, shades.Length);
        Assert.All(shades, shade => Assert.True(DockCanvasGeometry.IsColor(shade)));
        var lightness = shades.Take(5).Select(shade => DockCanvasGeometry.ToHsl(shade).Lightness).ToArray();
        Assert.True(lightness.SequenceEqual(lightness.OrderByDescending(value => value)));
        Assert.True(DockCanvasGeometry.ToHsl(shades[7]).Saturation < DockCanvasGeometry.ToHsl(shades[2]).Saturation);
        Assert.Equal("#F2B04A", DockCanvasGeometry.FromHsl(DockCanvasGeometry.ToHsl("#F2B04A").Hue,
            DockCanvasGeometry.ToHsl("#F2B04A").Saturation, DockCanvasGeometry.ToHsl("#F2B04A").Lightness));
    }

    [Fact]
    public void OlderCanvasItemsLoadWithTheNewDefaults()
    {
        var old = """{"Id":"6f1c3e2a-3d4b-4b7a-9a3e-2f1d5c6b7a8e","Kind":2,"X":1,"Y":2,"Width":3,"Height":4,"Text":"","Color":"#8BB5DA"}""";
        var item = JsonSerializer.Deserialize<DeskBoardItem>(old)!;
        Assert.Equal(0, item.Rotation);
        Assert.Null(item.Fill);
        Assert.Equal(2, item.BorderWidth);
        Assert.Equal(DeskRoute.Straight, item.Route);
        Assert.Equal(DeskAnchor.Auto, item.FromAnchor);
    }
}
