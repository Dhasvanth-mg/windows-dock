using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
        var (window, content, _) = CreateDockPanel("Assistants", "", 366, 0xEAB7, purple, widget: true);
        var grid = Columns(1, 1);
        var buttons = new List<Button>();
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var tint = PersonaTint(persona);
            var body = new DockPanel();
            var portrait = PersonaPortrait(persona, 40);
            portrait.Margin = new Thickness(0, 0, 9, 0);
            body.Children.Add(portrait);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Text(persona.Name, 15, tint, strong: true));
            labels.Children.Add(Text(persona.Role, 11, subdued));
            body.Children.Add(labels);
            var tile = MakeButton(body, persona.Label, () => OpenAssistantConversation(slot));
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            tile.Padding = new Thickness(9, 8, 9, 8);
            tile.ToolTip = persona.Bio;
            System.Windows.Automation.AutomationProperties.SetHelpText(tile, persona.Bio);
            var actionLabel = AssistantQuickLabel(persona);
            var tools = MakeButton(IconLabel(actionLabel, 0xE76C, tint), actionLabel, () => OpenAssistantQuickAction(slot));
            tools.HorizontalContentAlignment = HorizontalAlignment.Left;
            tools.Padding = new Thickness(8, 4, 8, 4);
            tools.Margin = new Thickness(0, 2, 0, 0);
            tools.Background = Brushes.Transparent;
            tools.BorderBrush = Brushes.Transparent;
            var cell = new StackPanel();
            cell.Children.Add(tile);
            cell.Children.Add(tools);
            Put(grid, cell, index / 2, index % 2);
            buttons.Add(tile);
        }
        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Contained);
        content.Children.Add(grid);
        var hint = Text("Keys 1 to 4 · Esc to close", 10, subdued);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(hint);
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

    private static SolidColorBrush PersonaTint(AssistantPersona persona)
    {
        var tint = new SolidColorBrush(persona.Color);
        tint.Freeze();
        return tint;
    }

    private Border PersonaPortrait(AssistantPersona persona, double size)
    {
        var tint = PersonaTint(persona);
        var art = new Image
        {
            Source = persona.Face, Width = size * .82, Height = size * .82,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 3),
            HorizontalAlignment = HorizontalAlignment.Center, Background = AgentArt.Wash(tint, 36),
            Child = art
        };
    }
}
