using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static readonly SolidColorBrush assistantShell = Brush("#000000");
    private static readonly SolidColorBrush assistantInset = Brush("#141414");
    private static readonly SolidColorBrush assistantRule = Brush("#272727");
    private static readonly SolidColorBrush assistantInk = Brush("#F5F5F5");
    private static readonly SolidColorBrush assistantSoftInk = Brush("#AEAEAE");
    private static SolidColorBrush AssistantInk => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantInk;
    private static SolidColorBrush AssistantSecondary => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantSoftInk;
    private static SolidColorBrush AssistantRule => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantRule;

    private static void ApplyAssistantResources(Window window)
    {
        var contrast = SystemParameters.HighContrast;
        var surface = contrast ? SystemColors.WindowBrush : assistantInset;
        var focus = contrast ? SystemColors.HighlightBrush : AssistantInk;
        var hover = contrast ? SystemColors.ControlBrush : Brush("#10FFFFFF");
        var selected = contrast ? SystemColors.ControlBrush : Brush("#22FFFFFF");
        foreach (var key in new[] { "ChipSurface", "FieldSurface", "InputSurface", "InputPopup" })
            window.Resources[key] = surface;
        foreach (var key in new[] { "ChipStroke", "FieldStroke", "InputStroke" })
            window.Resources[key] = AssistantRule;
        foreach (var key in new[] { "TextPrimary", "InputText", "InputMark" })
            window.Resources[key] = AssistantInk;
        window.Resources["TextMuted"] = AssistantSecondary;
        window.Resources["ChipHover"] = window.Resources["InputItemHover"] = hover;
        window.Resources["InputItemSelected"] = window.Resources["InputChecked"] = selected;
        window.Resources["ControlFocus"] = window.Resources["InputFocus"] = focus;
    }

    internal void ShowAssistantPicker()
    {
        if (agentPanel is { Title: "Assistants", IsVisible: true } picker)
        {
            picker.Activate();
            return;
        }
        var (window, content, _) = CreateDockPanel("Assistants", "", 366, 0, AssistantRule, assistant: true,
            section: DockSection.Assistants);
        var grid = Columns(1, 1);
        grid.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(1) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var verticalRule = new Border { Background = AssistantRule };
        Grid.SetColumn(verticalRule, 1);
        Grid.SetRowSpan(verticalRule, 3);
        grid.Children.Add(verticalRule);
        var horizontalRule = new Border { Background = AssistantRule };
        Grid.SetRow(horizontalRule, 1);
        Grid.SetColumnSpan(horizontalRule, 3);
        grid.Children.Add(horizontalRule);
        var buttons = new List<Button>();
        var buttonStyle = (Style)FindResource("AssistantPickerButton");
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var keyLabel = (index + 1).ToString();
            var body = new DockPanel();
            var key = Text(keyLabel, 11, AssistantSecondary);
            key.Margin = new Thickness(4, 2, 0, 0);
            key.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(key, Dock.Right);
            body.Children.Add(key);
            var portrait = PersonaPortrait(persona, 42);
            portrait.Background = Brushes.Transparent;
            portrait.Margin = new Thickness(0, 0, 7, 0);
            body.Children.Add(portrait);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Text(persona.Name, 16, AssistantInk, strong: true));
            labels.Children.Add(Text(persona.Role, 12, AssistantSecondary));
            body.Children.Add(labels);
            var tile = MakeButton(body, persona.Label, () => OpenAssistantConversation(slot));
            tile.Style = buttonStyle;
            tile.Foreground = AssistantInk;
            tile.Padding = new Thickness(3, 4, 6, 3);
            tile.ToolTip = persona.Bio;
            AutomationProperties.SetHelpText(tile, persona.Bio);
            AutomationProperties.SetAcceleratorKey(tile, keyLabel);
            var actionLabel = AssistantQuickLabel(persona);
            var actionIcon = persona.Id switch
            {
                "research" => 0xE721,
                "housekeeping" => 0xE787,
                "coding" => 0xE723,
                _ => 0xEA80
            };
            var action = new DockPanel();
            var glyph = DockGlyph(actionIcon, 12, AssistantSecondary);
            glyph.Margin = new Thickness(0, 0, 7, 0);
            action.Children.Add(glyph);
            action.Children.Add(Text(actionLabel, 12, AssistantInk));
            var tools = MakeButton(action, actionLabel, () => OpenAssistantQuickAction(slot));
            tools.Style = buttonStyle;
            tools.Foreground = AssistantInk;
            tools.Padding = new Thickness(10, 4, 8, 4);
            tools.MinHeight = 28;
            var cell = new StackPanel
            {
                Margin = new Thickness(index % 2 == 0 ? 0 : 5, 4, index % 2 == 0 ? 5 : 0, 4)
            };
            cell.Children.Add(tile);
            cell.Children.Add(tools);
            Grid.SetRow(cell, index / 2 * 2);
            Grid.SetColumn(cell, index % 2 * 2);
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

    private static SolidColorBrush PersonaTint(AssistantPersona persona)
    {
        var tint = new SolidColorBrush(persona.Color);
        tint.Freeze();
        return tint;
    }

    private Border PersonaPortrait(AssistantPersona persona, double size)
    {
        var art = new Image
        {
            Source = persona.Face, Width = size * .82, Height = size * .82,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = art
        };
    }
}
