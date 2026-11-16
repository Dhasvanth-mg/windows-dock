using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowsDock;

// One place for the dock's look: a black sheet, warm paper-white ink, neutral controls. Colour is kept for
// things that are actually in a state (a chat that is working, a battery that is low) and for the characters.
public partial class DashboardWindow
{
    private static readonly SolidColorBrush assistantShell = Brush("#000000");
    private static readonly SolidColorBrush assistantInset = Brush("#121110");
    private static readonly SolidColorBrush assistantRule = Brush("#292725");
    private static readonly SolidColorBrush assistantInk = Brush("#F2EEE7");
    private static readonly SolidColorBrush assistantSoftInk = Brush("#A69F95");
    private static readonly SolidColorBrush assistantFaintInk = Brush("#79746C");
    private static readonly SolidColorBrush controlFill = Brush("#201E1C");
    private static readonly SolidColorBrush controlSelected = Brush("#34312E");
    private static readonly SolidColorBrush onInk = Brush("#151412");
    private static readonly SolidColorBrush trackFill = Brush("#24FFFFFF");
    private static readonly SolidColorBrush warn = Brush("#EDB458");
    private static readonly SolidColorBrush good = Brush("#93CFA2");
    private static readonly SolidColorBrush bad = Brush("#F0907F");
    private static readonly FontFamily displayFace = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily textFace = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily smallFace = new("Segoe UI Variable Small, Segoe UI");

    // The one colour that is yours to choose. It marks the main action on a view, switches that are on, the current
    // tab and anything being filled. Every choice is light enough to carry dark text.
    internal sealed record DockAccent(string Id, string Name, string Hex);
    internal static readonly DockAccent[] Accents =
    [
        new("marmalade", "Marmalade", "#F2B04A"),
        new("tomato", "Tomato", "#F27D66"),
        new("pistachio", "Pistachio", "#A6D185"),
        new("pool", "Pool", "#74C6DC"),
        new("grape", "Grape", "#B7A2F2"),
        new("paper", "Paper", "#F2EEE7")
    ];
    private const string AccentFile = "accent.json";
    private static DockAccent chosenAccent = Accents[0];
    private static SolidColorBrush accentBrush = Brush(Accents[0].Hex);
    private static SolidColorBrush AccentFill => SystemParameters.HighContrast ? SystemColors.HighlightBrush : accentBrush;

    internal static DockAccent AccentById(string? id) =>
        Accents.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Accents[0];

    private static void LoadAccent()
    {
        chosenAccent = AccentById(DockData.Read<string>(AccentFile));
        accentBrush = Brush(chosenAccent.Hex);
    }

    private void SetAccent(DockAccent choice)
    {
        if (choice == chosenAccent)
            return;
        chosenAccent = choice;
        accentBrush = Brush(choice.Hex);
        try { DockData.Save(AccentFile, choice.Id); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowOwnedMessage("The colour is on for now, but Windows Dock could not save it.");
        }
        ApplyDashboardResources(this);
        ShowPage(currentPage);
    }

