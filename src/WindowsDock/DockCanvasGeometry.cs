using System.Globalization;
using System.Windows;

namespace WindowsDock;

/// <summary>
/// The maths behind connectors and colours on Project canvases: where an item's connection points are once it is
/// rotated, which point a dropped arrow should clip to, how an elbow or curved connector runs, and the colour palette.
/// </summary>
internal static class DockCanvasGeometry
{
    internal static readonly DeskAnchor[] Sides = [DeskAnchor.Top, DeskAnchor.Right, DeskAnchor.Bottom, DeskAnchor.Left];

    internal static Point Center(DeskBoardItem item) => new(item.X + item.Width / 2, item.Y + item.Height / 2);

    /// <summary>The middle of one side of an item, turned with the item.</summary>
    internal static Point Anchor(DeskBoardItem item, DeskAnchor side)
    {
        var center = Center(item);
        var offset = side switch
        {
            DeskAnchor.Top => new Vector(0, -item.Height / 2),
            DeskAnchor.Right => new Vector(item.Width / 2, 0),
            DeskAnchor.Bottom => new Vector(0, item.Height / 2),
            DeskAnchor.Left => new Vector(-item.Width / 2, 0),
            _ => new Vector(0, 0)
        };
        return center + Rotate(offset, item.Rotation);
    }

    /// <summary>The direction a connector leaves a side in, turned with the item.</summary>
    internal static Vector Normal(DeskBoardItem item, DeskAnchor side) => Rotate(side switch
    {
        DeskAnchor.Top => new Vector(0, -1),
        DeskAnchor.Right => new Vector(1, 0),
        DeskAnchor.Bottom => new Vector(0, 1),
        _ => new Vector(-1, 0)
    }, item.Rotation);

    /// <summary>The connection point closest to a spot: where an arrow dropped there clips on.</summary>
    internal static DeskAnchor Nearest(DeskBoardItem item, Point point) =>
        Sides.MinBy(side => (Anchor(item, side) - point).LengthSquared);

    /// <summary>
    /// The two sides a connector joins. A side set by hand is kept; an automatic one faces the other item.
    /// </summary>
    internal static (DeskAnchor From, DeskAnchor To) Ends(DeskBoardItem from, DeskAnchor fromSide, DeskBoardItem to, DeskAnchor toSide)
    {
        var start = fromSide == DeskAnchor.Auto ? Nearest(from, toSide == DeskAnchor.Auto ? Center(to) : Anchor(to, toSide)) : fromSide;
        var end = toSide == DeskAnchor.Auto ? Nearest(to, Anchor(from, start)) : toSide;
        return (start, end);
    }

    /// <summary>
    /// The corner points of an elbow connector: out from the first side, across, and in to the second, using only
    /// straight runs. Both ends leave along their side before turning, so the line never cuts back over an item.
    /// </summary>
    internal static Point[] Elbow(Point start, Vector startOut, Point end, Vector endOut, double lead = 18)
    {
        var a = start + startOut * lead;
        var b = end + endOut * lead;
        var horizontalFirst = Math.Abs(startOut.X) >= Math.Abs(startOut.Y);
        var middle = horizontalFirst
            ? new[] { new Point((a.X + b.X) / 2, a.Y), new Point((a.X + b.X) / 2, b.Y) }
            : [new Point(a.X, (a.Y + b.Y) / 2), new Point(b.X, (a.Y + b.Y) / 2)];
        return [start, a, middle[0], middle[1], b, end];
    }

    /// <summary>The two control points of a curved connector, pulled out along each side.</summary>
    internal static (Point First, Point Second) Curve(Point start, Vector startOut, Point end, Vector endOut)
    {
        var reach = Math.Max(30, (end - start).Length * .4);
        return (start + startOut * reach, end + endOut * reach);
    }

    internal static Vector Rotate(Vector vector, double degrees)
    {
        if (degrees == 0)
            return vector;
        var radians = degrees * Math.PI / 180;
        var (sin, cos) = Math.SinCos(radians);
        return new Vector(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    /// <summary>Snaps an angle to the nearest 15 degrees when it is within a few degrees of one, and keeps it in 0 to 359.</summary>
    internal static double SnapAngle(double degrees, bool always = false)
    {
        var turned = ((degrees % 360) + 360) % 360;
        var nearest = Math.Round(turned / 15) * 15;
        var result = always || Math.Abs(turned - nearest) <= 4 ? nearest : Math.Round(turned);
        return result >= 360 ? result - 360 : result;
    }

    /// <summary>
    /// One row of the palette for a colour: lightest to darkest, then the same steps with the colour muted, so the
    /// row runs from a pale tint to a deep shade and from vivid to greyed.
    /// </summary>
    internal static string[] Shades(string hex)
    {
        var (hue, saturation, lightness) = ToHsl(hex);
        double[] steps = [.86, .74, lightness, Math.Max(.2, lightness - .16), Math.Max(.12, lightness - .3)];
        var vivid = steps.Select(step => FromHsl(hue, Math.Min(1, Math.Max(saturation, .35)), step));
        var muted = steps.Select(step => FromHsl(hue, saturation * .3, step));
        return vivid.Concat(muted).ToArray();
    }

    internal static (double Hue, double Saturation, double Lightness) ToHsl(string hex)
    {
        if (!IsColor(hex))
            throw new ArgumentException("Use a hex colour such as #8BB5DA.");
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var r = (value >> 16 & 255) / 255d;
        var g = (value >> 8 & 255) / 255d;
        var b = (value & 255) / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max == min)
            return (0, 0, lightness);
        var delta = max - min;
        var saturation = lightness > .5 ? delta / (2 - max - min) : delta / (max + min);
        var hue = max == r ? (g - b) / delta + (g < b ? 6 : 0) : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        return (hue * 60, saturation, lightness);
    }

    internal static string FromHsl(double hue, double saturation, double lightness)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var section = ((hue % 360) + 360) % 360 / 60;
        var x = chroma * (1 - Math.Abs(section % 2 - 1));
        var (r, g, b) = section switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var m = lightness - chroma / 2;
        static int Channel(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return $"#{Channel(r + m):X2}{Channel(g + m):X2}{Channel(b + m):X2}";
    }

    internal static bool IsColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);
}
