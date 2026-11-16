namespace WindowsDock;

/// <summary>A sticky note or a longer memo. Stickies have a paper colour and one of them can be pinned to Today.</summary>
internal sealed record DockNote(Guid Id, string Kind, string Text, string Color, bool Pinned, DateTimeOffset Updated);

/// <summary>Notes kept in one local file. Every change goes through the data store's lock, so two panels cannot clobber each other.</summary>
internal static class DockNotes
{
    internal const string Sticky = "sticky";
    internal const string Memo = "memo";
    internal const int Limit = 80;
    internal const int TextLimit = 20000;
    private const string FileName = "notes.json";

    internal static readonly (string Id, string Name, string Hex)[] Papers =
    [
        ("butter", "Butter", "#F3DF8A"),
        ("peach", "Peach", "#F5BD9E"),
        ("mint", "Mint", "#B8E2B2"),
        ("sky", "Sky", "#ADD6F1"),
        ("lilac", "Lilac", "#D2C0F4")
    ];

    internal static List<DockNote> Load() => DockData.Read<List<DockNote>>(FileName) ?? [];

    internal static string PaperHex(string? id) =>
        Papers.FirstOrDefault(paper => paper.Id == id) is { Hex: { } hex } ? hex : Papers[0].Hex;

    internal static string NextPaper(string? id)
    {
        var index = Array.FindIndex(Papers, paper => paper.Id == id);
        return Papers[(index + 1) % Papers.Length].Id;
    }

    /// <summary>The first line, shortened: what a memo is called in the list.</summary>
    internal static string Title(DockNote note)
    {
        var line = note.Text.Split('\n').Select(part => part.Trim()).FirstOrDefault(part => part.Length > 0) ?? "";
        return line.Length == 0 ? "Empty note" : line.Length > 60 ? line[..60].TrimEnd() + "…" : line;
    }

    /// <summary>The sticky shown on Today: the pinned one, or failing that the one touched most recently.</summary>
    internal static DockNote? ForToday(IEnumerable<DockNote> notes)
    {
        var stickies = notes.Where(note => note.Kind == Sticky).ToArray();
        return stickies.FirstOrDefault(note => note.Pinned) ?? stickies.OrderByDescending(note => note.Updated).FirstOrDefault();
    }

    internal static DockNote Add(string kind, string text = "", string? color = null)
    {
        if (kind is not (Sticky or Memo))
            throw new ArgumentException("A note is either a sticky or a memo.");
        var note = new DockNote(Guid.NewGuid(), kind, Clip(text), color ?? Papers[0].Id, false, DateTimeOffset.UtcNow);
        DockData.Update<List<DockNote>>(FileName, saved =>
        {
            var notes = saved ?? [];
            if (notes.Count >= Limit)
                throw new InvalidOperationException($"That is {Limit} notes. Delete one to make room.");
            if (kind == Sticky && notes.Count(item => item.Kind == Sticky) == 0)
                note = note with { Pinned = true };
            notes.Add(note);
            return notes;
        });
        return note;
    }

    /// <summary>Rewrites one note. Returning null from <paramref name="change"/> deletes it. A missing note is left alone.</summary>
    internal static void Change(Guid id, Func<DockNote, DockNote?> change) =>
        DockData.Update<List<DockNote>>(FileName, saved =>
        {
            var notes = saved ?? [];
            var index = notes.FindIndex(note => note.Id == id);
            if (index < 0)
                return notes;
            var next = change(notes[index]);
            if (next == null)
            {
                notes.RemoveAt(index);
                return notes;
            }
            next = next with { Id = id, Kind = notes[index].Kind, Text = Clip(next.Text) };
            // Only one sticky is pinned to Today at a time.
            if (next.Pinned && !notes[index].Pinned)
                for (var other = 0; other < notes.Count; other++)
                    notes[other] = notes[other] with { Pinned = false };
            notes[index] = next;
            return notes;
        });

    private static string Clip(string text) => text.Length > TextLimit ? text[..TextLimit] : text;
}
