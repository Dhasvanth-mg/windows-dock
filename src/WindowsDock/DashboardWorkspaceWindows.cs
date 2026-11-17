using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private StackPanel WorkspaceWindowPicker(WorkspaceWindow[] saved, Action<WorkspaceWindow[]> capture)
    {
        var panel = new StackPanel();
        var options = new StackPanel { Visibility = Visibility.Collapsed };
        var choose = ActionButton("Choose open windows");
        choose.HorizontalAlignment = HorizontalAlignment.Left;
        var list = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = list,
            MaxHeight = 144,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var layout = new ComboBox { ItemsSource = new[] { "Side by side", "Stacked", "Grid" }, SelectedIndex = 2 };
        AutomationProperties.SetName(layout, "Tiling layout");
        var use = ActionButton("Use selected windows");
        var tile = ActionButton("Tile selected");
        var undo = ActionButton("Undo tiling");
        undo.IsEnabled = false;
        var refresh = ActionButton("Refresh window list");
        var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, 4), Children = { use, tile, undo, refresh } };
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        options.Children.Add(scroll);
        options.Children.Add(layout);
        options.Children.Add(buttons);
        options.Children.Add(status);
        panel.Children.Add(choose);
        panel.Children.Add(options);

        (WorkspaceSelection Target, CheckBox Check)[] choices = [];
        WorkspaceSelection[] lastPositions = [];
        choose.Click += (_, _) =>
        {
            if (options.Visibility == Visibility.Visible)
                options.Visibility = Visibility.Collapsed;
            else
            {
                Refresh();
                options.Visibility = Visibility.Visible;
            }
        };
        refresh.Click += (_, _) => Refresh();
        use.Click += (_, _) =>
        {
            var selected = SelectedLive();
            if (selected.Length == 0)
            {
                status.Text = "The selected windows are no longer available.";
                Refresh();
                return;
            }
            capture(selected.Select(item => item.Placement).ToArray());
            status.Text = $"Captured {selected.Length} window(s). Save the workspace to keep this layout.";
        };
        tile.Click += (_, _) =>
        {
            var before = SelectedLive();
            if (before.Length == 0)
                return;
            var target = DockWorkspaces.Arrange(before, (WorkspaceTiling)layout.SelectedIndex);
            if (!DockWorkspaces.FitsTiles(target))
            {
                status.Text = "Choose Grid or select fewer windows for this display.";
                return;
            }
            var missed = DockWorkspaces.PlaceSelected(target);
            if (missed < target.Length)
                lastPositions = before;
            undo.IsEnabled = lastPositions.Length != 0;
            status.Text = missed == 0 ? "Tiles applied. Use selected windows to save the layout." :
                $"Windows could not position {missed} selected window(s).";
        };
        undo.Click += (_, _) =>
        {
            var missed = DockWorkspaces.PlaceSelected(lastPositions);
            lastPositions = [];
            undo.IsEnabled = false;
            status.Text = missed == 0 ? "Previous positions restored." : $"{missed} original window(s) could not be restored.";
        };
        return panel;

        WorkspaceSelection[] SelectedLive()
        {
            var selected = choices.Where(item => item.Check.IsChecked == true).Select(item => item.Target).ToArray();
            return DockWorkspaces.AvailableWindows().Where(live => selected.Any(item => SameWindow(item, live))).ToArray();
        }

        void Refresh()
        {
            var previous = choices;
            choices = DockWorkspaces.AvailableWindows().Select(item =>
            {
                var earlier = previous.FirstOrDefault(entry => SameWindow(entry.Target, item)).Check;
                var marked = earlier?.IsChecked == true || previous.Length == 0 &&
                    saved.Any(entry => entry.Title == item.Placement.Title &&
                        entry.Path.Equals(item.Placement.Path, StringComparison.OrdinalIgnoreCase));
                var text = Text($"{Path.GetFileNameWithoutExtension(item.Window.Path)} · {item.Window.Title}", 12, AssistantInk);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                var check = new CheckBox { Content = text, IsChecked = marked, Margin = new Thickness(2, 3, 0, 3) };
                AutomationProperties.SetName(check, "Capture " + item.Window.Title);
                check.Checked += (_, _) => UpdateButtons();
                check.Unchecked += (_, _) => UpdateButtons();
                return (item, check);
            }).ToArray();
            list.Children.Clear();
            foreach (var item in choices)
                list.Children.Add(item.Check);
            if (choices.Length == 0)
                list.Children.Add(Text("No supported open windows.", 11, AssistantSecondary));
            UpdateButtons();
        }

        void UpdateButtons()
        {
            var count = choices.Count(item => item.Check.IsChecked == true);
            use.IsEnabled = count > 0;
            tile.IsEnabled = count > 0;
        }
    }

    private static bool SameWindow(WorkspaceSelection left, WorkspaceSelection right) =>
        left.Window.Handle == right.Window.Handle && left.Window.ProcessId == right.Window.ProcessId;
}
