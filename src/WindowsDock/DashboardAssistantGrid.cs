using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WindowsDock;

public partial class DashboardWindow
{
    internal void ShowAssistantPicker()
    {
        if (agentPanel is { Title: "Assistants", IsVisible: true } picker)
        {
            picker.Activate();
            return;
        }
        var (window, content, _) = CreateDockPanel("Assistants", "", 400, 0, AssistantRule,
            section: DockSection.Assistants);
        window.MinHeight = Math.Min(302, window.MaxHeight);
        var grid = Columns(1, 1, 1, 1);
        grid.Margin = new Thickness(0, 5, 0, 0);
        var buttons = new List<Button>();
        var buttonStyle = (Style)FindResource("AssistantPickerButton");
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var keyLabel = (index + 1).ToString();
            var face = PersonaPortrait(persona, 64, lively: true);
            var labels = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
            labels.Children.Add(face);
            var name = Text(persona.Name, 15, AssistantInk, strong: true);
            var role = Text(persona.Role, 11, AssistantSecondary);
            name.HorizontalAlignment = role.HorizontalAlignment = HorizontalAlignment.Center;
            labels.Children.Add(name);
            labels.Children.Add(role);

            var body = new Grid();
            body.Children.Add(labels);
            var key = Text(keyLabel, 11, AssistantSecondary);
            key.HorizontalAlignment = HorizontalAlignment.Right;
            key.VerticalAlignment = VerticalAlignment.Top;
            body.Children.Add(key);
            var tile = MakeButton(body, persona.Label, () => OpenAssistantConversation(slot));
            tile.Style = buttonStyle;
            tile.Foreground = AssistantInk;
            tile.Padding = new Thickness(3, 6, 4, 9);
            tile.ToolTip = persona.Bio;
            AutomationProperties.SetHelpText(tile, persona.Bio);
            AutomationProperties.SetAcceleratorKey(tile, keyLabel);

            var caption = persona.Id switch
            {
                "research" => "Web search",
                "housekeeping" => "Schedule",
                "coding" => "Logs",
                _ => "Idea jar"
            };
            var tools = MakeButton(caption, AssistantQuickLabel(persona), () => OpenAssistantQuickAction(slot));
            tools.Style = buttonStyle;
            tools.Foreground = AssistantSecondary;
            tools.FontSize = 11;
            tools.Padding = new Thickness(2, 4, 2, 4);
            tools.HorizontalContentAlignment = HorizontalAlignment.Center;
            tools.MinHeight = 31;
            var cell = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            cell.Children.Add(tile);
            cell.Children.Add(new Border { Height = 1, Background = AssistantRule, Margin = new Thickness(5, 4, 5, 0) });
            cell.Children.Add(tools);
            Grid.SetColumn(cell, index);
            grid.Children.Add(cell);
            buttons.Add(tile);
        }
        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Contained);
        content.Children.Add(grid);
        window.Loaded += (_, _) => buttons[assistantRole].Focus();
        window.PreviewKeyDown += (_, args) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
                return;
            var slot = args.Key switch
            {
                >= Key.D1 and <= Key.D4 => args.Key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad4 => args.Key - Key.NumPad1,
                _ => -1
            };
            if (slot < 0)
                return;
            args.Handled = true;
            OpenAssistantConversation(slot);
        };
        window.Show();
    }

    // The four assistants sit in the dashboard header, so talking to one is a single click from anywhere.
    private FrameworkElement AssistantShortcuts()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var button = MakeButton(PersonaPortrait(persona, 26), "Talk to " + persona.Name, () => OpenAssistantConversation(slot));
            button.Style = (Style)FindResource("AssistantPickerButton");
            button.Padding = new Thickness(2);
            button.ToolTip = $"{persona.Name}, {persona.Role.ToLowerInvariant()}";
            row.Children.Add(button);
        }
        return row;
    }

    private static SolidColorBrush PersonaTint(AssistantPersona persona)
    {
        var tint = new SolidColorBrush(persona.Color);
        tint.Freeze();
        return tint;
    }

    // Lively figures sway while idle. The small ones in the header only blink and hop, so an open dashboard is not
    // animating all the time.
    private static AssistantFigure PersonaPortrait(AssistantPersona persona, double size, bool lively = false) =>
        new(persona, size, lively);
}
