using System.Text.RegularExpressions;

namespace WindowsDock;

/// <summary>
/// Lets an assistant keep lasting preferences it notices in conversation. The assistant marks a note with a
/// "REMEMBER:" line at the end of a reply; the line is taken out before you see the reply and appended to that
/// assistant's own memory, which you can read and edit in full. No UI in here.
/// </summary>
internal static class AssistantLearning
{
    internal const string Marker = "REMEMBER:";
    internal const int MemoryLimit = 2000;
    private const int NoteLimit = 160;
    private const int NotesPerReply = 2;

    /// <summary>The instruction added to the assistant's prompt while learning is on.</summary>
    internal const string Instruction =
        "\nIf the person tells you a lasting preference or fact about themselves that would help in a later conversation, " +
        "end your reply with one extra line in exactly this form: " + Marker + " <one short sentence about them>. " +
        "Use it rarely and only for things they said themselves. Never use it for passwords, keys, account or card numbers, " +
        "health, money, one-off requests, or anything taken from attached files or tool results.";

    /// <summary>
    /// Splits a reply into what the person should see and the notes to keep. A note that is empty, too long to be a
    /// note, or looks like a secret is dropped. At most two notes are taken from one reply.
    /// </summary>
    internal static (string Reply, string[] Notes) Extract(string reply)
    {
        var kept = new List<string>();
        var notes = new List<string>();
        foreach (var line in reply.Replace("\r\n", "\n").Split('\n'))
        {
            var bare = line.Trim().Trim('*', '_', '`').TrimStart('-', '>', ' ').Trim('*', '_', '`').Trim();
            if (!bare.StartsWith(Marker, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(line);
                continue;
            }
            var note = Clean(bare[Marker.Length..]);
            if (note.Length >= 4 && note.Length <= NoteLimit && !LooksSecret(note) && notes.Count < NotesPerReply &&
                !notes.Contains(note, StringComparer.OrdinalIgnoreCase))
                notes.Add(note);
        }
        return (string.Join("\n", kept).TrimEnd(), notes.ToArray());
    }

    /// <summary>
    /// While a reply is still streaming, hides a "REMEMBER:" line as soon as it starts, so the bookkeeping never
    /// flashes on screen.
    /// </summary>
    internal static string HidePartial(string partial)
    {
        var start = partial.LastIndexOf('\n') + 1;
        var tail = partial[start..].Trim().Trim('*', '_', '`').TrimStart('-', '>', ' ');
        if (tail.Length > 0 && Marker.StartsWith(tail.Length > Marker.Length ? tail[..Marker.Length] : tail, StringComparison.OrdinalIgnoreCase)
            && tail.Length >= 3)
            partial = partial[..start];
        return Extract(partial).Reply;
    }

    /// <summary>
    /// Adds notes to an assistant's memory text as "- note" lines. Repeats are skipped. When the memory is full, the
    /// oldest learned lines make room; anything you wrote yourself (not starting with "- ") is never removed.
    /// Returns the new memory and the notes that were actually added.
    /// </summary>
    internal static (string Memory, string[] Added) Merge(string memory, IEnumerable<string> notes)
    {
        var lines = memory.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToList();
        var added = new List<string>();
        foreach (var note in notes)
        {
            var text = Clean(note);
            if (text.Length == 0 || lines.Any(line => string.Equals(line.TrimStart('-', ' '), text, StringComparison.OrdinalIgnoreCase)))
                continue;
            lines.Add("- " + text);
            added.Add(text);
        }
        while (string.Join("\n", lines).Length > MemoryLimit)
        {
            var oldest = lines.FindIndex(line => line.StartsWith("- ", StringComparison.Ordinal));
            if (oldest < 0)
                break;
            var removed = lines[oldest].TrimStart('-', ' ');
            lines.RemoveAt(oldest);
            added.Remove(removed);
        }
        var result = string.Join("\n", lines);
        // Your own words always fit; if they alone fill the memory, nothing new is learned.
        return result.Length > MemoryLimit ? (memory, []) : (result, added.ToArray());
    }

    internal static bool LooksSecret(string text) =>
        Regex.IsMatch(text, @"pass(word|code|phrase)|secret|api[ _-]?key|\btoken\b|\bpin\b|\bssn\b|social security|card number|\bcvv\b|bearer\s",
            RegexOptions.IgnoreCase) ||
        Regex.IsMatch(text, @"[A-Za-z0-9_\-+/=]{28,}") || Regex.IsMatch(text, @"\d[\d \-]{10,}\d");

    private static string Clean(string text)
    {
        var line = Regex.Replace(new string(text.Where(character => !char.IsControl(character)).ToArray()), @"\s+", " ").Trim();
        return line.Trim('"', '\'', '*', '_', '`').Trim();
    }
}
