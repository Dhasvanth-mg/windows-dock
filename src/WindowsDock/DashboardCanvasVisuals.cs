using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static SolidColorBrush CanvasBrush(string hex, SolidColorBrush fallback) =>
        !SystemParameters.HighContrast && hex.Length == 7 && hex[0] == '#' &&
        uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) : fallback;

    private static DoubleCollection? BoardDashes(DeskStroke style) => style switch
    {
        DeskStroke.Dashed => new DoubleCollection([5, 4]),
        DeskStroke.Dotted => new DoubleCollection([1, 4]),
        _ => null
    };

    private static Line BoardStroke(DeskStroke style, Brush color) => new()
    {
        Stroke = color, StrokeThickness = 2.6, StrokeDashArray = BoardDashes(style), StrokeDashCap = PenLineCap.Round
    };

    private static void SetBoardLine(Line line, Polygon arrow, Point start, Point end)
    {
        line.X1 = start.X;
        line.Y1 = start.Y;
        line.X2 = end.X;
        line.Y2 = end.Y;
        var direction = end - start;
        if (direction.Length < 1)
        {
            arrow.Points.Clear();
            return;
        }
        direction.Normalize();
        var perpendicular = new Vector(-direction.Y, direction.X);
        arrow.Points = new PointCollection([end, end - direction * 13 + perpendicular * 5, end - direction * 13 - perpendicular * 5]);
    }

    private FrameworkElement CanvasVisual(DeskBoardItem item, BitmapSource? image)
    {
        var color = CanvasBrush(item.Color, AssistantInk);
        var text = Text(item.Text, item.Kind == DeskItemKind.Sticker ? 39 : 14, color);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(9, 7, 9, 7);
        switch (item.Kind)
        {
            case DeskItemKind.Sticky:
                text.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
                    : color.Color.R * .299 + color.Color.G * .587 + color.Color.B * .114 > 145 ? Brushes.Black : Brushes.White;
                return new Border { Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : color,
                    CornerRadius = new CornerRadius(3), BorderBrush = color, BorderThickness = new Thickness(1), Child = text };
            case DeskItemKind.Text:
                return text;
            case DeskItemKind.Sticker:
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;
                text.FontFamily = new FontFamily("Segoe UI Emoji");
                return text;
            case DeskItemKind.Image:
                return image == null ? Text("Picture unavailable", 12, AssistantSecondary)
                    : new Image { Source = image, Stretch = Stretch.Uniform };
            case DeskItemKind.Chart:
                return BoardChart(item.Text, color);
            case DeskItemKind.Table:
                return BoardTable(item.Text, color);
            case DeskItemKind.Ink:
            {
                var coordinates = new Canvas { Width = 100, Height = 100 };
                coordinates.Children.Add(new Polyline { Stroke = color, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
                    StrokeDashArray = BoardDashes(item.Stroke), StrokeDashCap = PenLineCap.Round,
                    Points = new PointCollection((item.Vertices ?? []).Select(point => new Point(point.X * 100, point.Y * 100))) });
                return new Viewbox { Child = coordinates, Stretch = Stretch.Fill };
            }
            case DeskItemKind.Line:
            case DeskItemKind.Arrow:
            {
                var local = new Canvas { Width = 170, Height = 56, ClipToBounds = false };
                var line = BoardStroke(item.Stroke, color);
                var arrow = new Polygon { Fill = color, Visibility = item.Kind == DeskItemKind.Arrow ? Visibility.Visible : Visibility.Collapsed };
                var (from, to) = item.Angle switch
                {
                    DeskLineAngle.Horizontal => (new Point(10, 28), new Point(157, 28)),
                    DeskLineAngle.Vertical => (new Point(84, 7), new Point(84, 49)),
                    DeskLineAngle.Falling => (new Point(10, 12), new Point(157, 45)),
                    _ => (new Point(10, 45), new Point(157, 12))
                };
                SetBoardLine(line, arrow, item.Reverse ? to : from, item.Reverse ? from : to);
                local.Children.Add(line);
                local.Children.Add(arrow);
                return new Viewbox { Child = local, Stretch = Stretch.Fill };
            }
            default:
            {
                Shape shape = item.Kind switch
                {
                    DeskItemKind.Ellipse => new Ellipse(),
                    DeskItemKind.Diamond => new Polygon { Points = new PointCollection([new(50, 0), new(100, 50), new(50, 100), new(0, 50)]), Stretch = Stretch.Fill },
                    _ => new Rectangle { RadiusX = 4, RadiusY = 4 }
                };
                shape.Stroke = color;
                shape.StrokeThickness = 2;
                shape.StrokeDashArray = BoardDashes(item.Stroke);
                shape.Fill = new SolidColorBrush(Color.FromArgb(38, color.Color.R, color.Color.G, color.Color.B));
                text.VerticalAlignment = VerticalAlignment.Center;
                text.TextAlignment = TextAlignment.Center;
                return new Grid { Children = { shape, text } };
            }
        }
    }

    private FrameworkElement BoardTable(string csv, Brush accent)
    {
        var rows = DockCanvas.Table(csv);
        var columns = rows.Max(row => row.Length);
        var table = new Grid { Background = controlSelected };
        for (var column = 0; column < columns; column++)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < rows.Length; row++)
        {
            table.RowDefinitions.Add(new RowDefinition { MinHeight = 23 });
            for (var column = 0; column < columns; column++)
            {
                var content = Text(column < rows[row].Length ? rows[row][column] : "", 12, row == 0 ? accent : AssistantInk, row == 0);
                content.VerticalAlignment = VerticalAlignment.Center;
                content.TextTrimming = TextTrimming.CharacterEllipsis;
                var cell = new Border { BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = AssistantRule,
                    Padding = new Thickness(7, 2, 7, 2), Child = content };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                table.Children.Add(cell);
            }
        }
        return table;
    }

    private FrameworkElement BoardChart(string source, SolidColorBrush color)
    {
        var data = DockCanvas.Chart(source);
        var canvas = new Canvas { Width = 310, Height = 175, Background = controlSelected, ClipToBounds = true };
        if (data.Length == 0)
            return canvas;
        const double plotTop = 12;
        const double plotHeight = 125;
        var slot = 285d / data.Length;
        var low = Math.Min(0, data.Min(item => item.Value));
        var high = Math.Max(0, data.Max(item => item.Value));
        double Y(double value) => plotTop + (high - value) / Math.Max(.000_000_001, high - low) * plotHeight;
        var axis = new Line { Stroke = AssistantFaint, StrokeThickness = 1, X1 = 12, X2 = 298, Y1 = Y(0), Y2 = Y(0) };
        canvas.Children.Add(axis);
        for (var index = 0; index < data.Length; index++)
        {
            var (label, value) = data[index];
            var left = 12 + index * slot;
            var y = Y(value);
            var bar = new Rectangle { Fill = color, Width = Math.Max(3, slot * .57), Height = Math.Max(1, Math.Abs(y - Y(0))),
                RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(bar, left + slot * .215);
            Canvas.SetTop(bar, Math.Min(y, Y(0)));
            canvas.Children.Add(bar);
            if (slot < 20)
                continue;
            var title = Text(label, 10, AssistantSecondary);
            title.Width = slot;
            title.TextAlignment = TextAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(title, left);
            Canvas.SetTop(title, 142);
            canvas.Children.Add(title);
            var amount = Text(value.ToString("G4", CultureInfo.CurrentCulture), 10, AssistantInk);
            amount.Width = slot;
            amount.TextAlignment = TextAlignment.Center;
            amount.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(amount, left);
            Canvas.SetTop(amount, 156);
            canvas.Children.Add(amount);
        }
        return new Viewbox { Child = canvas, Stretch = Stretch.Fill };
    }
}
