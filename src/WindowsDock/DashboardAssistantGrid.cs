using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

    private static void ApplyDashboardResources(Window window)
    {
        var contrast = SystemParameters.HighContrast;
        var surface = contrast ? SystemColors.WindowBrush : assistantInset;
        var focus = contrast ? SystemColors.HighlightBrush : AssistantInk;
        var hover = contrast ? SystemColors.ControlBrush : Brush("#10FFFFFF");
        var selected = contrast ? SystemColors.ControlBrush : Brush("#22FFFFFF");
        window.Resources["DockSurface"] = contrast ? SystemColors.WindowBrush : assistantShell;
        window.Resources["DockStroke"] = AssistantRule;
        foreach (var key in new[] { "ChipSurface", "FieldSurface", "InputSurface", "InputPopup" })
            window.Resources[key] = surface;
        foreach (var key in new[] { "ChipStroke", "FieldStroke", "InputStroke" })
            window.Resources[key] = AssistantRule;
        foreach (var key in new[] { "TextPrimary", "InputText", "InputMark" })
            window.Resources[key] = AssistantInk;
        window.Resources["TextSecondary"] = window.Resources["TextMuted"] = AssistantSecondary;
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
        var (window, content, _) = CreateDockPanel("Assistants", "", 366, 0, AssistantRule,
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
            var face = PersonaPortrait(persona, 64);
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
            var delay = TimeSpan.FromMilliseconds(25 * index);
            tile.Loaded += (_, _) =>
            {
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                {
                    var lift = new TranslateTransform();
                    face.RenderTransform = lift;
                    lift.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(185)) { BeginTime = delay,
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                    face.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(155)) { BeginTime = delay });
                }
            };

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
