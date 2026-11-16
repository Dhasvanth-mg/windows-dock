using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WindowsDock;

public partial class DashboardWindow
{
    private FrameworkElement FormatChatReply(string text, int turn, Action<string, string> save)
    {
        var body = new StackPanel();
        var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = FontFamily,
            FontSize = 13, Foreground = AssistantInk };
        var reader = new RichTextBox
        {
            Document = document, IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0),
            Padding = new Thickness(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AutomationProperties.SetName(reader, "Assistant reply");
        body.Children.Add(reader);
        var files = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        var codeIndex = 0;
        foreach (var block in ChatMarkdown.Blocks(text))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
            document.Blocks.Add(paragraph);
            if (block.Kind == MdKind.Rule)
            {
                paragraph.BorderBrush = AssistantRule;
                paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                continue;
            }
            if (block.Kind == MdKind.Code)
            {
                paragraph.Background = ControlFill;
                paragraph.Padding = new Thickness(8, 6, 8, 6);
                paragraph.FontFamily = new FontFamily("Consolas");
                paragraph.FontSize = 12;
                paragraph.Inlines.Add(new Run(block.Text));
                if (block.Label != "table")
                {
                    var name = ChatMarkdown.SuggestedName(block.Label, turn, ++codeIndex);
                    var button = ActionButton($"Save {name}", () => save(name, block.Text));
                    button.Margin = new Thickness(0, 0, 4, 4);
                    files.Children.Add(button);
                }
                continue;
            }
            if (block.Kind == MdKind.Heading)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = block.Level <= 2 ? 16 : 14;
                paragraph.Margin = new Thickness(0, 8, 0, 4);
            }
            if (block.Kind == MdKind.Quote)
            {
                paragraph.BorderBrush = AssistantRule;
                paragraph.BorderThickness = new Thickness(2, 0, 0, 0);
                paragraph.Padding = new Thickness(8, 0, 0, 0);
                paragraph.Foreground = AssistantSecondary;
            }
            if (block.Kind is MdKind.Bullet or MdKind.Number)
            {
                paragraph.Margin = new Thickness(block.Level * 12, 0, 0, 3);
                paragraph.Inlines.Add(new Run(block.Kind == MdKind.Bullet ? "•  " : block.Label + "  "));
            }
            foreach (var span in ChatMarkdown.Spans(block.Text))
            {
                var run = new Run(span.Text);
                if (span.Bold)
                    run.FontWeight = FontWeights.SemiBold;
                if (span.Italic)
                    run.FontStyle = FontStyles.Italic;
                if (span.Code)
                {
                    run.FontFamily = new FontFamily("Consolas");
                    run.Background = ControlFill;
                }
                if (span.Link is { } url)
                {
                    var link = new Hyperlink(run) { NavigateUri = new Uri(url), Foreground = AssistantInk };
                    link.RequestNavigate += (_, args) =>
                    {
                        args.Handled = true;
                        OpenTarget(url);
                    };
                    paragraph.Inlines.Add(link);
                }
                else
                    paragraph.Inlines.Add(run);
            }
        }
        if (files.Children.Count > 0)
            body.Children.Add(files);
        return body;
    }
}
