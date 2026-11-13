using System.Windows;
using System.Windows.Controls;

namespace WindowsDock;

public partial class MainWindow
{
    private static readonly string[] Sections = ["Before pins", "After pins", "Before widgets", "Before clock"];

    private void RenderLayout(string section)
    {
        foreach (var item in settings.Layout.Where(item => item.Section == section))
        {
            if (item.Target.Length == 0)
                AddDivider();
            else
                AddButton(item.Label, new TextBlock { Text = item.Label, MaxWidth = 100, TextTrimming = TextTrimming.CharacterEllipsis }, () => Launch(item.Target));
        }
    }

    private void EditLayout(StackPanel panel)
    {
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = "Dividers and shortcuts", Foreground = StudioAccent, FontSize = 23 });
        var section = new ComboBox { ItemsSource = Sections, SelectedIndex = 0, Margin = new Thickness(0, 10, 0, 8) };
        panel.Children.Add(section);
        panel.Children.Add(new TextBlock { Text = "Shortcut label" });
        var label = new TextBox { MaxLength = 60 };
        panel.Children.Add(label);
        panel.Children.Add(new TextBlock { Text = "Web URL or full path to an EXE or LNK" });
        var target = new TextBox { MaxLength = 2048, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(target);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);
        panel.Children.Add(StudioButton("Add divider", () => Add("", "")));
        panel.Children.Add(StudioButton("Add shortcut", () =>
        {
            if (string.IsNullOrWhiteSpace(label.Text) || !DockSettings.IsShortcut(target.Text.Trim()))
                status.Text = "Enter a label and a valid shortcut target.";
            else
                Add(label.Text.Trim(), target.Text.Trim());
        }));
        for (var i = 0; i < settings.Layout.Length; i++)
        {
            var index = i;
            var item = settings.Layout[i];
            var row = new DockPanel();
            var remove = StudioButton("Remove", () =>
            {
                Apply(settings with { Layout = settings.Layout.Where((_, position) => position != index).ToArray() });
                EditLayout(panel);
            });
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var position = new ComboBox { ItemsSource = Sections, SelectedItem = item.Section, Margin = new Thickness(0, 0, 8, 7) };
            position.SelectionChanged += (_, _) =>
            {
                var items = settings.Layout.ToArray();
                items[index] = item with { Section = (string)position.SelectedItem };
                Apply(settings with { Layout = items });
            };
            DockPanel.SetDock(position, Dock.Right);
            row.Children.Add(position);
            row.Children.Add(new TextBlock { Text = item.Target.Length == 0 ? "Divider" : item.Label, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(row);
        }

        void Add(string name, string path)
        {
            if (settings.Layout.Length >= 24)
            {
                status.Text = "Remove an item before adding another.";
                return;
            }
            Apply(settings with { Layout = [..settings.Layout, new LayoutItem((string)section.SelectedItem, name, path)] });
            EditLayout(panel);
        }
    }
}