    private FrameworkElement AccentPicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var choice in Accents)
        {
            var current = choice == chosenAccent;
            var swatch = new Grid { Width = 24, Height = 24 };
            swatch.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Stroke = current ? AssistantInk : Brushes.Transparent, StrokeThickness = 1.5
            });
            swatch.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 14, Height = 14, Fill = SystemParameters.HighContrast ? AssistantInk : Brush(choice.Hex)
            });
            var button = MakeButton(swatch, choice.Name + " accent", () => SetAccent(choice));
            button.Style = (Style)FindResource("AssistantPickerButton");
            button.Padding = new Thickness(2);
            button.Margin = new Thickness(1, 0, 0, 0);
            button.ToolTip = choice.Name;
            AutomationProperties.SetItemStatus(button, current ? "Selected" : "");
            row.Children.Add(button);
        }
        return row;
    }

    private static SolidColorBrush AssistantInk => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantInk;
    private static SolidColorBrush AssistantSecondary => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantSoftInk;
    private static SolidColorBrush AssistantFaint => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantFaintInk;
    private static SolidColorBrush AssistantRule => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : assistantRule;
    private static Brush ControlFill => SystemParameters.HighContrast ? SystemColors.WindowBrush : controlFill;

    private static void ApplyDashboardResources(Window window)
    {
        var contrast = SystemParameters.HighContrast;
        var focus = contrast ? SystemColors.HighlightBrush : Brush("#B8F2EEE7");
        var hover = contrast ? SystemColors.ControlBrush : Brush("#12FFFFFF");
        var selected = contrast ? SystemColors.ControlBrush : Brush("#1FFFFFFF");
        window.Resources["DockSurface"] = contrast ? SystemColors.WindowBrush : assistantShell;
        window.Resources["DockStroke"] = AssistantRule;
        window.Resources["ChipSurface"] = window.Resources["InputSurface"] = ControlFill;
        window.Resources["ChipStroke"] = contrast ? SystemColors.WindowTextBrush : controlFill;
        window.Resources["FieldSurface"] = contrast ? SystemColors.WindowBrush : Brush("#0B0A0A");
        window.Resources["InputPopup"] = contrast ? SystemColors.WindowBrush : Brush("#1A1917");
        window.Resources["FieldStroke"] = window.Resources["InputStroke"] = AssistantRule;
        foreach (var key in new[] { "TextPrimary", "InputText", "InputMark" })
            window.Resources[key] = AssistantInk;
        window.Resources["TextSecondary"] = AssistantSecondary;
        window.Resources["TextMuted"] = AssistantFaint;
        window.Resources["ChipHover"] = window.Resources["InputItemHover"] = hover;
        window.Resources["InputItemSelected"] = selected;
        window.Resources["InputChecked"] = window.Resources["Accent"] = AccentFill;
        window.Resources["SegmentSelected"] = contrast ? SystemColors.HighlightBrush : controlSelected;
        window.Resources["SwitchOn"] = AccentFill;
        window.Resources["SwitchThumbOn"] = contrast ? SystemColors.HighlightTextBrush : onInk;
        window.Resources["ControlFocus"] = window.Resources["InputFocus"] = focus;
        if (!contrast)
        {
            window.Resources[SystemParameters.VerticalScrollBarWidthKey] = 6.0;
            window.Resources[SystemParameters.HorizontalScrollBarHeightKey] = 6.0;
        }
    }

    // Small text gets the Small optical size and never drops under 11; headings get Display.
    private static double TypeSize(double size) => size switch
    {
        <= 10 => 11,
        <= 11 => 11.5,
        <= 12 => 12.5,
        _ => size
    };

    private static FontFamily TypeFace(double size) => size >= 17 ? displayFace : size < 12 ? smallFace : textFace;

    // The one filled button on a view: the thing you most likely came here to do.
    private Button PrimaryButton(string label, Action? action = null)
    {
        var button = MakeButton(label, label, action);
        MarkPrimary(button, true);
        return button;
    }

    private void MarkPrimary(Button button, bool primary)
    {
        if (SystemParameters.HighContrast)
            return;
        if (primary)
        {
            button.Background = button.BorderBrush = AccentFill;
            button.Foreground = onInk;
            button.FontWeight = FontWeights.SemiBold;
            button.Resources["ChipHover"] = Brush("#22000000");
        }
        else
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
            button.ClearValue(ForegroundProperty);
            button.ClearValue(FontWeightProperty);
            button.Resources.Remove("ChipHover");
            button.Style = (Style)FindResource("Chip");
        }
    }

    // A text-only button for going somewhere rather than doing something.
    private Button QuietButton(string label, Action? action = null)
    {
        var button = MakeButton(label, label, action);
        button.Style = (Style)FindResource("Quiet");
        ToolTipService.SetIsEnabled(button, false);
        return button;
    }

    private FrameworkElement Segmented(string name, IReadOnlyList<string> labels, int selected, Action<int> choose) =>
        Segmented(name, labels, selected, choose, out _);

    // "mark" lets the caller move the selection when the value changes somewhere else.
    private FrameworkElement Segmented(string name, IReadOnlyList<string> labels, int selected, Action<int> choose,
        out Action<int> mark)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new Button[labels.Count];
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var button = new Button { Content = labels[i], Style = (Style)FindResource("Segment") };
            AutomationProperties.SetName(button, labels[i]);
            button.Click += (_, _) =>
            {
                Mark(index);
                choose(index);
            };
            buttons[i] = button;
            row.Children.Add(button);
        }
        Mark(selected);
        var track = new Border
        {
            Child = row, Background = ControlFill, CornerRadius = new CornerRadius(8), Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(track, name);
        mark = Mark;
        return track;

        void Mark(int current)
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].Tag = i == current ? "Selected" : null;
                AutomationProperties.SetItemStatus(buttons[i], i == current ? "Selected" : "");
            }
        }
    }

    private CheckBox Switch(string name, bool on = false)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    // Label on the left, the control it belongs to on the right, a hairline underneath.
    private Border SettingRow(string label, string? detail, FrameworkElement? trailing, bool last = false) =>
        SettingRow(label, detail, trailing, out _, last);

    private Border SettingRow(string label, string? detail, FrameworkElement? trailing, out TextBlock caption, bool last = false)
    {
        var row = new DockPanel { LastChildFill = true };
        if (trailing != null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(label, 13));
        caption = Text(detail ?? "", 11, AssistantSecondary);
        caption.TextWrapping = TextWrapping.Wrap;
        caption.Margin = new Thickness(0, 1, 0, 0);
        caption.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        labels.Children.Add(caption);
        row.Children.Add(labels);
        return new Border
        {
            Child = row, MinHeight = 44, Padding = new Thickness(0, 7, 0, 7), BorderBrush = AssistantRule,
            BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1)
        };
    }

    private TextBlock SectionTitle(string value)
    {
        var title = Text(value, 12, AssistantSecondary);
        title.Margin = new Thickness(0, 0, 0, 4);
        return title;
    }

    private static Border Rule(double top = 0, double bottom = 0) => new()
    {
        Height = 1, Background = AssistantRule, Margin = new Thickness(0, top, 0, bottom), SnapsToDevicePixels = true
    };

    private static Border Divider(double gap = 20) => new()
    {
        Width = 1, Background = AssistantRule, Margin = new Thickness(gap, 0, gap, 0), SnapsToDevicePixels = true
    };

    // A two-part line such as "39°  Clear": the figure in ink, the words after it quieter.
    private TextBlock Reading(string figure, string words, double size = 13)
    {
        var block = Text("", size);
        block.Inlines.Add(new Run(figure));
        if (words.Length > 0)
            block.Inlines.Add(new Run("  " + words) { Foreground = AssistantSecondary });
        return block;
    }

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}
