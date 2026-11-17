using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WindowsDock;

public partial class DashboardWindow
{
    // There used to be a separate "Assistants" window for choosing a character. The four faces in the dashboard header
    // do that now, so every route that opened the chooser (the tray menu, Ctrl+Alt+A, the Assistant widget, the panel
    // footer) opens the last assistant you talked to instead, one click from the other three.
    internal void ShowAssistantPicker() => ShowAssistantWidget();

    private readonly List<Button> assistantShortcuts = [];

    // The four assistants sit in the dashboard header, so talking to one is a single click from anywhere. The one
    // whose chat is open is underlined, the way a tab is.
    private FrameworkElement AssistantShortcuts()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        assistantShortcuts.Clear();
        for (var index = 0; index < AssistantPersonas.All.Length; index++)
        {
            var slot = index;
            var persona = AssistantPersonas.All[index];
            var button = MakeButton(PersonaPortrait(persona, 26), "Talk to " + persona.Name, () => OpenAssistantConversation(slot));
            button.Style = (Style)FindResource("AssistantPickerButton");
            button.Padding = new Thickness(2);
            button.ToolTip = $"{persona.Name}, {persona.Role.ToLowerInvariant()}";
            AutomationProperties.SetAcceleratorKey(button, $"Alt+{index + 1}");
            assistantShortcuts.Add(button);
            row.Children.Add(button);
        }
        return row;
    }

    private void MarkAssistantShortcut(int slot)
    {
        for (var index = 0; index < assistantShortcuts.Count; index++)
        {
            assistantShortcuts[index].Tag = index == slot ? "Selected" : null;
            AutomationProperties.SetItemStatus(assistantShortcuts[index], index == slot ? "Open" : "");
        }
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
