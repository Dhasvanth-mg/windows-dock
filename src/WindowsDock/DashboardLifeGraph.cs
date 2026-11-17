using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowLifeRelationships(Guid slot, Guid focus)
    {
        if (DockLife.Load().FirstOrDefault(item => item.Id == slot) is not { } saved)
            return;
        var world = DockLife.Tick(saved, DateTimeOffset.UtcNow);
        var (window, content, _) = CreateDockPanel("Relationships", world.World, 825, 0xE716, accent,
            backAction: () => ShowLifeSimulator(slot), backLabel: "Back to life simulator", height: 625, expandable: true);
        var directory = new[] { new LifePersonItem(world.Id, world.Player + " · You") }.Concat(world.People
            .OrderByDescending(person => person.Alive).ThenBy(person => person.Name)
            .Select(person => new LifePersonItem(person.Id, person.Name + (person.Alive ? "" : " · Deceased")))).ToArray();
        var select = new ComboBox { ItemsSource = directory, DisplayMemberPath = nameof(LifePersonItem.Label),
            SelectedItem = directory.FirstOrDefault(item => item.Id == focus) ?? directory[0] };
        AutomationProperties.SetName(select, "Focus person");
        var map = new Canvas { Width = 690, Height = 378, Background = assistantInset, ClipToBounds = true };
        var pager = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var note = Text("", 12, AssistantSecondary);
        note.TextWrapping = TextWrapping.Wrap;
        var page = 0;
        var portraits = new Dictionary<Guid, ImageSource>();
        content.Children.Add(Labeled("Focus", select));
        content.Children.Add(new Viewbox { Child = map, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        content.Children.Add(pager);
        content.Children.Add(note);
        select.SelectionChanged += (_, _) => { page = 0; Draw(); };
        Draw();
        window.Show();

        void Draw()
        {
            if (select.SelectedItem is not LifePersonItem center)
                return;
            map.Children.Clear();
            pager.Children.Clear();
            var edges = DockLife.Relationships(world, center.Id);
            const int pageSize = 8;
            var displayed = edges.Skip(page * pageSize).Take(pageSize).ToArray();
            var origin = new Point(map.Width / 2, map.Height / 2);
            var nodes = new List<(LifeRelationship Person, Point Position)>();
            for (var index = 0; index < displayed.Length; index++)
            {
                var edge = displayed[index];
                var angle = Math.PI * 2 * index / displayed.Length - (displayed.Length < 3 ? Math.PI / 8 : Math.PI / 2);
                var point = origin + new Vector(Math.Cos(angle) * 250, Math.Sin(angle) * 132);
                var lineColor = edge.Kind is "Spouse" or "Partner" or "Dating" ? accent : edge.Kind is "Parent" or "Child" or "Sibling" ? amber : AssistantFaint;
                var ray = new Line { X1 = origin.X, Y1 = origin.Y, X2 = point.X, Y2 = point.Y,
                    Stroke = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : lineColor, StrokeThickness = 1.4 };
                if (!edge.Alive)
                    ray.StrokeDashArray = new DoubleCollection([4, 4]);
                map.Children.Add(ray);
                nodes.Add((edge, point));
            }
            foreach (var (person, position) in nodes)
                AddPerson(person.Id, person.Name, person.Kind, person.Alive, position, false);
            var centerPerson = world.People.FirstOrDefault(item => item.Id == center.Id);
            AddPerson(center.Id, centerPerson?.Name ?? world.Player,
                centerPerson == null ? "You" : centerPerson.Alive ? centerPerson.Job : "Deceased", centerPerson?.Alive ?? true, origin, true);
            if (page > 0)
                pager.Children.Add(QuietButton("Previous", () => { page--; Draw(); }));
            if ((page + 1) * pageSize < edges.Length)
                pager.Children.Add(QuietButton("Next", () => { page++; Draw(); }));
            note.Text = edges.Length == 0 ? "No saved relationships for this person yet."
                : $"{edges.Length} {(edges.Length == 1 ? "connection" : "connections")} · Select a card to see their relationships.";
        }

        void AddPerson(Guid id, string name, string label, bool alive, Point point, bool center)
        {
            if (!portraits.TryGetValue(id, out var illustration))
                portraits[id] = illustration = LifePortrait(id);
            var avatar = new Image { Source = illustration, Width = 32, Height = 32, Opacity = alive ? 1 : .55,
                Margin = new Thickness(0, 0, 5, 0) };
            var caption = Text(name, 12, AssistantInk, true);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            var relation = Text(label + (!alive && !center ? " · Deceased" : ""), 10, AssistantSecondary);
            relation.TextTrimming = TextTrimming.CharacterEllipsis;
            var face = new DockPanel { Children = { avatar, new StackPanel { Children = { caption, relation } } } };
            var tile = ActionButton(name, () => select.SelectedItem = directory.First(item => item.Id == id));
            tile.Content = face;
            tile.Width = 146;
            tile.Height = 49;
            tile.Margin = new Thickness(0);
            tile.Padding = new Thickness(7, 3, 7, 3);
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            tile.Background = controlSelected;
            if (center)
            {
                tile.BorderBrush = AccentFill;
                tile.BorderThickness = new Thickness(1);
                tile.Focusable = false;
            }
            AutomationProperties.SetName(tile, $"{name}, {label}");
            Canvas.SetLeft(tile, point.X - tile.Width / 2);
            Canvas.SetTop(tile, point.Y - tile.Height / 2);
            map.Children.Add(tile);
        }
    }

    private static ImageSource LifePortrait(Guid id)
    {
        var parts = id.ToByteArray();
        var backgrounds = new[] { 0x394359, 0x54363E, 0x2A4640, 0x4E405C, 0x59452E, 0x354A57 };
        var skinColors = new[] { 0xE9B68F, 0xBF826B, 0x94614B, 0x70412F, 0xF2CBAB };
        var hairColors = new[] { 0x272625, 0x603D2D, 0xA05C40, 0xB09D88, 0x4B3C47 };
        var shirtColors = new[] { 0xC5A95C, 0x759C91, 0x9E768E, 0x7297B4, 0xC48672 };
        var skin = Brush(skinColors[parts[1] % skinColors.Length]);
        var hair = Brush(hairColors[parts[2] % hairColors.Length]);
        var face = new DrawingGroup();
        using (var draw = face.Open())
        {
            draw.DrawRoundedRectangle(Brush(backgrounds[parts[0] % backgrounds.Length]), null, new Rect(0, 0, 48, 48), 8, 8);
            if (parts[4] % 3 == 0)
                draw.DrawEllipse(hair, null, new Point(24, 23), 15, 18);
            draw.DrawGeometry(Brush(shirtColors[parts[3] % shirtColors.Length]), null,
                Geometry.Parse("M3,47 C4,41 11,36 20,35 L28,35 C37,36 44,41 45,47 Z"));
            draw.DrawRoundedRectangle(skin, null, new Rect(20, 29, 8, 10), 2, 2);
            draw.DrawEllipse(skin, null, new Point(13, 22), 2.4, 3);
            draw.DrawEllipse(skin, null, new Point(35, 22), 2.4, 3);
            draw.DrawEllipse(skin, null, new Point(24, 20), 11, 15);
            draw.DrawGeometry(hair, null, Geometry.Parse((parts[4] % 3) switch
            {
                0 => "M12,23 C7,2 35,-1 37,17 L34,24 C30,20 31,14 30,12 C23,17 18,16 14,15 Z",
                1 => "M12,19 C9,7 18,4 24,4 C33,3 37,9 36,19 L33,16 C29,15 24,14 20,11 L14,18 Z",
                _ => "M12,20 C9,15 10,7 17,6 C20,1 28,3 31,6 C37,5 39,12 36,20 L32,15 L19,14 L14,16 Z"
            }));
            var details = Brush(0x291D1C);
            draw.DrawEllipse(details, null, new Point(19.2, 22), 1.1, 1.15);
            draw.DrawEllipse(details, null, new Point(28.8, 22), 1.1, 1.15);
            draw.DrawGeometry(null, new Pen(Brush(0x7A463F), 1.2), Geometry.Parse("M21,28 Q24,30 27,28"));
            if (parts[5] % 4 == 0)
            {
                var wire = new Pen(details, .8);
                draw.DrawEllipse(null, wire, new Point(19, 22), 3.8, 3.1);
                draw.DrawEllipse(null, wire, new Point(29, 22), 3.8, 3.1);
                draw.DrawLine(wire, new Point(23, 22), new Point(25, 22));
            }
        }
        var image = new DrawingImage(face);
        image.Freeze();
        return image;

        static SolidColorBrush Brush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }
}
