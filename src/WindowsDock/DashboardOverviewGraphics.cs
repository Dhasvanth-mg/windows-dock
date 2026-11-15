using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace WindowsDock;

public partial class DashboardWindow
{
    private UIElement OverviewGauge(string label, SolidColorBrush color, out Action<double> update)
    {
        const double center = 23;
        const double radius = 19;
        var dial = new Grid { Width = 46, Height = 46 };
        dial.Children.Add(new Ellipse
        {
            Width = radius * 2, Height = radius * 2, Stroke = AgentArt.Wash(color, 54), StrokeThickness = 3
        });
        var arc = new ShapePath
        {
            Stroke = color, StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.None
        };
        dial.Children.Add(arc);
        var reading = Text("0%", 11, AssistantInk, true);
        reading.HorizontalAlignment = HorizontalAlignment.Center;
        reading.VerticalAlignment = VerticalAlignment.Center;
        dial.Children.Add(reading);
        var caption = Text(label, 10, AssistantSecondary);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        var group = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        group.Children.Add(dial);
        group.Children.Add(caption);
        var displayed = -1;
        update = value =>
        {
            var percent = (int)Math.Clamp(Math.Round(value), 0, 100);
            if (displayed == percent)
                return;
            displayed = percent;
            reading.Text = $"{percent}%";
            var angle = 2 * Math.PI * Math.Min(99.9, percent) / 100 - Math.PI / 2;
            var end = new Point(center + Math.Cos(angle) * radius, center + Math.Sin(angle) * radius);
            arc.Data = percent == 0 ? null : new PathGeometry([
                new PathFigure(new Point(center, center - radius),
                    [new ArcSegment(end, new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true)], false)
            ]);
            System.Windows.Automation.AutomationProperties.SetName(group, $"{label} {percent}%");
        };
        return group;
    }

    private FrameworkElement WeatherIllustration(int? code)
    {
        var sky = new Canvas { Width = 82, Height = 64, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false };
        var sun = code is <= 2;
        var rain = code is >= 51 and <= 67 or >= 80 and <= 82 or >= 95;
        var snow = code is >= 71 and <= 77 or 85 or 86;
        var disk = sun ? amber : accent;
        Spot(11, 5, 61, 54, AgentArt.Wash(disk, 24));
        if (sun)
        {
            Spot(23, 8, 33, 33, AgentArt.Wash(amber, 65));
            Spot(28, 13, 23, 23, AgentArt.Wash(amber, 230));
        }
        if (code is not 0)
        {
            var cloud = AgentArt.Wash(accent, code == null ? (byte)90 : (byte)182);
            var layer = new Border { Width = 49, Height = 14, CornerRadius = new CornerRadius(7), Background = cloud };
            Canvas.SetLeft(layer, 20);
            Canvas.SetTop(layer, 35);
            sky.Children.Add(layer);
            Spot(25, 25, 19, 20, cloud);
            Spot(36, 20, 22, 27, cloud);
            Spot(52, 30, 13, 14, cloud);
        }
        if (rain || snow)
        {
            for (var index = 0; index < 3; index++)
                Spot(28 + index * 12, 52 - index % 2 * 2, snow ? 3 : 2.5, snow ? 3 : 7, accent);
        }
        return sky;

        void Spot(double x, double y, double width, double height, Brush fill)
        {
            var shape = new Ellipse { Width = width, Height = height, Fill = fill };
            Canvas.SetLeft(shape, x);
            Canvas.SetTop(shape, y);
            sky.Children.Add(shape);
        }
    }
}
