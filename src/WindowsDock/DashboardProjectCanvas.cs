using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly (DeskItemKind Kind, string Title)[] CanvasTools =
    [
        (DeskItemKind.Sticky, "Sticky note"), (DeskItemKind.Text, "Text box"), (DeskItemKind.Rectangle, "Rectangle"),
        (DeskItemKind.Ellipse, "Ellipse"), (DeskItemKind.Diamond, "Diamond"), (DeskItemKind.Line, "Line"),
        (DeskItemKind.Arrow, "Arrow"), (DeskItemKind.Sticker, "Sticker"), (DeskItemKind.Image, "Picture"),
        (DeskItemKind.Chart, "Chart"), (DeskItemKind.Table, "Table")
    ];
    private static readonly string[] CanvasColors = ["#F3F0E7", "#E7BF68", "#DE8A8A", "#C9A3DA", "#8BB5DA", "#8BC6B1", "#A9BE83", "#93989F"];

    private void ShowProjectCanvas(Guid? desk, Guid boardId, bool fromLibrary = false)
    {
        var board = DockDeskspaces.FindCanvas(desk, boardId);
        if (board == null)
            return;
        var library = fromLibrary || desk == null;
        Action back = library ? ShowProjectCanvases : () => ShowDeskspaces(desk);
        var layout = new DockPanel();
        var (window, _, _) = CreateDockPanel(board.Name, "Project canvas", 1120, 0xE7C3, accent,
            backAction: back, backLabel: library ? "Back to canvases" : "Back to deskspace", body: layout, height: 665, expandable: true);
        var viewport = new Grid { ClipToBounds = true, Background = assistantInset, Focusable = true };
        AutomationProperties.SetName(viewport, "Project canvas drawing surface");
        var drawing = new Canvas { Background = Brushes.Transparent };
        var scale = new ScaleTransform(1, 1);
        var pan = new TranslateTransform(28, 28);
        drawing.RenderTransform = new TransformGroup { Children = { scale, pan } };
        viewport.Children.Add(drawing);
        var sidebar = new StackPanel();
        var side = new ScrollViewer { Content = sidebar, Width = 262, Margin = new Thickness(12, 0, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var undo = new LinkedList<DeskCanvas>();
        var images = new Dictionary<string, BitmapSource?>(StringComparer.Ordinal);
        var items = new Dictionary<Guid, Border>();
        var links = new List<(DeskBoardItem Item, Path Line, Path Hit, Polygon End, Polygon Start)>();
        var ends = new Dictionary<Guid, (Point Start, Point End)>();
        var adornments = new List<FrameworkElement>();
        var lastRoute = DeskRoute.Straight;
        string? openPicker = null;
        Guid? selected = null;
        Guid? connectFrom = null;
        var drawingInk = false;
        var deleteArmed = false;
        Polyline? ink = null;
        Point? panStart = null;
        Point originalPan = default;

        // A dot grid that pans and zooms with the board, the way whiteboard apps show where the paper is.
        var dots = new DrawingBrush(new GeometryDrawing(Brush("#2EFFFFFF"), null, new EllipseGeometry(new Point(1, 1), 1, 1)))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute, Transform = drawing.RenderTransform
        };
        var grid = new Border { Background = SystemParameters.HighContrast ? null : dots, IsHitTestVisible = false };
        viewport.Children.Insert(0, grid);

        // Tools sit in a floating rail on the left of the board. A click drops that item in the middle of the view;
        // the pen and connector stay on until used or until Esc.
        var rail = new StackPanel { Margin = new Thickness(6) };
        var railCard = Floating(rail, HorizontalAlignment.Left, VerticalAlignment.Top);
        railCard.Margin = new Thickness(12, 12, 0, 12);
        Button? penButton = null, connectButton = null;
        void AddTool(FrameworkElement icon, string label, Action action, out Button button)
        {
            button = MakeButton(icon, label, action);
            button.Style = (Style)FindResource("AssistantPickerButton");
            button.Width = button.Height = 34;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0, 0, 0, 2);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.ToolTip = label;
            rail.Children.Add(button);
        }
        void Separator() => rail.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 4, 6, 6), Background = AssistantRule });
        AddTool(CanvasToolIcon("pen"), "Pen (draw freehand)", () =>
        {
            drawingInk = !drawingInk;
            connectFrom = null;
            status.Text = drawingInk ? "Drawing. Press Esc or click the pen again to stop." : "";
            MarkTools();
        }, out var pen);
        penButton = pen;
        AddTool(CanvasToolIcon("connect"), "Connect two items with an arrow", () =>
        {
            drawingInk = false;
            connectFrom = selected is { } id && items.ContainsKey(id) ? id : null;
            status.Text = connectFrom == null ? "Select a starting item first." : "Now select the item the arrow points to.";
            MarkTools();
        }, out var connect);
        connectButton = connect;
        Separator();
        foreach (var (kind, title) in CanvasTools)
        {
            var toolKind = kind;
            AddTool(CanvasToolIcon(kind.ToString()), "Add " + title.ToLowerInvariant(), () => Run(() =>
            {
                drawingInk = false;
                MarkTools();
                if (toolKind == DeskItemKind.Image)
                {
                    if (DockDeskImages.Choose(DialogOwner(window)) is { } image)
                        Insert(toolKind, image: image);
                }
                else
                    Insert(toolKind);
            }), out _);
        }
        var railScroll = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        railCard.Child = railScroll;
        viewport.Children.Add(railCard);

        var zoomLabel = Text("100%", 12, AssistantSecondary);
        zoomLabel.Width = 42;
        zoomLabel.TextAlignment = TextAlignment.Center;
        zoomLabel.VerticalAlignment = VerticalAlignment.Center;
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        zoomRow.Children.Add(ChipIcon(0xE738, "Zoom out", () => ZoomBy(1 / 1.25)));
        zoomRow.Children.Add(zoomLabel);
        zoomRow.Children.Add(ChipIcon(0xE710, "Zoom in", () => ZoomBy(1.25)));
        zoomRow.Children.Add(ChipIcon(0xE9A6, "Fit the board in view", Fit));
        var zoomCard = Floating(zoomRow, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        zoomCard.Margin = new Thickness(0, 0, 12, 12);
        viewport.Children.Add(zoomCard);

        var editRow = new StackPanel { Orientation = Orientation.Horizontal };
        editRow.Children.Add(ChipIcon(0xE7A7, "Undo (Ctrl+Z)", Undo));
        editRow.Children.Add(ChipIcon(0xE77F, "Paste a picture, text or table cells (Ctrl+V)", () => Run(Paste)));
        editRow.Children.Add(ChipIcon(0xE713, "Canvas name and settings", () => Select(null)));
        var editCard = Floating(editRow, HorizontalAlignment.Right, VerticalAlignment.Top);
        editCard.Margin = new Thickness(0, 12, 12, 0);
        viewport.Children.Add(editCard);
        var overlays = new FrameworkElement[] { railCard, zoomCard, editCard };

        DockPanel.SetDock(side, Dock.Right);
        layout.Children.Add(side);
        layout.Children.Add(viewport);
        scale.Changed += (_, _) => zoomLabel.Text = $"{scale.ScaleX * 100:0}%";

        void MarkTools()
        {
            foreach (var (button, on) in new[] { (penButton, drawingInk), (connectButton, connectFrom != null) })
                if (button != null)
                {
                    button.Background = on ? controlSelected : Brushes.Transparent;
                    AutomationProperties.SetItemStatus(button, on ? "On" : "");
                }
            viewport.Cursor = drawingInk ? Cursors.Pen : null;
        }

        void ZoomBy(double factor)
        {
            var center = new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2);
            var next = Math.Clamp(scale.ScaleX * factor, .15, 3);
            var ratio = next / scale.ScaleX;
            pan.X = center.X - (center.X - pan.X) * ratio;
            pan.Y = center.Y - (center.Y - pan.Y) * ratio;
            scale.ScaleX = scale.ScaleY = next;
        }

        bool OnOverlay(object source)
        {
            for (var element = source as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
                if (overlays.Contains(element))
                    return true;
            return false;
        }
        viewport.PreviewMouseWheel += (_, args) =>
        {
            var point = args.GetPosition(viewport);
            var next = Math.Clamp(scale.ScaleX * (args.Delta > 0 ? 1.15 : 1 / 1.15), .15, 3);
            var ratio = next / scale.ScaleX;
            pan.X = point.X - (point.X - pan.X) * ratio;
            pan.Y = point.Y - (point.Y - pan.Y) * ratio;
            scale.ScaleX = scale.ScaleY = next;
            args.Handled = true;
        };
        viewport.PreviewMouseRightButtonDown += (_, args) =>
        {
            panStart = args.GetPosition(viewport);
            originalPan = new Point(pan.X, pan.Y);
            viewport.CaptureMouse();
            args.Handled = true;
        };
        viewport.PreviewMouseRightButtonUp += (_, args) =>
        {
            panStart = null;
            viewport.ReleaseMouseCapture();
            args.Handled = true;
        };
        viewport.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (!drawingInk || OnOverlay(args.OriginalSource))
                return;
            var point = args.GetPosition(drawing);
            ink = new Polyline { Stroke = AssistantInk, StrokeThickness = 3, IsHitTestVisible = false, Points = new PointCollection([point]) };
            drawing.Children.Add(ink);
            viewport.CaptureMouse();
            args.Handled = true;
        };
        viewport.MouseMove += (_, args) =>
        {
            if (panStart is { } origin)
            {
                var delta = args.GetPosition(viewport) - origin;
                pan.X = originalPan.X + delta.X;
                pan.Y = originalPan.Y + delta.Y;
            }
            else if (ink is { } line && line.Points.Count < 3600)
            {
                var point = args.GetPosition(drawing);
                if ((point - line.Points[^1]).Length > 2)
                    line.Points.Add(point);
            }
        };
        viewport.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (ink is not { } line)
                return;
            var points = line.Points.ToArray();
            ink = null;
            drawing.Children.Remove(line);
            viewport.ReleaseMouseCapture();
            args.Handled = true;
            if (points.Length < 2)
                return;
            var left = points.Min(point => point.X) - 4;
            var top = points.Min(point => point.Y) - 4;
            var width = points.Max(point => point.X) - left + 4;
            var height = points.Max(point => point.Y) - top + 4;
            var item = DockCanvas.Create(DeskItemKind.Ink, left, top) with { Width = width, Height = height,
                Vertices = points.Select(point => new DeskVertex((point.X - left) / width, (point.Y - top) / height)).ToArray() };
            Run(() => Commit(current => current with { Items = [.. current.Items, item] }, item.Id));
        };
        viewport.LostMouseCapture += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, viewport))
                return;
            panStart = null;
            if (ink is { } old)
            {
                ink = null;
                drawing.Children.Remove(old);
            }
        };
        window.PreviewKeyDown += (_, args) =>
        {
            if (Keyboard.FocusedElement is TextBoxBase)
                return;
            if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.V)
                Run(Paste);
            else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.Z)
                Undo();
            else if (args.Key == Key.Delete && selected is { } id)
                Run(() => Commit(current => DockCanvas.Remove(current, id), null));
            else if (args.Key == Key.Escape && (connectFrom != null || drawingInk))
            {
                connectFrom = null;
                drawingInk = false;
                status.Text = "";
                MarkTools();
            }
            else
                return;
            args.Handled = true;
        };
        window.Loaded += (_, _) => Fit();
        Draw();
        Inspect();
        window.Show();

        void Paste()
        {
            if (System.Windows.Clipboard.GetImage() is { } image)
            {
                Insert(DeskItemKind.Image, image: DockDeskImages.Keep(image));
                return;
            }
            if (!System.Windows.Clipboard.ContainsText())
                throw new ArgumentException("Copy a picture, text, or cells from a table first.");
            var text = System.Windows.Clipboard.GetText();
            Insert(text.Contains('\t') ? DeskItemKind.Table : DeskItemKind.Text, text);
        }

        void Insert(DeskItemKind kind, string? text = null, string? image = null)
        {
            var center = new Point((viewport.ActualWidth / 2 - pan.X) / scale.ScaleX,
                (viewport.ActualHeight / 2 - pan.Y) / scale.ScaleY);
            var item = DockCanvas.Create(kind, center.X - 60, center.Y - 42, text, image);
            if (image != null && GetImage(image) is { } picture)
                item = item with { Height = 260d * picture.PixelHeight / Math.Max(picture.PixelHeight, picture.PixelWidth),
                    Width = 260d * picture.PixelWidth / Math.Max(picture.PixelHeight, picture.PixelWidth) };
            for (var index = 0; index < 32 && board.Items.Any(other => Math.Abs(item.X - other.X) < 22 && Math.Abs(item.Y - other.Y) < 22); index++)
                item = item with { X = item.X + 27, Y = item.Y + 27 };
            Commit(current => current with { Items = [.. current.Items, item] }, item.Id);
        }

        void Fit()
        {
            var visible = board.Items.Where(item => item.From == null).ToArray();
            if (visible.Length == 0 || viewport.ActualWidth < 90 || viewport.ActualHeight < 90)
            {
                scale.ScaleX = scale.ScaleY = 1;
                pan.X = pan.Y = 28;
                return;
            }
            var left = visible.Min(item => item.X);
            var top = visible.Min(item => item.Y);
            var width = visible.Max(item => item.X + item.Width) - left;
            var height = visible.Max(item => item.Y + item.Height) - top;
            // Leave room for the tool rail on the left and the floating controls top and bottom.
            const double railRoom = 72, edge = 24, bars = 56;
            var roomWidth = viewport.ActualWidth - railRoom - edge;
            var roomHeight = viewport.ActualHeight - 2 * bars;
            scale.ScaleX = scale.ScaleY = Math.Clamp(Math.Min(roomWidth / width, roomHeight / height), .15, 1.15);
            pan.X = railRoom + (roomWidth - width * scale.ScaleX) / 2 - left * scale.ScaleX;
            pan.Y = bars + (roomHeight - height * scale.ScaleY) / 2 - top * scale.ScaleY;
        }

        void Run(Action work)
        {
            status.Text = "";
            try { work(); }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or
                UnauthorizedAccessException or ExternalException or NotSupportedException)
            { status.Text = error.Message; }
        }

        void Commit(Func<DeskCanvas, DeskCanvas> edit, Guid? focus)
        {
            var before = board;
            var after = board;
            DockDeskspaces.ChangeCanvas(desk, boardId, saved => { before = saved; return after = edit(saved); });
            undo.AddFirst(before);
            if (undo.Count > 24)
                undo.RemoveLast();
            board = after;
            Select(focus);
        }

        void Undo()
        {
            if (undo.First?.Value is not { } previous)
                return;
            Run(() =>
            {
                DockDeskspaces.ChangeCanvas(desk, boardId, _ => previous);
                undo.RemoveFirst();
                board = previous;
                Select(selected);
            });
        }

        void Select(Guid? id)
        {
            selected = id;
            Draw();
            Inspect();
        }

        BitmapSource? GetImage(string id)
        {
            if (!images.TryGetValue(id, out var image))
            {
                image = DockDeskImages.Open(id);
                images[id] = image;
            }
            return image;
        }

        void Choose(Guid id)
        {
            if (connectFrom is { } from && from != id && items.ContainsKey(id))
            {
                connectFrom = null;
                MarkTools();
                var arrow = DockCanvas.Create(DeskItemKind.Arrow, 0, 0) with { From = from, To = id };
                Run(() => Commit(current => current with { Items = [.. current.Items, arrow] }, arrow.Id));
            }
            else
                Select(id);
        }

        void Draw()
        {
            drawing.Children.Clear();
            items.Clear();
            links.Clear();
            adornments.Clear();
            foreach (var item in board.Items.Where(entry => entry.From == null))
                AddVisual(item);
            // Connectors sit above the items they join, so an arrowhead is never hidden under a shape.
            foreach (var item in board.Items.Where(entry => entry.From != null))
            {
                var color = CanvasBrush(item.Color, AssistantSecondary);
                var width = Math.Clamp(item.BorderWidth * 1.3, 1, 16) + (item.Id == selected ? 1.2 : 0);
                var line = new Path { Stroke = color, StrokeThickness = width, StrokeDashArray = BoardDashes(item.Stroke),
                    StrokeDashCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
                // A wide see-through twin makes a thin line easy to click.
                var hit = new Path { Stroke = Brushes.Transparent, StrokeThickness = Math.Max(14, width + 8), Cursor = Cursors.Hand };
                hit.MouseLeftButtonDown += (_, args) => { Choose(item.Id); args.Handled = true; };
                var end = new Polygon { Fill = color, IsHitTestVisible = false };
                var start = new Polygon { Fill = color, IsHitTestVisible = false };
                drawing.Children.Add(line);
                drawing.Children.Add(hit);
                drawing.Children.Add(end);
                drawing.Children.Add(start);
                links.Add((item, line, hit, end, start));
            }
            PositionLinks();
            Adorn();
        }

        // The item as it stands on screen right now, which differs from the saved one while it is being dragged.
        DeskBoardItem? Live(Guid? id)
        {
            if (id is not { } key || board.Items.FirstOrDefault(entry => entry.Id == key) is not { } item)
                return null;
            return items.TryGetValue(key, out var frame)
                ? item with { X = Canvas.GetLeft(frame), Y = Canvas.GetTop(frame), Width = frame.Width, Height = frame.Height }
                : item;
        }

        void PositionLinks()
        {
            ends.Clear();
            foreach (var (item, line, hit, endHead, startHead) in links)
            {
                if (Live(item.From) is not { } a || Live(item.To) is not { } b)
                    continue;
                var (fromSide, toSide) = DockCanvasGeometry.Ends(a, item.FromAnchor, b, item.ToAnchor);
                var start = DockCanvasGeometry.Anchor(a, fromSide);
                var finish = DockCanvasGeometry.Anchor(b, toSide);
                var startOut = DockCanvasGeometry.Normal(a, fromSide);
                var finishOut = DockCanvasGeometry.Normal(b, toSide);
                Geometry geometry;
                Vector arriving, leaving;
                switch (item.Route)
                {
                    case DeskRoute.Elbow:
                        var corners = DockCanvasGeometry.Elbow(start, startOut, finish, finishOut);
                        var figure = new PathFigure { StartPoint = corners[0] };
                        figure.Segments.Add(new PolyLineSegment(corners.Skip(1), true));
                        geometry = new PathGeometry([figure]);
                        arriving = -finishOut;
                        leaving = startOut;
                        break;
                    case DeskRoute.Curved:
                        var (first, second) = DockCanvasGeometry.Curve(start, startOut, finish, finishOut);
                        var curve = new PathFigure { StartPoint = start };
                        curve.Segments.Add(new BezierSegment(first, second, finish, true));
                        geometry = new PathGeometry([curve]);
                        arriving = -finishOut;
                        leaving = startOut;
                        break;
                    default:
                        geometry = new LineGeometry(start, finish);
                        arriving = finish - start;
                        leaving = finish - start;
                        break;
                }
                line.Data = geometry;
                hit.Data = geometry;
                var (atStart, atEnd) = BoardHeads(item);
                Arrowhead(endHead, finish, arriving, line.StrokeThickness, atEnd);
                Arrowhead(startHead, start, -leaving, line.StrokeThickness, atStart);
                ends[item.Id] = (start, finish);
            }
        }

        void AddVisual(DeskBoardItem item)
        {
            var shape = new Border { Width = item.Width, Height = item.Height, Background = Brushes.Transparent,
                BorderThickness = new Thickness(1), BorderBrush = item.Id == selected ? AccentFill : Brushes.Transparent,
                Cursor = Cursors.SizeAll, ClipToBounds = true, Focusable = true,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(item.Rotation) };
            AutomationProperties.SetName(shape, CanvasTitle(item.Kind));
            var body = new Grid();
            var rendered = CanvasVisual(item, item.Image == null ? null : GetImage(item.Image));
            rendered.IsHitTestVisible = false;
            body.Children.Add(rendered);
            shape.Child = body;
            Canvas.SetLeft(shape, item.X);
            Canvas.SetTop(shape, item.Y);
            items[item.Id] = shape;
            drawing.Children.Add(shape);
            Point? drag = null;
            shape.MouseLeftButtonDown += (_, args) =>
            {
                if (connectFrom != null)
                    Choose(item.Id);
                else
                {
                    shape.Focus();
                    drag = args.GetPosition(drawing);
                    shape.CaptureMouse();
                }
                args.Handled = true;
            };
            shape.MouseMove += (_, args) =>
            {
                if (drag is not { } from)
                    return;
                var delta = args.GetPosition(drawing) - from;
                Canvas.SetLeft(shape, item.X + delta.X);
                Canvas.SetTop(shape, item.Y + delta.Y);
                HideAdornments();
                PositionLinks();
            };
            shape.MouseLeftButtonUp += (_, args) =>
            {
                if (drag == null)
                    return;
                drag = null;
                shape.ReleaseMouseCapture();
                var x = Canvas.GetLeft(shape);
                var y = Canvas.GetTop(shape);
                if (Math.Abs(x - item.X) + Math.Abs(y - item.Y) > 3)
                    Run(() => Update(item.Id, entry => entry with { X = x, Y = y }));
                else
                    Choose(item.Id);
                args.Handled = true;
            };
            if (item.Id == selected)
            {
                var handle = new Thumb { Width = 13, Height = 13, Cursor = Cursors.SizeNWSE,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Background = AccentFill };
                AutomationProperties.SetName(handle, "Resize item");
                body.Children.Add(handle);
                handle.DragStarted += (_, _) => HideAdornments();
                handle.DragDelta += (_, args) =>
                {
                    shape.Width = Math.Clamp(shape.Width + args.HorizontalChange / scale.ScaleX, 20, 1800);
                    shape.Height = Math.Clamp(shape.Height + args.VerticalChange / scale.ScaleY, 20, 1400);
                    PositionLinks();
                };
                handle.DragCompleted += (_, _) => Run(() => Update(item.Id,
                    entry => entry with { Width = shape.Width, Height = shape.Height }));
            }
        }

        void HideAdornments()
        {
            foreach (var element in adornments)
                element.Visibility = Visibility.Collapsed;
        }

        // Handles around the selection: the four connection points and a rotation handle on an item, or the two
        // ends of a connector. Dragging from a connection point onto another item clips an arrow between them.
        void Adorn()
        {
            if (Live(selected) is not { } item)
                return;
            if (item.From != null)
            {
                if (ends.TryGetValue(item.Id, out var line))
                {
                    AddEndHandle(item, line.Start, fromEnd: true, line.End);
                    AddEndHandle(item, line.End, fromEnd: false, line.Start);
                }
                return;
            }
            if (item.Kind is not (DeskItemKind.Line or DeskItemKind.Arrow or DeskItemKind.Ink))
                foreach (var side in DockCanvasGeometry.Sides)
                {
                    var spot = DockCanvasGeometry.Anchor(item, side);
                    var dot = Dot(spot, 12, AccentFill, $"Connect from the {side.ToString().ToLowerInvariant()} point");
                    dot.Cursor = Cursors.Cross;
                    var source = item;
                    var fromSide = side;
                    DragLine(dot, spot, source.Id, target =>
                    {
                        var (targetItem, targetSide) = target;
                        var arrow = DockCanvas.Create(DeskItemKind.Arrow, 0, 0) with
                        {
                            From = source.Id, To = targetItem.Id, FromAnchor = fromSide, ToAnchor = targetSide, Route = lastRoute
                        };
                        Run(() => Commit(current => current with { Items = [.. current.Items, arrow] }, arrow.Id));
                    });
                }

            // The rotation handle floats above the top edge and turns with the item.
            var center = DockCanvasGeometry.Center(item);
            var up = DockCanvasGeometry.Rotate(new Vector(0, -(item.Height / 2 + 26)), item.Rotation);
            var knob = Dot(center + up, 14, Brush("#F4EFE4"), "Rotate");
            knob.Cursor = Cursors.Hand;
            var stem = new Line { X1 = center.X + up.X * .62, Y1 = center.Y + up.Y * .62, X2 = center.X + up.X, Y2 = center.Y + up.Y,
                Stroke = AccentFill, StrokeThickness = 1.5, IsHitTestVisible = false };
            drawing.Children.Insert(drawing.Children.IndexOf(knob), stem);
            adornments.Add(stem);
            var turning = false;
            knob.MouseLeftButtonDown += (_, args) =>
            {
                turning = knob.CaptureMouse();
                args.Handled = true;
            };
            knob.MouseMove += (_, args) =>
            {
                if (!turning || !items.TryGetValue(item.Id, out var frame))
                    return;
                var pointer = args.GetPosition(drawing) - center;
                var angle = DockCanvasGeometry.SnapAngle(Math.Atan2(pointer.Y, pointer.X) * 180 / Math.PI + 90,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                ((RotateTransform)frame.RenderTransform).Angle = angle;
                status.Text = $"{angle:0}°" + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "" : " · hold Shift to turn in 15° steps");
                foreach (var element in adornments.Where(element => element != knob && element != stem))
                    element.Visibility = Visibility.Collapsed;
            };
            knob.MouseLeftButtonUp += (_, args) =>
            {
                if (!turning)
                    return;
                turning = false;
                knob.ReleaseMouseCapture();
                args.Handled = true;
                if (items.TryGetValue(item.Id, out var frame))
                {
                    var angle = ((RotateTransform)frame.RenderTransform).Angle;
                    status.Text = "";
                    Run(() => Update(item.Id, entry => entry with { Rotation = angle }));
                }
            };
        }

        Ellipse Dot(Point center, double size, Brush fill, string label)
        {
            var dot = new Ellipse { Width = size / scale.ScaleX, Height = size / scale.ScaleX, Fill = fill,
                Stroke = Brush("#151412"), StrokeThickness = 1.5 / scale.ScaleX, ToolTip = label };
            AutomationProperties.SetName(dot, label);
            Canvas.SetLeft(dot, center.X - dot.Width / 2);
            Canvas.SetTop(dot, center.Y - dot.Height / 2);
            drawing.Children.Add(dot);
            adornments.Add(dot);
            return dot;
        }

        // The ends of a selected connector can be dragged to another point or another item.
        void AddEndHandle(DeskBoardItem connector, Point spot, bool fromEnd, Point anchoredAt)
        {
            var handle = Dot(spot, 13, Brush("#F4EFE4"), fromEnd ? "Move where the line starts" : "Move where the line ends");
            handle.Cursor = Cursors.Cross;
            var keep = fromEnd ? connector.To : connector.From;
            DragLine(handle, anchoredAt, keep, target =>
            {
                var (item, side) = target;
                Run(() => Update(connector.Id, entry => fromEnd
                    ? entry with { From = item.Id, FromAnchor = side }
                    : entry with { To = item.Id, ToAnchor = side }));
            }, hideLink: connector.Id);
        }

        // Drags a loose line from a fixed point; over an item it clips to that item's nearest connection point.
        void DragLine(FrameworkElement grip, Point fixedEnd, Guid? exclude, Action<(DeskBoardItem Item, DeskAnchor Side)> dropped,
            Guid? hideLink = null)
        {
            Line? preview = null;
            Ellipse? snap = null;
            (DeskBoardItem Item, DeskAnchor Side)? target = null;
            grip.MouseLeftButtonDown += (_, args) =>
            {
                if (!grip.CaptureMouse())
                    return;
                preview = new Line { X1 = fixedEnd.X, Y1 = fixedEnd.Y, X2 = fixedEnd.X, Y2 = fixedEnd.Y, Stroke = AccentFill,
                    StrokeThickness = 2 / scale.ScaleX, StrokeDashArray = new DoubleCollection([4, 3]), IsHitTestVisible = false };
                drawing.Children.Add(preview);
                if (hideLink is { } link && links.FirstOrDefault(entry => entry.Item.Id == link) is { Line: not null } shown)
                    shown.Line.Opacity = .25;
                args.Handled = true;
            };
            grip.MouseMove += (_, args) =>
            {
                if (preview == null)
                    return;
                var point = args.GetPosition(drawing);
                target = ItemAt(point, exclude) is { } over ? (over, DockCanvasGeometry.Nearest(over, point)) : null;
                var end = target is { } hit ? DockCanvasGeometry.Anchor(hit.Item, hit.Side) : point;
                preview.X2 = end.X;
                preview.Y2 = end.Y;
                if (snap != null)
                    drawing.Children.Remove(snap);
                snap = null;
                if (target != null)
                {
                    snap = new Ellipse { Width = 16 / scale.ScaleX, Height = 16 / scale.ScaleX, Stroke = AccentFill,
                        StrokeThickness = 2.5 / scale.ScaleX, IsHitTestVisible = false };
                    Canvas.SetLeft(snap, end.X - snap.Width / 2);
                    Canvas.SetTop(snap, end.Y - snap.Height / 2);
                    drawing.Children.Add(snap);
                }
                status.Text = target == null ? "Drop on another item to clip the line to it." : "";
            };
            grip.MouseLeftButtonUp += (_, args) =>
            {
                if (preview == null)
                    return;
                grip.ReleaseMouseCapture();
                drawing.Children.Remove(preview);
                if (snap != null)
                    drawing.Children.Remove(snap);
                preview = null;
                snap = null;
                args.Handled = true;
                if (target is { } chosen)
                    dropped(chosen);
                else
                    Draw();
                target = null;
            };
        }

        // The topmost item under a point, allowing for rotation and a small margin so edges are easy to hit.
        DeskBoardItem? ItemAt(Point point, Guid? exclude)
        {
            foreach (var item in board.Items.Where(entry => entry.From == null && entry.Id != exclude &&
                         entry.Kind is not (DeskItemKind.Line or DeskItemKind.Arrow or DeskItemKind.Ink)).Reverse())
            {
                var local = DockCanvasGeometry.Rotate(point - DockCanvasGeometry.Center(item), -item.Rotation);
                if (Math.Abs(local.X) <= item.Width / 2 + 10 && Math.Abs(local.Y) <= item.Height / 2 + 10)
                    return item;
            }
            return null;
        }

        void Inspect()
        {
            sidebar.Children.Clear();
            if (board.Items.FirstOrDefault(item => item.Id == selected) is not { } item)
            {
                var name = Field("Project canvas name", 100);
                name.Text = board.Name;
                var hint = Text("Drag items to arrange them. Right-drag to pan and scroll to zoom. Paste a picture, text, or spreadsheet cells.", 12, AssistantSecondary);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Margin = new Thickness(0, 0, 0, 14);
                sidebar.Children.Add(hint);
                sidebar.Children.Add(Labeled("Name", name));
                sidebar.Children.Add(QuietButton("Save canvas name", () => Run(() =>
                {
                    Commit(current => current with { Name = DockDeskspaces.Label(name.Text) }, null);
                    window.Title = board.Name;
                })));
                // Deleting asks by changing the button rather than opening a message box.
                var remove = QuietButton(deleteArmed ? $"Yes, delete {board.Name}" : "Delete canvas", () =>
                {
                    if (!deleteArmed)
                    {
                        deleteArmed = true;
                        Inspect();
                        return;
                    }
                    deleteArmed = false;
                    Run(() =>
                    {
                        DockDeskspaces.RemoveCanvas(desk, boardId);
                        back();
                    });
                });
                if (deleteArmed)
                    remove.Foreground = bad;
                sidebar.Children.Add(remove);
                if (deleteArmed)
                    sidebar.Children.Add(QuietButton("Keep it", () =>
                    {
                        deleteArmed = false;
                        Inspect();
                    }));
                sidebar.Children.Add(status);
                return;
            }

            sidebar.Children.Add(SectionTitle(CanvasTitle(item.Kind) + (item.From != null ? " (connector)" : "")));
            var hasText = item.Kind is not (DeskItemKind.Image or DeskItemKind.Ink or DeskItemKind.Line or DeskItemKind.Arrow);
            if (hasText)
            {
                var text = Field("Canvas item content", 12_000);
                text.AcceptsReturn = true;
                text.TextWrapping = TextWrapping.Wrap;
                text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                text.Height = item.Kind is DeskItemKind.Chart or DeskItemKind.Table ? 120 : 70;
                text.Text = item.Text;
                var caption = item.Kind switch
                {
                    DeskItemKind.Chart => "One label,value on each line",
                    DeskItemKind.Table => "Rows of tab- or comma-separated cells",
                    DeskItemKind.Sticker => "Sticker",
                    _ => "Text"
                };
                // Text is saved when you click away from it, like a note.
                text.LostKeyboardFocus += (_, _) =>
                {
                    if (text.Text == item.Text)
                        return;
                    if (item.Kind == DeskItemKind.Chart && DockCanvas.Chart(text.Text).Length == 0)
                    {
                        status.Text = "Add at least one label,value row.";
                        return;
                    }
                    var value = text.Text;
                    Dispatcher.BeginInvoke(() => Run(() => Update(item.Id, entry => entry with { Text = value })));
                };
                sidebar.Children.Add(Labeled(caption, text));
                if (item.Kind == DeskItemKind.Sticker)
                {
                    var stickers = new WrapPanel();
                    foreach (var symbol in new[] { "⭐", "💡", "📌", "🎯", "❤️", "⚡", "🌱", "🚀", "🎨", "🧩" })
                        stickers.Children.Add(QuietButton(symbol, () => Run(() => Update(item.Id, entry => entry with { Text = symbol }))));
                    sidebar.Children.Add(stickers);
                }
            }

            var isShape = item.Kind is DeskItemKind.Rectangle or DeskItemKind.Ellipse or DeskItemKind.Diamond;
            var isLine = item.From != null || item.Kind is DeskItemKind.Line or DeskItemKind.Arrow;
            if (isShape)
            {
                sidebar.Children.Add(Picker("Fill", item.Fill, allowNone: true, pick => Change(entry => entry with { Fill = pick })));
                sidebar.Children.Add(Picker("Border", item.BorderColor ?? item.Color, allowNone: false,
                    pick => Change(entry => entry with { BorderColor = pick })));
                sidebar.Children.Add(Choice("Border width", ["None", "Thin", "Medium", "Thick"], [0, 1.5, 3, 6], item.BorderWidth,
                    value => Change(entry => entry with { BorderWidth = value })));
                sidebar.Children.Add(StrokeChoice());
                sidebar.Children.Add(Picker("Text colour", item.Color, allowNone: false, pick => Change(entry => entry with { Color = pick! })));
            }
            else
            {
                sidebar.Children.Add(Picker(isLine ? "Line colour" : "Colour", item.Color, allowNone: false,
                    pick => Change(entry => entry with { Color = pick! })));
            }
            if (isLine || item.Kind == DeskItemKind.Ink)
            {
                sidebar.Children.Add(Choice("Line width", ["Thin", "Medium", "Thick", "Heavy"], [1, 2, 3.5, 6], item.BorderWidth,
                    value => Change(entry => entry with { BorderWidth = value })));
                sidebar.Children.Add(StrokeChoice());
            }
            if (isLine)
            {
                var heads = item.Heads != DeskHeads.Default ? item.Heads
                    : item.Kind == DeskItemKind.Line && item.From == null ? DeskHeads.None : DeskHeads.End;
                sidebar.Children.Add(Choice("Arrowheads", ["None", "End", "Start", "Both"],
                    [(double)DeskHeads.None, (double)DeskHeads.End, (double)DeskHeads.Start, (double)DeskHeads.Both], (double)heads,
                    value => Change(entry => entry with { Heads = (DeskHeads)(int)value, Reverse = false })));
            }
            if (item.From != null)
            {
                sidebar.Children.Add(Choice("Path", ["Straight", "Elbow", "Curved"], [0, 1, 2], (double)item.Route, value =>
                {
                    lastRoute = (DeskRoute)(int)value;
                    Change(entry => entry with { Route = lastRoute });
                }));
                string[] sides = ["Auto", "Top", "Right", "Bottom", "Left"];
                double[] values = [0, 1, 2, 3, 4];
                sidebar.Children.Add(Choice("Starts at", sides, values, (double)item.FromAnchor,
                    value => Change(entry => entry with { FromAnchor = (DeskAnchor)(int)value })));
                sidebar.Children.Add(Choice("Ends at", sides, values, (double)item.ToAnchor,
                    value => Change(entry => entry with { ToAnchor = (DeskAnchor)(int)value })));
            }
            else if (item.Kind is DeskItemKind.Line or DeskItemKind.Arrow)
                sidebar.Children.Add(Choice("Direction", ["↗", "↘", "→", "↓"], [0, 1, 2, 3], (double)item.Angle,
                    value => Change(entry => entry with { Angle = (DeskLineAngle)(int)value })));

            if (item.From == null)
            {
                sidebar.Children.Add(Caption("Rotation"));
                var angle = Field("Rotation in degrees", 4);
                angle.Width = 56;
                angle.Text = item.Rotation.ToString("0", CultureInfo.CurrentCulture);
                angle.KeyDown += (_, args) =>
                {
                    if (args.Key != Key.Enter)
                        return;
                    args.Handled = true;
                    if (double.TryParse(angle.Text.TrimEnd('°'), NumberStyles.Float, CultureInfo.CurrentCulture, out var degrees))
                        Change(entry => entry with { Rotation = DockCanvasGeometry.SnapAngle(degrees) });
                    else
                        status.Text = "Enter the rotation in degrees, such as 45.";
                };
                var turn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                turn.Children.Add(angle);
                foreach (var (label, step) in new[] { ("−15°", -15d), ("+15°", 15d), ("+90°", 90d) })
                {
                    var button = QuietButton(label, () => Change(entry => entry with { Rotation = DockCanvasGeometry.SnapAngle(entry.Rotation + step, true) }));
                    button.Margin = new Thickness(10, 0, 0, 0);
                    turn.Children.Add(button);
                }
                var reset = QuietButton("Straighten", () => Change(entry => entry with { Rotation = 0 }));
                reset.Margin = new Thickness(10, 0, 0, 0);
                turn.Children.Add(reset);
                sidebar.Children.Add(turn);
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (item.From == null)
            {
                actions.Children.Add(Gap(QuietButton("Duplicate", () => Run(() =>
                {
                    var copy = item with { Id = Guid.NewGuid(), X = item.X + 24, Y = item.Y + 24 };
                    Commit(current => current with { Items = [.. current.Items, copy] }, copy.Id);
                }))));
                actions.Children.Add(Gap(QuietButton("Bring to front", () => Run(() => Commit(current => current with
                    { Items = [.. current.Items.Where(entry => entry.Id != item.Id), current.Items.First(entry => entry.Id == item.Id)] }, item.Id)))));
            }
            var removeItem = QuietButton("Remove", () => Run(() => Commit(current => DockCanvas.Remove(current, item.Id), null)));
            removeItem.Foreground = bad;
            actions.Children.Add(removeItem);
            sidebar.Children.Add(actions);
            sidebar.Children.Add(status);

            void Change(Func<DeskBoardItem, DeskBoardItem> edit) => Run(() => Update(item.Id, edit));

            FrameworkElement StrokeChoice() => Choice("Line style", ["Solid", "Dashed", "Dotted"], [0, 1, 2], (double)item.Stroke,
                value => Change(entry => entry with { Stroke = (DeskStroke)(int)value }));

            static FrameworkElement Gap(FrameworkElement element)
            {
                element.Margin = new Thickness(0, 0, 14, 0);
                return element;
            }
        }

        // One row per colour: its name and current swatch. A click opens the palette under it; only one is open at a time.
        FrameworkElement Picker(string label, string? current, bool allowNone, Action<string?> pick)
        {
            var open = openPicker == label;
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var row = new DockPanel();
            var chevron = DockGlyph(open ? 0xE70E : 0xE70D, 10, AssistantFaint);
            chevron.VerticalAlignment = VerticalAlignment.Center;
            chevron.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(chevron, Dock.Right);
            row.Children.Add(chevron);
            var swatch = new Border { Width = 22, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = Brush("#33FFFFFF"), Background = DockCanvasGeometry.IsColor(current) ? CanvasBrush(current!, AssistantInk) : Brushes.Transparent };
            DockPanel.SetDock(swatch, Dock.Right);
            row.Children.Add(swatch);
            var note = current switch { null => " · soft tint", "none" => " · none", _ => "" };
            var name = Text(label + note, 12, AssistantSecondary);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var header = PlainButton(row, (open ? "Close " : "Choose ") + label.ToLowerInvariant(), () =>
            {
                openPicker = open ? null : label;
                Inspect();
            });
            header.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            header.Margin = new Thickness(-8, 0, -8, 0);
            panel.Children.Add(header);
            if (open)
                panel.Children.Add(Spaced(CanvasPalette(current, allowNone, pick), 0, 6));
            return panel;
        }

        TextBlock Caption(string label)
        {
            var caption = Text(label, 11, AssistantFaint);
            caption.Margin = new Thickness(0, 10, 0, 4);
            return caption;
        }

        // A labelled segmented choice. The closest value is shown as chosen, so older items with in-between widths still mark one.
        FrameworkElement Choice(string label, string[] labels, double[] values, double current, Action<double> choose)
        {
            var nearest = Array.IndexOf(values, values.MinBy(value => Math.Abs(value - current)));
            var panel = new StackPanel();
            panel.Children.Add(Caption(label));
            panel.Children.Add(Segmented(label, labels, nearest, index => choose(values[index])));
            return panel;
        }

        void Update(Guid id, Func<DeskBoardItem, DeskBoardItem> edit) =>
            Commit(current => current with { Items = current.Items.Select(item => item.Id == id ? edit(item) : item).ToArray() }, id);
    }

    // A small rounded card that floats over the board, for the tool rail and the zoom and edit controls.
    private Border Floating(UIElement child, HorizontalAlignment horizontal, VerticalAlignment vertical) => new()
    {
        Child = child, HorizontalAlignment = horizontal, VerticalAlignment = vertical,
        Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : controlFill,
        BorderBrush = AssistantRule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(3)
    };

    private Button ChipIcon(int glyph, string label, Action action)
    {
        var button = IconButton(glyph, label, action);
        button.Width = 32;
        button.Height = 30;
        button.Margin = new Thickness(0);
        return button;
    }

    // Tool icons drawn as the thing they make, so a rectangle tool looks like a rectangle.
    private FrameworkElement CanvasToolIcon(string tool)
    {
        var ink = SystemParameters.HighContrast ? SystemColors.ControlTextBrush : AssistantInk;
        var soft = SystemParameters.HighContrast ? SystemColors.ControlTextBrush : AssistantSecondary;
        FrameworkElement Glyph(int code) => DockGlyph(code, 15, ink);
        var canvas = new Canvas { Width = 18, Height = 18 };
        switch (tool)
        {
            case "pen":
                canvas.Children.Add(new Polyline { Points = new PointCollection { new(2, 13), new(5, 7), new(8, 12), new(11, 5), new(14, 11), new(16.5, 6) },
                    Stroke = ink, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round });
                return canvas;
            case "connect": return Glyph(0xE71B);
            case nameof(DeskItemKind.Sticky):
                canvas.Children.Add(new Polygon { Points = new PointCollection { new(2, 2), new(16, 2), new(16, 12), new(12, 16), new(2, 16) },
                    Fill = Brush("#E7BF68") });
                canvas.Children.Add(new Polygon { Points = new PointCollection { new(16, 12), new(12, 12), new(12, 16) }, Fill = Brush("#B8913F") });
                return canvas;
            case nameof(DeskItemKind.Text):
                return new TextBlock { Text = "T", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ink,
                    FontFamily = new FontFamily("Georgia"), HorizontalAlignment = HorizontalAlignment.Center };
            case nameof(DeskItemKind.Rectangle):
                canvas.Children.Add(Place(new Rectangle { Width = 15, Height = 12, Stroke = ink, StrokeThickness = 1.6, RadiusX = 2, RadiusY = 2 }, 1.5, 3));
                return canvas;
            case nameof(DeskItemKind.Ellipse):
                canvas.Children.Add(Place(new Ellipse { Width = 15, Height = 13, Stroke = ink, StrokeThickness = 1.6 }, 1.5, 2.5));
                return canvas;
            case nameof(DeskItemKind.Diamond):
                canvas.Children.Add(new Polygon { Points = new PointCollection { new(9, 1.5), new(16.5, 9), new(9, 16.5), new(1.5, 9) },
                    Stroke = ink, StrokeThickness = 1.6 });
                return canvas;
            case nameof(DeskItemKind.Line):
                canvas.Children.Add(new Line { X1 = 2, Y1 = 16, X2 = 16, Y2 = 2, Stroke = ink, StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                return canvas;
            case nameof(DeskItemKind.Arrow):
                canvas.Children.Add(new Line { X1 = 2, Y1 = 16, X2 = 14, Y2 = 4, Stroke = ink, StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round });
                canvas.Children.Add(new Polygon { Points = new PointCollection { new(16.5, 1.5), new(15.5, 9), new(9, 2.5) }, Fill = ink });
                return canvas;
            case nameof(DeskItemKind.Sticker): return Glyph(0xE734);
            case nameof(DeskItemKind.Image): return Glyph(0xEB9F);
            case nameof(DeskItemKind.Chart):
                foreach (var (x, height) in new[] { (2.0, 7.0), (7.5, 13.0), (13.0, 10.0) })
                    canvas.Children.Add(Place(new Rectangle { Width = 4, Height = height, Fill = x == 7.5 ? AccentFill : soft, RadiusX = 1, RadiusY = 1 },
                        x, 16 - height));
                return canvas;
            case nameof(DeskItemKind.Table):
                canvas.Children.Add(Place(new Rectangle { Width = 15, Height = 13, Stroke = ink, StrokeThickness = 1.4, RadiusX = 1.5, RadiusY = 1.5 }, 1.5, 2.5));
                canvas.Children.Add(new Line { X1 = 1.5, X2 = 16.5, Y1 = 7, Y2 = 7, Stroke = ink, StrokeThickness = 1.2 });
                canvas.Children.Add(new Line { X1 = 7, X2 = 7, Y1 = 2.5, Y2 = 15.5, Stroke = ink, StrokeThickness = 1.2 });
                return canvas;
            default: return Glyph(0xE710);
        }

        static UIElement Place(FrameworkElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            return element;
        }
    }

    // An arrowhead whose size follows the line's thickness, pointing along the direction the line arrives in.
    private static void Arrowhead(Polygon head, Point tip, Vector direction, double thickness, bool show)
    {
        head.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || direction.Length < .001)
            return;
        direction.Normalize();
        var across = new Vector(-direction.Y, direction.X);
        var length = 9 + thickness * 2.2;
        var half = 4 + thickness * 1.2;
        head.Points = new PointCollection([tip, tip - direction * length + across * half, tip - direction * length - across * half]);
    }

    // The dock's own colour first, then each canvas hue, every one from a pale tint to a deep shade and then the same
    // steps muted; a row of neutrals; and a box for any other hex colour.
    private FrameworkElement CanvasPalette(string? current, bool allowNone, Action<string?> pick)
    {
        var panel = new StackPanel();
        string[] hues = [chosenAccent.Hex, "#DE6F6F", "#E7BF68", "#A9BE83", "#8BC6B1", "#8BB5DA", "#9C8BDA", "#D58BC4"];
        string[] neutrals = ["#FFFFFF", "#E6E4DF", "#C4C1BA", "#9A968F", "#716D66", "#4D4A45", "#33312D", "#22201E", "#141311", "#000000"];
        var rows = hues.Select(DockCanvasGeometry.Shades).Append(neutrals).ToArray();
        for (var row = 0; row < rows.Length; row++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            foreach (var hex in rows[row])
                line.Children.Add(Swatch(hex, row == 0 ? "Dock colour" : null));
            panel.Children.Add(line);
        }
        var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        if (allowNone)
        {
            var none = QuietButton("No fill", () => pick("none"));
            none.Margin = new Thickness(0, 0, 12, 0);
            if (current == "none")
                none.Foreground = AssistantInk;
            custom.Children.Add(none);
        }
        var hexBox = Field("Any hex colour", 7);
        hexBox.Width = 78;
        hexBox.Text = DockCanvasGeometry.IsColor(current) ? current! : "";
        hexBox.Tag = "#RRGGBB";
        hexBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;
            args.Handled = true;
            var value = hexBox.Text.Trim();
            if (!value.StartsWith('#'))
                value = "#" + value;
            if (DockCanvasGeometry.IsColor(value))
                pick(value.ToUpperInvariant());
        };
        custom.Children.Add(hexBox);
        panel.Children.Add(custom);
        return panel;

        FrameworkElement Swatch(string hex, string? family)
        {
            var on = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
            var chip = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = CanvasBrush(hex, AssistantInk),
                BorderThickness = new Thickness(on ? 2 : 1), BorderBrush = on ? AssistantInk : Brush("#22FFFFFF")
            };
            var button = PlainButton(chip, $"{family ?? "Colour"} {hex}", () => pick(hex));
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0, 0, 2, 0);
            button.ToolTip = hex;
            if (on)
                AutomationProperties.SetItemStatus(button, "Chosen");
            return button;
        }
    }

    private static double EdgeInset(Border frame, Vector vector) => Math.Min(
        frame.Width / (2 * Math.Max(0.001, Math.Abs(vector.X))),
        frame.Height / (2 * Math.Max(0.001, Math.Abs(vector.Y))));

    private static string CanvasTitle(DeskItemKind kind) => kind == DeskItemKind.Ink ? "Drawing" :
        CanvasTools.First(item => item.Kind == kind).Title;
}
