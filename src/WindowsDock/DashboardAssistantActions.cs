using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace WindowsDock;

public partial class DashboardWindow
{
    private static string AssistantQuickLabel(AssistantPersona persona) => persona.Id switch
    {
        "research" => "Search the web",
        "housekeeping" => "Preview schedule",
        "coding" => "Attach logs",
        _ => "Idea jar"
    };

    private void OpenAssistantQuickAction(int slot)
    {
        assistantRole = slot;
        var persona = AssistantPersonas.All[slot];
        var chat = assistantConversations[slot];
        switch (persona.Id)
        {
            case "research":
            case "housekeeping":
                ShowAssistantConnections(persona, quick: true);
                break;
            case "coding":
                ShowAssistantLogs(persona, chat);
                break;
            case "casual":
                ShowAssistantIdeaJar(persona, chat);
                break;
        }
    }

    private void ShowAssistantLogs(AssistantPersona persona, AssistantConversation chat)
    {
        const string source = "Debug logs";
        var tint = AssistantInk;
        var (window, content, _) = CreateDockPanel("Attach logs", persona.Name, 460, 0xE8C8, tint,
            backAction: ShowAssistantWidget, backLabel: $"Back to {persona.Name}");
        var note = Text("Keep the relevant lines and remove secrets before attaching.", 11, AssistantSecondary);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 0, 0, 7);
        content.Children.Add(note);

        var logs = Field("Logs or error text", 0);
        logs.AcceptsReturn = true;
        logs.TextWrapping = TextWrapping.Wrap;
        logs.Height = 134;
        logs.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        logs.Tag = "Paste or type the error and surrounding lines";
        if (chat.Context?.Source == source)
            logs.Text = chat.Context.Text;
        content.Children.Add(logs);

        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 6, 0, 6);
        content.Children.Add(status);
        var attach = AccentButton(chat.Context == null ? "Attach to draft" : "Replace attached context", 0xE723, tint, () =>
        {
            var text = logs.Text.Trim();
            if (text.Length is 0 or > AssistantContext.TextLimit)
                return;
            chat.Context = new AssistantContext(source, text);
            if (string.IsNullOrWhiteSpace(chat.Draft))
                chat.Draft = "Help me diagnose the attached logs. What should I check first?";
            ShowAssistantWidget();
        });
        var actions = new WrapPanel();
        var paste = ActionButton("Paste logs", () =>
        {
            try
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    status.Text = "There’s no text on the clipboard.";
                    return;
                }
                logs.SelectedText = text;
                logs.Focus();
            }
            catch (ExternalException) { status.Text = "Clipboard is busy. Try again."; }
        });
        paste.Margin = new Thickness(0, 0, 5, 0);
        actions.Children.Add(paste);
        actions.Children.Add(attach);
        content.Children.Add(actions);

        logs.TextChanged += (_, _) => Update();
        window.Loaded += (_, _) => logs.Focus();
        Update();
        window.Show();

        void Update()
        {
            var length = logs.Text.Trim().Length;
            attach.IsEnabled = length is > 0 and <= AssistantContext.TextLimit;
            status.Text = length > AssistantContext.TextLimit
                ? $"Trim the excerpt to {AssistantContext.TextLimit:N0} characters to attach it."
                : $"{length:N0} / {AssistantContext.TextLimit:N0} characters";
        }
    }

    private void ShowAssistantIdeaJar(AssistantPersona persona, AssistantConversation chat)
    {
        var (window, content, _) = CreateDockPanel("Idea jar", persona.Name, 366, 0xEA80, AssistantInk,
            backAction: ShowAssistantWidget, backLabel: $"Back to {persona.Name}");
        var note = Text("Pick a conversation starter to add to your draft.", 11, AssistantSecondary);
        note.Margin = new Thickness(0, 0, 0, 7);
        content.Children.Add(note);
        var status = Text("", 11, AssistantSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        foreach (var (label, prompt) in new[]
        {
            ("Find a rabbit hole", "Give me one oddly specific thing worth reading about. Tell me what makes it interesting."),
            ("Stress-test an idea", "I’m going to share an idea. Find its most interesting assumption and give me one way to test it."),
            ("Do a tiny creative exercise", "Give me a five-minute creative exercise with one weird constraint.")
        })
        {
            var button = ActionButton(label, () =>
            {
                var draft = string.IsNullOrWhiteSpace(chat.Draft) ? prompt : $"{chat.Draft.TrimEnd()}\n\n{prompt}";
                if (draft.Length > AssistantMessageLimit)
                {
                    status.Text = "There isn’t enough room in this draft. Shorten it and try again.";
                    return;
                }
                chat.Draft = draft;
                ShowAssistantWidget();
            });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 5);
            content.Children.Add(button);
        }
        content.Children.Add(status);
        window.Show();
    }
}
