using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace WindowsDock;

// The pieces that make the assistant chat read like a chat site rather than a settings form: a round send button,
// a quiet row of actions under each message, and a speech bubble for what you said.
public partial class DashboardWindow
{
    private static ControlTemplate? roundTemplate;

    // A circle with the icon in the middle. Dims when it cannot be pressed, brightens a touch on hover.
    private static ControlTemplate RoundTemplate => roundTemplate ??= (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
            <Grid>
                <Ellipse x:Name="Disc" Fill="{TemplateBinding Background}"/>
                <Ellipse x:Name="Ring" Stroke="{TemplateBinding BorderBrush}" StrokeThickness="0"/>
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Grid>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Disc" Property="Opacity" Value="0.88"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Disc" Property="Opacity" Value="0.72"/>
                </Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Ring" Property="StrokeThickness" Value="2"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter TargetName="Disc" Property="Opacity" Value="0.3"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private Button RoundIconButton(int icon, string name, double size = 30)
    {
        var glyph = DockGlyph(icon, size * .44, SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : onInk);
        glyph.FontWeight = FontWeights.Bold;
        var button = new Button
        {
            Content = glyph, Template = RoundTemplate, Width = size, Height = size, Background = AccentFill,
            BorderBrush = (Brush)FindResource("ControlFocus"), FocusVisualStyle = null, ToolTip = name
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    // Small, faint icon buttons that sit under a message, the way copy and retry sit under a reply on the chat sites.
    private Button MessageAction(int icon, string name, Action? action = null)
    {
        var button = IconButton(icon, name, action);
        button.Width = 26;
        button.Height = 24;
        button.Margin = new Thickness(0, 0, 1, 0);
        if (button.Content is TextBlock glyph)
        {
            glyph.FontSize = 12;
            glyph.Foreground = AssistantFaint;
        }
        return button;
    }

    // Copies text and briefly turns the copy icon into a tick, so the click visibly did something.
    private void CopyWithTick(Button button, string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        if (button.Content is not TextBlock glyph)
            return;
        var icon = glyph.Text;
        glyph.Text = char.ConvertFromUtf32(0xE73E);
        glyph.Foreground = AssistantInk;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            glyph.Text = icon;
            glyph.Foreground = AssistantFaint;
        };
        timer.Start();
    }

    // What you said, in a soft bubble on the right.
    private static Border UserBubble(UIElement content) => new()
    {
        Child = content, Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : controlFill,
        BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Transparent,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18, 18, 4, 18),
        Padding = new Thickness(14, 9, 14, 10), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 470
    };
}
