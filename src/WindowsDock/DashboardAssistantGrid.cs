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
        var (window, content, _) = CreateDockPanel("Assistants", "", 430, 0xEAB7, purple, widget: true);
        var grid = Columns(1, 1);
        var buttons = new List<Button>();
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var tint = PersonaTint(persona);
            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var portrait = PersonaPortrait(persona, 50);
            portrait.Margin = new Thickness(0, 0, 0, 5);
            body.Children.Add(portrait);
            var name = Text(persona.Name, 17, tint, strong: true);
            name.TextAlignment = TextAlignment.Center;
            var role = Text(persona.Role, 11, subdued);
            role.TextAlignment = TextAlignment.Center;
            body.Children.Add(name);
            body.Children.Add(role);
            var tile = MakeButton(body, persona.Label, () => OpenAssistantConversation(slot));
            tile.Padding = new Thickness(8, 9, 8, 10);
            tile.Background = AgentArt.Wash(tint, 25);
            tile.BorderBrush = AgentArt.Wash(tint, 70);
            tile.ToolTip = persona.Bio;
            Put(grid, tile, index / 2, index % 2);
            buttons.Add(tile);
        }
        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Contained);
        content.Children.Add(grid);
        var hint = Text("1–4 to pick · Esc to close", 10, muted);
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
            Source = AssistantPersonas.Face(persona), Width = size * .78, Height = size * .65,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.NearestNeighbor);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 3),
            HorizontalAlignment = HorizontalAlignment.Center, Background = AgentArt.Wash(tint, 25),
            BorderBrush = AgentArt.Wash(tint, 55), BorderThickness = new Thickness(1), Child = art
        };
    }
}
