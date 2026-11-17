using System.Globalization;

namespace WindowsDock;

internal enum DeskItemKind { Sticky, Text, Rectangle, Ellipse, Diamond, Line, Arrow, Sticker, Image, Chart, Table, Ink }
internal enum DeskStroke { Solid, Dashed, Dotted }
internal enum DeskLineAngle { Rising, Falling, Horizontal, Vertical }
internal sealed record DeskVertex(double X, double Y);
internal sealed record DeskBoardItem(Guid Id, DeskItemKind Kind, double X, double Y, double Width, double Height,
    string Text, string Color, DeskStroke Stroke = DeskStroke.Solid, string? Image = null, bool Reverse = false,
    DeskVertex[]? Vertices = null, Guid? From = null, Guid? To = null, DeskLineAngle Angle = DeskLineAngle.Rising);
internal sealed record DeskCanvas(Guid Id, string Name, DeskBoardItem[] Items);

internal static class DockCanvas
{
    internal static DeskBoardItem Create(DeskItemKind kind, double x, double y, string? text = null, string? image = null)
    {
        var (width, height, content, color) = kind switch
        {
            DeskItemKind.Sticky => (180, 132, "Write a note", "#E7BF68"),
            DeskItemKind.Text => (220, 86, "Add text", "#F3F0E7"),
            DeskItemKind.Rectangle or DeskItemKind.Ellipse or DeskItemKind.Diamond => (140, 100, "", "#8BB5DA"),
            DeskItemKind.Line or DeskItemKind.Arrow => (150, 48, "", "#AFC1C8"),
            DeskItemKind.Sticker => (76, 72, "⭐", "#FFDC81"),
            DeskItemKind.Image => (250, 180, "", "#F3F0E7"),
            DeskItemKind.Chart => (305, 190, "Ideas,4\nDrafts,7\nDone,2", "#8BC6B1"),
            DeskItemKind.Table => (310, 140, "Task\tOwner\tStatus\nSketch\t\tOpen", "#85A9CF"),
            _ => (100, 100, "", "#F3F0E7")
        };
        return new(Guid.NewGuid(), kind, x, y, width, height, DockDeskspaces.Clip(text ?? content, 12_000), color, Image: image);
    }

    internal static DeskCanvas Remove(DeskCanvas board, Guid item) => board with
    {
        Items = board.Items.Where(entry => entry.Id != item && entry.From != item && entry.To != item).ToArray()
    };

    internal static (string Label, double Value)[] Chart(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(32).Select(line =>
        {
            var parts = line.Split([',', '\t'], 2, StringSplitOptions.TrimEntries);
            return parts.Length == 2 && parts[0].Length > 0 &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? (DockDeskspaces.Clip(parts[0], 30), Math.Clamp(number, -1e12, 1e12)) : ("", 0d);
        }).Where(item => item.Item1 != "").ToArray();

    internal static string[][] Table(string text) => text.Replace("\r", "").Split('\n').Take(40)
        .Select(line => (line.Contains('\t') ? line.Split('\t') : line.Split(',')).Take(12).ToArray()).ToArray();
}
