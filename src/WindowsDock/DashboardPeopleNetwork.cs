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
    private void ShowPeopleNetwork(Guid desk, Guid mapId)
    {
        var map = DockDeskspaces.Get(desk).Maps.FirstOrDefault(item => item.Id == mapId);
        if (map == null)
            return;
        var layout = new DockPanel();
        var (window, _, _) = CreateDockPanel(map.Name, "People network", 1050, 0xE8F1, accent,
            backAction: () => ShowDeskspaces(desk), backLabel: "Back to deskspace", body: layout, height: 640, expandable: true);
        var viewport = new Grid { ClipToBounds = true, Background = assistantInset };
        var graph = new Canvas { Background = Brushes.Transparent };
        var scale = new ScaleTransform(1, 1);
        var pan = new TranslateTransform(34, 32);
        graph.RenderTransform = new TransformGroup { Children = { scale, pan } };
        viewport.Children.Add(graph);
        var sidebar = new StackPanel();
        var editor = new ScrollViewer { Content = sidebar, Width = 245, Margin = new Thickness(12, 0, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(editor, Dock.Right);
        layout.Children.Add(editor);
        var people = DockDeskspaces.People().ToDictionary(item => item.Id);
        var photos = people.Values.ToDictionary(item => item.Id, item => DockDeskImages.Open(item.Photo, 76));
        var undo = new Stack<DeskMap>();
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        Guid? selected = null;
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        var clue = Text("Drag nodes · right-drag to pan · scroll to zoom", 11, AssistantSecondary);
        clue.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(new WrapPanel { Children =
        {
            ActionButton("Add person", Add),
            QuietButton("Directory", () => ShowDeskPeople(desk, mapId)),
            QuietButton("Network details", () => { selected = null; Draw(); EditPanel(); }),
            QuietButton("Fit network", Fit),
            QuietButton("Undo", Undo),
            clue
        } });
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(viewport);
        Point? panStart = null;
        Point startPan = default;

        viewport.PreviewMouseWheel += (_, args) =>
        {
            var position = args.GetPosition(viewport);
            var previous = scale.ScaleX;
            var next = Math.Clamp(previous * (args.Delta > 0 ? 1.15 : 1 / 1.15), .2, 2.2);
            var ratio = next / previous;
            pan.X = position.X - (position.X - pan.X) * ratio;
            pan.Y = position.Y - (position.Y - pan.Y) * ratio;
            scale.ScaleX = scale.ScaleY = next;
            args.Handled = true;
        };
        viewport.PreviewMouseRightButtonDown += (_, args) =>
        {
            panStart = args.GetPosition(viewport);
            startPan = new Point(pan.X, pan.Y);
            viewport.CaptureMouse();
            args.Handled = true;
        };
        viewport.MouseMove += (_, args) =>
        {
            if (panStart is not { } start)
                return;
            var delta = args.GetPosition(viewport) - start;
            pan.X = startPan.X + delta.X;
            pan.Y = startPan.Y + delta.Y;
        };
        viewport.PreviewMouseRightButtonUp += (_, args) =>
        {
            panStart = null;
            viewport.ReleaseMouseCapture();
            args.Handled = true;
        };
        viewport.LostMouseCapture += (_, _) => panStart = null;
        window.Loaded += (_, _) => Fit();
        Draw();
        EditPanel();
        window.Show();

        string Title(DeskNode node) => node.Person is { } id && people.TryGetValue(id, out var person) ? person.Name : node.Title;

        void Fit()
        {
            if (map.Nodes.Length == 0 || viewport.ActualWidth < 80 || viewport.ActualHeight < 80)
            {
                scale.ScaleX = scale.ScaleY = 1;
                pan.X = 34;
                pan.Y = 32;
                return;
            }
            var left = map.Nodes.Min(node => node.X);
            var top = map.Nodes.Min(node => node.Y);
            var width = map.Nodes.Max(node => node.X) - left + 154;
            var height = map.Nodes.Max(node => node.Y) - top + 50;
            scale.ScaleX = scale.ScaleY = Math.Clamp(Math.Min((viewport.ActualWidth - 48) / width,
                (viewport.ActualHeight - 48) / height), .2, 1.15);
            pan.X = (viewport.ActualWidth - width * scale.ScaleX) / 2 - left * scale.ScaleX;
            pan.Y = (viewport.ActualHeight - height * scale.ScaleY) / 2 - top * scale.ScaleY;
        }

        Point Center() => new((Math.Max(220, viewport.ActualWidth) / 2 - pan.X) / scale.ScaleX - 75,
            (Math.Max(100, viewport.ActualHeight) / 2 - pan.Y) / scale.ScaleY - 23);

        void Add()
        {
            sidebar.Children.Clear();
            var title = Field("New person name", 100);
            var note = Field("Node notes", 8000);
            note.AcceptsReturn = true;
            note.TextWrapping = TextWrapping.Wrap;
            note.Height = 82;
            sidebar.Children.Add(Labeled("Name", title));
            sidebar.Children.Add(Labeled("Notes", note));
            var directory = new ComboBox { ItemsSource = people.Values.Where(person => !map.Nodes.Any(node => node.Person == person.Id))
                .OrderBy(item => item.Name).ToArray(), DisplayMemberPath = nameof(DeskPerson.Name), SelectedIndex = 0 };
            AutomationProperties.SetName(directory, "Existing person");
            sidebar.Children.Add(Labeled("From the directory", directory));
            sidebar.Children.Add(QuietButton("Add existing person", () =>
            {
                if (directory.SelectedItem is DeskPerson entry)
                    Insert(entry.Name, note.Text, entry.Id);
            }));
            sidebar.Children.Add(ActionButton("Create person node", () => Run(() =>
            {
                var entry = DockDeskspaces.SavePerson(null, title.Text, "");
                people[entry.Id] = entry;
                Insert(entry.Name, note.Text, entry.Id);
            })));
            sidebar.Children.Add(QuietButton("Cancel", EditPanel));
            sidebar.Children.Add(status);
            status.Text = "";
            title.Focus();
        }

        void Insert(string title, string notes, Guid? linked)
        {
            Run(() =>
            {
                var point = Center();
                var offset = 0;
                while (map.Nodes.Any(node => Math.Abs(node.X - point.X) < 100 && Math.Abs(node.Y - point.Y) < 46))
                {
                    point.Offset(36, 36);
                    if (++offset == 20) break;
                }
                var node = DockDeskspaces.Node(title, notes, point.X, point.Y, linked);
                Commit(saved => saved with { Nodes = [.. saved.Nodes, node] }, node.Id);
            });
        }

        void Commit(Func<DeskMap, DeskMap> change, Guid? focus)
        {
            var before = map;
            DockDeskspaces.ChangeMap(desk, mapId, saved => { before = saved; return map = change(saved); });
            undo.Push(before);
            selected = focus;
            Draw();
            EditPanel();
        }

        void Undo()
        {
            if (!undo.TryPeek(out var before))
                return;
            Run(() =>
            {
                DockDeskspaces.ChangeMap(desk, mapId, _ => before);
                undo.Pop();
                map = before;
                Draw();
                EditPanel();
            });
        }

        void Run(Action action)
        {
            status.Text = "";
            try { action(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { status.Text = error.Message; }
        }

        void EditPanel()
        {
            sidebar.Children.Clear();
            if (map.Nodes.FirstOrDefault(node => node.Id == selected) is not { } node)
            {
                sidebar.Children.Add(Text("Select a person to edit notes and relationships.", 12, AssistantSecondary));
                sidebar.Children.Add(QuietButton("Rename network", Rename));
                sidebar.Children.Add(QuietButton("Delete network", () =>
                {
                    if (MessageBox.Show(window, $"Delete {map.Name}?", "People network", MessageBoxButton.YesNo,
                        MessageBoxImage.None, MessageBoxResult.No) == MessageBoxResult.Yes)
                        Run(() => { DockDeskspaces.Change(desk, item => item with { Maps = item.Maps.Where(saved => saved.Id != mapId).ToArray() }); ShowDeskspaces(desk); });
                }));
                sidebar.Children.Add(status);
                return;
            }
            var title = Field("Node title", 100);
            title.Text = Title(node);
            title.IsReadOnly = node.Person != null;
            var notes = Field("Node notes", 8000);
            notes.Text = node.Notes;
            notes.AcceptsReturn = true;
            notes.TextWrapping = TextWrapping.Wrap;
            notes.Height = 80;
            sidebar.Children.Add(Labeled(node.Person != null ? "Person" : "Thought", title));
            if (node.Person is { } person)
                sidebar.Children.Add(QuietButton("Open contact profile", () => ShowDeskPeople(desk, mapId, person)));
            sidebar.Children.Add(Labeled("Notes in this network", notes));
            sidebar.Children.Add(ActionButton("Save node", () => Run(() => Commit(board => board with
            {
                Nodes = board.Nodes.Select(item => item.Id == node.Id ? item with
                    { Title = DockDeskspaces.Label(title.Text), Notes = notes.Text } : item).ToArray()
            }, node.Id))));
            sidebar.Children.Add(QuietButton("Remove node", () => Run(() =>
                Commit(board => DockDeskspaces.RemoveNode(board, node.Id), null))));
            var target = new ComboBox { ItemsSource = map.Nodes.Where(item => item.Id != node.Id)
                .Select(item => new DeskResource(item.Id, Title(item))).ToArray(), DisplayMemberPath = nameof(DeskResource.Name) };
            AutomationProperties.SetName(target, "Node to connect");
            var label = Field("Connection label", 60);
            label.Tag = "Relationship (optional)";
            sidebar.Children.Add(Labeled("Connect to", target));
            sidebar.Children.Add(label);
            sidebar.Children.Add(ActionButton("Save connection", () =>
            {
                if (target.SelectedItem is DeskResource item)
                    Run(() => Commit(board => DockDeskspaces.AddEdge(board, node.Id, item.Id, label.Text), node.Id));
            }));
            foreach (var edge in map.Edges.Where(edge => edge.From == node.Id || edge.To == node.Id))
            {
                var id = edge.From == node.Id ? edge.To : edge.From;
                var other = map.Nodes.FirstOrDefault(item => item.Id == id);
                if (other != null)
                    sidebar.Children.Add(DeskRow(ActionButton(Title(other), () => { selected = id; Draw(); EditPanel(); }),
                        () => Run(() => Commit(board => board with { Edges = board.Edges.Where(item => item != edge).ToArray() }, node.Id)),
                        "Remove connection with " + Title(other)));
            }
            sidebar.Children.Add(status);
        }

        void Rename()
        {
            sidebar.Children.Clear();
            var field = Field("Network name", 100);
            field.Text = map.Name;
            sidebar.Children.Add(field);
            sidebar.Children.Add(ActionButton("Save network name", () => Run(() =>
            {
                Commit(saved => saved with { Name = DockDeskspaces.Label(field.Text) }, null);
                window.Title = map.Name;
            })));
            sidebar.Children.Add(status);
        }

        void Draw()
        {
            graph.Children.Clear();
            var dots = map.Nodes.ToDictionary(node => node.Id, node => new Point(node.X, node.Y));
            var lines = new List<(DeskEdge Edge, Line Line, TextBlock Label)>();
            foreach (var edge in map.Edges.Where(item => dots.ContainsKey(item.From) && dots.ContainsKey(item.To)))
            {
                var line = new Line { Stroke = AssistantFaint, StrokeThickness = 2, IsHitTestVisible = false };
                var tag = Text(edge.Label, 10, AssistantSecondary);
                tag.Background = assistantInset;
                tag.IsHitTestVisible = false;
                lines.Add((edge, line, tag));
                graph.Children.Add(line);
                graph.Children.Add(tag);
            }
            PlaceEdges();
            foreach (var node in map.Nodes)
            {
                var name = Text(Title(node), 12, AssistantInk, true);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                var contents = new DockPanel();
                if (node.Person is { } person && photos.TryGetValue(person, out var photo) && photo != null)
                    contents.Children.Add(new Image { Source = photo, Width = 32, Height = 32, Margin = new Thickness(0, 0, 7, 0),
                        Stretch = Stretch.UniformToFill, IsHitTestVisible = false });
                contents.Children.Add(new StackPanel { Children = { name, Text(node.Person != null ? "Person" : "Thought", 10, AssistantSecondary) } });
                var shape = new Border { Width = 154, Height = 50, CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 6, 9, 6), Cursor = Cursors.Hand,
                    BorderBrush = node.Id == selected ? AccentFill : AssistantRule, BorderThickness = new Thickness(1),
                    Background = controlSelected, Child = contents };
                AutomationProperties.SetName(shape, Title(node));
                Canvas.SetTop(shape, node.Y);
                Canvas.SetLeft(shape, node.X);
                graph.Children.Add(shape);
                Point? drag = null;
                shape.MouseLeftButtonDown += (_, args) =>
                {
                    drag = args.GetPosition(graph);
                    shape.CaptureMouse();
                    args.Handled = true;
                };
                shape.MouseMove += (_, args) =>
                {
                    if (drag is not { } start)
                        return;
                    var delta = args.GetPosition(graph) - start;
                    var point = new Point(node.X + delta.X, node.Y + delta.Y);
                    dots[node.Id] = point;
                    Canvas.SetLeft(shape, point.X);
                    Canvas.SetTop(shape, point.Y);
                    PlaceEdges();
                };
                shape.MouseLeftButtonUp += (_, _) =>
                {
                    drag = null;
                    shape.ReleaseMouseCapture();
                    var point = dots[node.Id];
                    if (Math.Abs(point.X - node.X) + Math.Abs(point.Y - node.Y) > 3)
                        Run(() => Commit(board => board with
                        {
                            Nodes = board.Nodes.Select(item => item.Id == node.Id ? item with { X = point.X, Y = point.Y } : item).ToArray()
                        }, node.Id));
                    else
                    {
                        selected = node.Id;
                        Draw();
                        EditPanel();
                    }
                };
            }

            void PlaceEdges()
            {
                foreach (var (edge, line, label) in lines)
                {
                    var first = dots[edge.From];
                    var second = dots[edge.To];
                    line.X1 = first.X + 77;
                    line.Y1 = first.Y + 25;
                    line.X2 = second.X + 77;
                    line.Y2 = second.Y + 25;
                    Canvas.SetLeft(label, (line.X1 + line.X2) / 2);
                    Canvas.SetTop(label, (line.Y1 + line.Y2) / 2 - 12);
                }
            }
        }
    }
}
