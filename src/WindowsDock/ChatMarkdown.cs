using System.Text;

namespace WindowsDock;

internal enum MdKind { Paragraph, Heading, Code, Bullet, Number, Quote, Rule }

/// <summary>
/// One block of a reply. Level is the heading depth or the list indent. Label is the code language, or the
/// original marker of a numbered item ("3.").
/// </summary>
internal sealed record MdBlock(MdKind Kind, string Text, int Level = 0, string Label = "");

internal sealed record MdSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

/// <summary>
/// The subset of Markdown that chat models actually write: headings, lists, quotes, fenced code, and inline bold,
/// italic, code and links. Anything else is kept as plain text rather than guessed at. No UI in here.
/// </summary>
internal static class ChatMarkdown
{
    internal static List<MdBlock> Blocks(string text)
    {
        var blocks = new List<MdBlock>();
        var paragraph = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                // An unclosed fence runs to the end of the reply, which is what a cut-off answer looks like.
                for (index++; index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal); index++)
                    code.Add(lines[index]);
                blocks.Add(new(MdKind.Code, string.Join("\n", code), 0, language));
                continue;
            }
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }
            if (trimmed.StartsWith('|') && trimmed.Length > 1)
            {
                // Tables are shown as aligned monospace text rather than rebuilt as a grid.
                Flush();
                var rows = new List<string>();
                for (; index < lines.Length && lines[index].TrimStart().StartsWith('|'); index++)
                    rows.Add(lines[index].Trim());
                index--;
                blocks.Add(new(MdKind.Code, string.Join("\n", rows), 0, "table"));
                continue;
            }
            if (IsRule(trimmed))
            {
                Flush();
                blocks.Add(new(MdKind.Rule, ""));
                continue;
            }
            var hashes = trimmed.TakeWhile(character => character == '#').Count();
            if (hashes is >= 1 and <= 6 && trimmed.Length > hashes && trimmed[hashes] == ' ')
            {
                Flush();
                blocks.Add(new(MdKind.Heading, trimmed[(hashes + 1)..].Trim().TrimEnd('#').TrimEnd(), hashes));
                continue;
            }
            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                Flush();
                var quoted = trimmed.Length > 1 ? trimmed[2..] : "";
                if (blocks.Count > 0 && blocks[^1].Kind == MdKind.Quote)
                    blocks[^1] = blocks[^1] with { Text = blocks[^1].Text + "\n" + quoted };
                else
                    blocks.Add(new(MdKind.Quote, quoted));
                continue;
            }
            if (trimmed.Length > 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
            {
                Flush();
                blocks.Add(new(MdKind.Bullet, trimmed[2..].Trim(), Math.Min(indent / 2, 4)));
                continue;
            }
            var digits = trimmed.TakeWhile(char.IsAsciiDigit).Count();
            if (digits is >= 1 and <= 3 && trimmed.Length > digits + 1 && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
            {
                Flush();
                blocks.Add(new(MdKind.Number, trimmed[(digits + 2)..].Trim(), Math.Min(indent / 2, 4), trimmed[..digits] + "."));
                continue;
            }
            paragraph.Add(line.Trim());
        }
        Flush();
        return blocks;

        void Flush()
        {
            if (paragraph.Count == 0)
                return;
            blocks.Add(new(MdKind.Paragraph, string.Join("\n", paragraph)));
            paragraph.Clear();
        }

        static bool IsRule(string line) =>
            line.Length >= 3 && line[0] is '-' or '*' or '_' && line.All(character => character == line[0] || character == ' ') &&
            line.Count(character => character == line[0]) >= 3;
    }

    internal static List<MdSpan> Spans(string text)
    {
        var spans = new List<MdSpan>();
        Read(text, false, false, spans);
        return spans;
    }

    private static void Read(string text, bool bold, bool italic, List<MdSpan> spans)
    {
        var plain = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '`')
            {
                var end = text.IndexOf('`', index + 1);
                if (end > index + 1)
                {
                    Flush();
                    spans.Add(new(text[(index + 1)..end], bold, italic, Code: true));
                    index = end;
                    continue;
                }
            }
            else if (character is '*' or '_' && index + 1 < text.Length && text[index + 1] == character)
            {
                var marker = new string(character, 2);
                var end = text.IndexOf(marker, index + 2, StringComparison.Ordinal);
                if (end > index + 2)
                {
                    Flush();
                    Read(text[(index + 2)..end], true, italic, spans);
                    index = end + 1;
                    continue;
                }
            }
            else if (character is '*' or '_' && index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1]) &&
                     (character == '*' || index == 0 || !char.IsLetterOrDigit(text[index - 1])))
            {
                // An underscore inside a word (snake_case) is just an underscore.
                var end = text.IndexOf(character, index + 1);
                if (end > index + 1 && !char.IsWhiteSpace(text[end - 1]) &&
                    (character == '*' || end + 1 == text.Length || !char.IsLetterOrDigit(text[end + 1])))
                {
                    Flush();
                    Read(text[(index + 1)..end], bold, true, spans);
                    index = end;
                    continue;
                }
            }
            else if (character == '[')
            {
                var close = text.IndexOf("](", index + 1, StringComparison.Ordinal);
                var end = close < 0 ? -1 : text.IndexOf(')', close + 2);
                if (close > index + 1 && end > close + 2 && WebLink(text[(close + 2)..end].Trim()) is { } target)
                {
                    Flush();
                    spans.Add(new(text[(index + 1)..close], bold, italic, Link: target));
                    index = end;
                    continue;
                }
            }
            else if (character == 'h' && (text.AsSpan(index).StartsWith("https://") || text.AsSpan(index).StartsWith("http://")))
            {
                var end = index;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('<' or '>' or '"'))
                    end++;
                while (end > index && text[end - 1] is '.' or ',' or ')' or ';' or ':' or '!' or '?')
                    end--;
                if (WebLink(text[index..end]) is { } target)
                {
                    Flush();
                    spans.Add(new(text[index..end], bold, italic, Link: target));
                    index = end - 1;
                    continue;
                }
            }
            plain.Append(character);
        }
        Flush();

        void Flush()
        {
            if (plain.Length == 0)
                return;
            spans.Add(new(plain.ToString(), bold, italic));
            plain.Clear();
        }
    }

    /// <summary>Only ordinary web addresses become clickable. A model cannot make a link that runs something local.</summary>
    internal static string? WebLink(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0
            ? uri.AbsoluteUri : null;

    internal static string? NamedFile(string label)
    {
        var marker = label.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;
        var name = label[(marker + 9)..].Trim().Trim('"');
        return DockChats.IsLeaf(name) ? name : null;
    }

    internal static string SuggestedName(string label, int turn, int block)
    {
        if (NamedFile(label) is { } name)
            return name;
        var extension = label.Split(' ', 2)[0].ToLowerInvariant() switch
        {
            "bash" or "sh" => "sh", "cs" or "csharp" => "cs", "css" => "css", "html" => "html",
            "javascript" or "js" => "js", "json" => "json", "markdown" or "md" => "md", "python" or "py" => "py",
            "sql" => "sql", "ts" or "typescript" => "ts", "xml" => "xml", "yaml" or "yml" => "yaml", _ => "txt"
        };
        return $"reply-{turn}-file-{block}.{extension}";
    }
}
