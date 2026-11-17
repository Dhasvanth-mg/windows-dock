using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowsDock;

public partial class DashboardWindow
{
    private void ShowProjectCanvases()
    {
        var (window, content, _) = CreateDockPanel("Project canvases", "", 620, 0xE7C3, accent, widget: true, height: 520);
        var name = Field("New canvas name", 100);
        name.Tag = "New canvas name";
        var feedback = Text("", 12, AssistantSecondary);
        feedback.TextWrapping = TextWrapping.Wrap;
        var create = ActionButton("Create canvas", Create);
        MarkPrimary(create, true);
        create.Margin = new Thickness(8, 0, 0, 0);
        var form = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
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
        content.Children.Add(feedback);

        foreach (var entry in DockDeskspaces.ListCanvases())
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            if (entry.SpaceId is { } desk)
            {
                var move = QuietButton("Make standalone", () => Run(() =>
                {
                    DockDeskspaces.MakeCanvasStandalone(desk, entry.Canvas.Id);
                    ShowProjectCanvas(null, entry.Canvas.Id);
                }));
                DockPanel.SetDock(move, Dock.Right);
                row.Children.Add(move);
            }
            var labels = new StackPanel { Children = { Text(entry.Canvas.Name, 13, AssistantInk) } };
            if (entry.SpaceName is { } spaceName)
                labels.Children.Add(Text(spaceName, 11, AssistantSecondary));
            var open = MakeButton(labels, "Open canvas " + entry.Canvas.Name,
                () => ShowProjectCanvas(entry.SpaceId, entry.Canvas.Id, fromLibrary: true));
            open.HorizontalContentAlignment = HorizontalAlignment.Left;
            row.Children.Add(open);
            content.Children.Add(row);
        }
        window.Show();

        void Create() => Run(() => ShowProjectCanvas(null, DockDeskspaces.AddCanvas(null, name.Text).Id));

        void Run(Action action)
        {
            feedback.Text = "";
            try { action(); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { feedback.Text = error.Message; }
        }
    }
}
