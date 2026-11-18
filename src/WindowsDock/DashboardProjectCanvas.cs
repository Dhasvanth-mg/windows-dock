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
        var side = new ScrollViewer { Content = sidebar, Width = 238, Margin = new Thickness(12, 0, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var undo = new LinkedList<DeskCanvas>();
        var images = new Dictionary<string, BitmapSource?>(StringComparer.Ordinal);
        var items = new Dictionary<Guid, Border>();
        var links = new List<(DeskBoardItem Item, Line Line, Polygon Arrow)>();
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
            foreach (var item in board.Items.Where(entry => entry.From != null))
            {
                var color = CanvasBrush(item.Color, AssistantSecondary);
                var line = BoardStroke(item.Stroke, color);
                line.StrokeThickness = item.Id == selected ? 4 : 2;
                line.Cursor = Cursors.Hand;
                var arrow = new Polygon { Fill = color, IsHitTestVisible = false };
                line.MouseLeftButtonDown += (_, args) => { Choose(item.Id); args.Handled = true; };
                drawing.Children.Add(line);
                drawing.Children.Add(arrow);
                links.Add((item, line, arrow));
            }
            foreach (var item in board.Items.Where(entry => entry.From == null))
                AddVisual(item);
            PositionLinks();
        }

        void PositionLinks()
        {
            foreach (var (item, line, arrow) in links)
            {
                if (item.From is not { } from || item.To is not { } to || !items.TryGetValue(from, out var first) ||
                    !items.TryGetValue(to, out var second))
                    continue;
                var start = new Point(Canvas.GetLeft(first) + first.Width / 2, Canvas.GetTop(first) + first.Height / 2);
                var end = new Point(Canvas.GetLeft(second) + second.Width / 2, Canvas.GetTop(second) + second.Height / 2);
                var delta = end - start;
                if (delta.Length < 1)
                    delta = new Vector(1, 0);
                start += delta * Math.Min(.46, EdgeInset(first, delta));
                end -= delta * Math.Min(.46, EdgeInset(second, delta));
                SetBoardLine(line, arrow, item.Reverse ? end : start, item.Reverse ? start : end);
            }
        }

        void AddVisual(DeskBoardItem item)
        {
            var shape = new Border { Width = item.Width, Height = item.Height, Background = Brushes.Transparent,
                BorderThickness = new Thickness(1), BorderBrush = item.Id == selected ? AccentFill : Brushes.Transparent,
                Cursor = Cursors.SizeAll, ClipToBounds = true, Focusable = true };
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

            sidebar.Children.Add(SectionTitle(CanvasTitle(item.Kind)));
            var text = Field("Canvas item content", 12_000);
            text.AcceptsReturn = true;
            text.TextWrapping = TextWrapping.Wrap;
            text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            text.Height = item.Kind is DeskItemKind.Chart or DeskItemKind.Table ? 140 : 84;
            text.Text = item.Text;
            if (item.Kind is not (DeskItemKind.Image or DeskItemKind.Ink or DeskItemKind.Line or DeskItemKind.Arrow))
            {
                var caption = item.Kind switch
                {
                    DeskItemKind.Chart => "One label,value on each line",
                    DeskItemKind.Table => "Rows with tab-separated or comma-separated cells",
                    _ => "Text"
                };
                sidebar.Children.Add(Labeled(caption, text));
            }
            var colors = new WrapPanel();
            var color = Field("Item color hex", 7);
            color.Text = item.Color;
            foreach (var preset in CanvasColors)
            {
                var swatch = QuietButton(" ", () => color.Text = preset);
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.Background = CanvasBrush(preset, AssistantInk);
                AutomationProperties.SetName(swatch, "Color " + preset);
                colors.Children.Add(swatch);
            }
            sidebar.Children.Add(colors);
            if (item.Kind == DeskItemKind.Sticker)
            {
                var stickers = new WrapPanel();
                foreach (var symbol in new[] { "⭐", "💡", "📌", "🎯", "❤️", "⚡", "🌱", "🚀", "🎨", "🧩" })
                    stickers.Children.Add(QuietButton(symbol, () => text.Text = symbol));
                sidebar.Children.Add(stickers);
            }
            sidebar.Children.Add(Labeled("Color", color));
            var style = new ComboBox { ItemsSource = Enum.GetValues<DeskStroke>(), SelectedItem = item.Stroke };
            AutomationProperties.SetName(style, "Line style");
            if (item.Kind is DeskItemKind.Line or DeskItemKind.Arrow or DeskItemKind.Rectangle or DeskItemKind.Ellipse or DeskItemKind.Diamond or DeskItemKind.Ink)
                sidebar.Children.Add(Labeled("Line style", style));
            var reverse = new CheckBox { Content = item.From == null ? "Flip direction" : "Reverse arrow", IsChecked = item.Reverse };
            var angle = new ComboBox { ItemsSource = Enum.GetValues<DeskLineAngle>(), SelectedItem = item.Angle };
            AutomationProperties.SetName(angle, "Line direction");
            if (item.Kind is DeskItemKind.Arrow or DeskItemKind.Line)
            {
                if (item.From == null)
                    sidebar.Children.Add(Labeled("Direction", angle));
                sidebar.Children.Add(reverse);
            }
            sidebar.Children.Add(ActionButton("Apply changes", () => Run(() =>
            {
                if (color.Text.Length != 7 || color.Text[0] != '#' || !color.Text.Skip(1).All(Uri.IsHexDigit))
                    throw new ArgumentException("Use a hex color such as #8BB5DA.");
                if (item.Kind == DeskItemKind.Chart && DockCanvas.Chart(text.Text).Length == 0)
                    throw new ArgumentException("Add at least one label,value row.");
                Update(item.Id, current => current with { Text = text.Text, Color = color.Text,
                    Reverse = reverse.IsChecked == true, Stroke = (DeskStroke)(style.SelectedItem ?? DeskStroke.Solid),
                    Angle = (DeskLineAngle)(angle.SelectedItem ?? DeskLineAngle.Rising) });
            })));
            if (item.From == null)
            {
                sidebar.Children.Add(new WrapPanel { Children =
                {
                    QuietButton("Duplicate", () => Run(() =>
                    {
                        var copy = item with { Id = Guid.NewGuid(), X = item.X + 24, Y = item.Y + 24 };
                        Commit(current => current with { Items = [.. current.Items, copy] }, copy.Id);
                    })),
                    QuietButton("Bring to front", () => Run(() => Commit(current => current with
                        { Items = [.. current.Items.Where(entry => entry.Id != item.Id), current.Items.First(entry => entry.Id == item.Id)] }, item.Id)))
                } });
            }
            sidebar.Children.Add(QuietButton("Remove item", () => Run(() => Commit(current => DockCanvas.Remove(current, item.Id), null))));
            sidebar.Children.Add(status);
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

    private static double EdgeInset(Border frame, Vector vector) => Math.Min(
        frame.Width / (2 * Math.Max(0.001, Math.Abs(vector.X))),
        frame.Height / (2 * Math.Max(0.001, Math.Abs(vector.Y))));

    private static string CanvasTitle(DeskItemKind kind) => kind == DeskItemKind.Ink ? "Drawing" :
        CanvasTools.First(item => item.Kind == kind).Title;
}
