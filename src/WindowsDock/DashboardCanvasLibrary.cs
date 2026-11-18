using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly string[] CanvasTemplates = ["Blank", "Kanban", "Mind map", "Retro"];
    private int canvasTemplate;

    private void ShowProjectCanvases()
    {
        var (window, content, _) = CreateDockPanel("Project canvases", "", 620, 0xE7C3, accent, widget: true, height: 520);
        var name = Field("New canvas name", 100);
        name.Tag = "Name a new canvas";
        var feedback = Text("", 12, bad);
        feedback.TextWrapping = TextWrapping.Wrap;
        var create = ActionButton("Create", Create);
        MarkPrimary(create, true);
        create.Margin = new Thickness(8, 0, 0, 0);
        var form = new DockPanel();
        DockPanel.SetDock(create, Dock.Right);
        form.Children.Add(create);
        form.Children.Add(name);
        name.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                Create();
            }
        };
        content.Children.Add(form);
        var start = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        var startLabel = Text("Start from", 12, AssistantSecondary);
        startLabel.VerticalAlignment = VerticalAlignment.Center;
        startLabel.Margin = new Thickness(0, 0, 10, 0);
        start.Children.Add(startLabel);
        start.Children.Add(Segmented("Canvas template", CanvasTemplates, canvasTemplate, index => canvasTemplate = index));
        content.Children.Add(start);
        content.Children.Add(feedback);

        var entries = DockDeskspaces.ListCanvases();
        var shelf = new WrapPanel { Margin = new Thickness(-5, 8, -5, 0) };
        foreach (var entry in entries)
            shelf.Children.Add(CanvasTile(entry));
        content.Children.Add(shelf);
        if (entries.Length == 0)
        {
            var empty = Text("No canvases yet. Name one above, pick how it starts, and press Create.", 12.5, AssistantFaint);
            empty.Margin = new Thickness(0, 14, 0, 0);
            content.Children.Add(empty);
        }
        window.Show();

        void Create() => Run(() =>
        {
            var board = DockDeskspaces.AddCanvas(null, name.Text.Trim().Length == 0 && canvasTemplate > 0
                ? CanvasTemplates[canvasTemplate] : name.Text);
            var items = CanvasStarter(canvasTemplate);
            if (items.Length > 0)
                DockDeskspaces.ChangeCanvas(null, board.Id, saved => saved with { Items = items });
            ShowProjectCanvas(null, board.Id);
        });

        FrameworkElement CanvasTile(DeskCanvasEntry entry)
        {
            var body = new StackPanel { Width = 190 };
            body.Children.Add(new Border
            {
                Height = 104, CornerRadius = new CornerRadius(8), Background = assistantInset, BorderBrush = AssistantRule,
                BorderThickness = new Thickness(1), Child = CanvasThumbnail(entry.Canvas), ClipToBounds = true
            });
            var title = Text(entry.Canvas.Name, 13, AssistantInk, true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(2, 7, 0, 0);
            body.Children.Add(title);
            var count = entry.Canvas.Items.Count(item => item.From == null);
            body.Children.Add(Spaced(Text((entry.SpaceName is { } space ? space + " · " : "") + Plural(count, "item", "items"), 11, AssistantFaint), 2, 1));
            var tile = PlainButton(body, "Open canvas " + entry.Canvas.Name,
                () => ShowProjectCanvas(entry.SpaceId, entry.Canvas.Id, fromLibrary: true));
            tile.Margin = new Thickness(5, 0, 5, 12);
            tile.Padding = new Thickness(5);
            tile.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (entry.SpaceId is not { } desk)
                return tile;
            // A canvas that belongs to a deskspace can be lifted out on its own.
            var stack = new StackPanel { Children = { tile } };
            var move = QuietButton("Make standalone", () => Run(() =>
            {
                DockDeskspaces.MakeCanvasStandalone(desk, entry.Canvas.Id);
                ShowProjectCanvas(null, entry.Canvas.Id);
            }));
            move.Margin = new Thickness(10, -10, 0, 8);
            stack.Children.Add(move);
            return stack;
        }

        void Run(Action action)
        {
            feedback.Text = "";
            try { action(); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { feedback.Text = error.Message; }
        }
    }

    // A tiny drawing of the board: each item as a block in its own colour, and the arrows between them.
    private FrameworkElement CanvasThumbnail(DeskCanvas board)
    {
        var shapes = board.Items.Where(item => item.From == null).ToArray();
        if (shapes.Length == 0)
            return new TextBlock { Text = "Empty", Foreground = AssistantFaint, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var left = shapes.Min(item => item.X);
        var top = shapes.Min(item => item.Y);
        var width = Math.Max(1, shapes.Max(item => item.X + item.Width) - left);
        var height = Math.Max(1, shapes.Max(item => item.Y + item.Height) - top);
        var drawing = new Canvas { Width = width, Height = height };
        var byId = shapes.ToDictionary(item => item.Id);
        foreach (var link in board.Items.Where(item => item.From != null))
            if (link.From is { } from && link.To is { } to && byId.TryGetValue(from, out var a) && byId.TryGetValue(to, out var b))
                drawing.Children.Add(new Line
                {
                    X1 = a.X - left + a.Width / 2, Y1 = a.Y - top + a.Height / 2, X2 = b.X - left + b.Width / 2, Y2 = b.Y - top + b.Height / 2,
                    Stroke = AssistantSecondary, StrokeThickness = Math.Max(2, width / 120)
                });
        foreach (var item in shapes)
        {
            var fill = CanvasBrush(item.Color, AssistantSecondary);
            FrameworkElement block = item.Kind switch
            {
                DeskItemKind.Ellipse => new Ellipse { Fill = fill },
                DeskItemKind.Rectangle or DeskItemKind.Diamond => new Rectangle { Stroke = fill, StrokeThickness = Math.Max(2, width / 110),
                    RadiusX = 6, RadiusY = 6 },
                DeskItemKind.Text => new Rectangle { Fill = AssistantSecondary, Height = Math.Min(item.Height, 18), Opacity = .6 },
                _ => new Rectangle { Fill = fill, RadiusX = 4, RadiusY = 4 }
            };
            block.Width = item.Width;
            if (item.Kind != DeskItemKind.Text)
                block.Height = item.Height;
            Canvas.SetLeft(block, item.X - left);
            Canvas.SetTop(block, item.Y - top);
            drawing.Children.Add(block);
        }
        return new Viewbox { Child = drawing, Stretch = Stretch.Uniform, Margin = new Thickness(10) };
    }

    // Items a new canvas starts with, so a template is something to fill in rather than a blank page.
    private static DeskBoardItem[] CanvasStarter(int template)
    {
        switch (template)
        {
            case 1:
            {
                var items = new List<DeskBoardItem>();
                string[] columns = ["To do", "Doing", "Done"];
                for (var index = 0; index < columns.Length; index++)
                {
                    var x = index * 300;
                    items.Add(DockCanvas.Create(DeskItemKind.Text, x, 0, columns[index]) with { Width = 260, Height = 50 });
                    items.Add(DockCanvas.Create(DeskItemKind.Rectangle, x, 60) with { Width = 260, Height = 420, Color = "#93989F",
                        Stroke = DeskStroke.Dashed });
                }
                items.Add(DockCanvas.Create(DeskItemKind.Sticky, 40, 90, "First task") with { Color = "#E7BF68" });
                items.Add(DockCanvas.Create(DeskItemKind.Sticky, 40, 240, "Another one") with { Color = "#8BB5DA" });
                items.Add(DockCanvas.Create(DeskItemKind.Sticky, 340, 90, "In progress") with { Color = "#C9A3DA" });
                return items.ToArray();
            }
            case 2:
            {
                var centre = DockCanvas.Create(DeskItemKind.Ellipse, 300, 210, "The big idea") with { Width = 200, Height = 120, Color = "#E7BF68" };
                var items = new List<DeskBoardItem> { centre };
                (double X, double Y, string Text, string Color)[] branches =
                [
                    (0, 0, "Why it matters", "#8BB5DA"), (620, 0, "Who it is for", "#8BC6B1"),
                    (0, 400, "What could go wrong", "#DE8A8A"), (620, 400, "First step", "#C9A3DA")
                ];
                foreach (var branch in branches)
                {
                    var note = DockCanvas.Create(DeskItemKind.Sticky, branch.X, branch.Y, branch.Text) with { Color = branch.Color };
                    items.Add(note);
                    items.Add(DockCanvas.Create(DeskItemKind.Arrow, 0, 0) with { From = centre.Id, To = note.Id });
                }
                return items.ToArray();
            }
            case 3:
            {
                var items = new List<DeskBoardItem>();
                (string Heading, string Note, string Color)[] columns =
                [
                    ("Went well", "Something to keep", "#8BC6B1"), ("To improve", "Something that dragged", "#DE8A8A"),
                    ("Try next", "One small change", "#8BB5DA")
                ];
                for (var index = 0; index < columns.Length; index++)
                {
                    var x = index * 240;
                    items.Add(DockCanvas.Create(DeskItemKind.Text, x, 0, columns[index].Heading) with { Width = 200, Height = 50 });
                    items.Add(DockCanvas.Create(DeskItemKind.Sticky, x, 70, columns[index].Note) with { Color = columns[index].Color });
                }
                return items.ToArray();
            }
            default:
                return [];
        }
    }
}
