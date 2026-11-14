using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly SolidColorBrush assistantShell = Brush("#202B37");
    private static readonly SolidColorBrush assistantBay = Brush("#293746");
    private static readonly SolidColorBrush assistantRule = Brush("#435264");
    private static readonly SolidColorBrush assistantInk = Brush("#F2F6FA");
    private static readonly SolidColorBrush assistantSoftInk = Brush("#B5C4D2");

    internal void ShowAssistantPicker()
    {
        if (agentPanel is { Title: "Assistants", IsVisible: true } picker)
        {
            picker.Activate();
            return;
        }
        var contrast = SystemParameters.HighContrast;
        var ink = contrast ? SystemColors.WindowTextBrush : assistantInk;
        var secondary = contrast ? SystemColors.WindowTextBrush : assistantSoftInk;
        var rule = contrast ? SystemColors.WindowTextBrush : assistantRule;
        var (window, content, _) = CreateDockPanel("Assistants", "", 366, 0, rule, widget: true,
            surface: contrast ? SystemColors.WindowBrush : assistantShell);
        var grid = Columns(1, 1);
        var buttons = new List<Button>();
        var buttonStyle = (Style)FindResource("AssistantPickerButton");
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var tint = contrast ? ink : PersonaTint(persona);
            var body = new DockPanel();
            var key = new Border
            {
                Child = Text((index + 1).ToString(), 11, secondary),
                BorderThickness = new Thickness(1), BorderBrush = rule, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(key, Dock.Right);
            body.Children.Add(key);
            var portrait = PersonaPortrait(persona, 42);
            portrait.Background = Brushes.Transparent;
            portrait.Margin = new Thickness(0, 0, 7, 0);
            body.Children.Add(portrait);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Text(persona.Name, 16, ink, strong: true));
            labels.Children.Add(Text(persona.Role, 11, secondary));
            body.Children.Add(labels);
            var tile = MakeButton(body, persona.Label, () => OpenAssistantConversation(slot));
            tile.Style = buttonStyle;
            tile.Foreground = tint;
            tile.Padding = new Thickness(4, 5, 5, 4);
            tile.ToolTip = persona.Bio;
            AutomationProperties.SetHelpText(tile, persona.Bio);
            AutomationProperties.SetAcceleratorKey(tile, (index + 1).ToString());
            var actionLabel = AssistantQuickLabel(persona);
            var actionIcon = persona.Id switch
            {
                "research" => 0xE721,
                "housekeeping" => 0xE787,
                "coding" => 0xE723,
                _ => 0xEA80
            };
            var action = new DockPanel();
            var glyph = DockGlyph(actionIcon, 12, tint);
            glyph.Margin = new Thickness(0, 0, 7, 0);
            action.Children.Add(glyph);
            action.Children.Add(Text(actionLabel, 12, ink));
            var tools = MakeButton(action, actionLabel, () => OpenAssistantQuickAction(slot));
            tools.Style = buttonStyle;
            tools.Foreground = tint;
            tools.Padding = new Thickness(8, 5, 8, 5);
            tools.MinHeight = 29;
            var cell = new StackPanel();
            cell.Children.Add(tile);
            cell.Children.Add(new Border { Height = 1, Background = rule, Margin = new Thickness(8, 0, 8, 0) });
            cell.Children.Add(tools);
            var compartment = new Border
            {
                Child = cell, Background = contrast ? SystemColors.WindowBrush : assistantBay,
                BorderBrush = rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
                Padding = new Thickness(2)
            };
            Put(grid, compartment, index / 2, index % 2);
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
