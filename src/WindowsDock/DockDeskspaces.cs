using System.IO;

namespace WindowsDock;

internal sealed record DeskLink(string Title, string Url);
internal sealed record DeskPerson(Guid Id, string Name, string Notes, string Email = "", string Phone = "",
    string Website = "", string? Photo = null);
internal sealed record DeskNode(Guid Id, string Title, string Notes, double X, double Y, Guid? Person = null);
internal sealed record DeskEdge(Guid From, Guid To, string Label);
internal sealed record DeskMap(Guid Id, string Name, DeskNode[] Nodes, DeskEdge[] Edges);
internal sealed record Deskspace(Guid Id, string Name, WorkspaceWindow[] Windows, DeskLink[] Links,
    Guid[] Notes, Guid[] Voice, DeskMap[] Maps, DeskCanvas[]? Canvases = null);
internal sealed record DeskCanvasEntry(DeskCanvas Canvas, Guid? SpaceId, string? SpaceName);

internal static class DockDeskspaces
{
    private const string FileName = "deskspaces.json";
    private sealed record Collection(Deskspace[] Spaces, DeskPerson[] People, DeskCanvas[]? StandaloneCanvases = null);
    private static Collection Load() => DockData.Read<Collection>(FileName) ?? new([], []);
    internal static Deskspace[] Spaces() => Load().Spaces;
    internal static DeskPerson[] People() => Load().People;
    internal static Deskspace Get(Guid id) => Spaces().FirstOrDefault(item => item.Id == id) ??
        throw new IOException("This deskspace has been removed.");

    internal static Deskspace Add(string name)
    {
        var item = new Deskspace(Guid.NewGuid(), Label(name), [], [], [], [], []);
        DockData.Update<Collection>(FileName, saved => (saved ?? new([], [])) with { Spaces = [.. saved?.Spaces ?? [], item] });
        return item;
    }

    internal static void Remove(Guid id) => DockData.Update<Collection>(FileName, saved =>
        (saved ?? new([], [])) with { Spaces = (saved?.Spaces ?? []).Where(item => item.Id != id).ToArray() });

    internal static void Change(Guid id, Func<Deskspace, Deskspace> edit) => DockData.Update<Collection>(FileName, saved =>
    {
        var all = saved ?? new([], []);
        if (!all.Spaces.Any(item => item.Id == id))
            throw new IOException("This deskspace has been removed.");
        return all with { Spaces = all.Spaces.Select(item => item.Id == id ? edit(item) : item).ToArray() };
    });

    internal static DeskLink Link(string label, string url)
    {
        if (url.Length > 2048 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length != 0 || parsed.Host.Length == 0)
            throw new ArgumentException("Enter an HTTPS webpage URL.");
        return new(Label(string.IsNullOrWhiteSpace(label) ? parsed.Host : label), parsed.AbsoluteUri);
    }

    internal static DeskPerson SavePerson(Guid? id, string name, string notes, string email = "", string phone = "",
        string website = "", string? photo = null)
    {
        var person = new DeskPerson(id ?? Guid.NewGuid(), Label(name), Clip(notes, 8000), Clip(email.Trim(), 320),
            Clip(phone.Trim(), 100), string.IsNullOrWhiteSpace(website) ? "" : Link("", website).Url, photo);
        DockData.Update<Collection>(FileName, saved =>
        {
            var all = saved ?? new([], []);
            return all with { People = [.. all.People.Where(item => item.Id != person.Id), person] };
        });
        return person;
    }

    internal static DeskMap AddMap(Guid space, string name)
    {
        var map = new DeskMap(Guid.NewGuid(), Label(name), [], []);
        Change(space, item => item with { Maps = [.. item.Maps, map] });
        return map;
    }

    internal static void ChangeMap(Guid space, Guid map, Func<DeskMap, DeskMap> edit) =>
        Change(space, item =>
        {
            if (!item.Maps.Any(board => board.Id == map))
                throw new IOException("This mind map has been removed.");
            return item with { Maps = item.Maps.Select(board => board.Id == map ? edit(board) : board).ToArray() };
        });

    internal static DeskCanvasEntry[] ListCanvases()
    {
        var state = Load();
        return (state.StandaloneCanvases ?? []).Select(board => new DeskCanvasEntry(board, null, null))
            .Concat(state.Spaces.SelectMany(space => (space.Canvases ?? []).Select(board => new DeskCanvasEntry(board, space.Id, space.Name))))
            .OrderBy(entry => entry.SpaceId.HasValue).ThenBy(entry => entry.Canvas.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static DeskCanvas? FindCanvas(Guid? space, Guid id)
    {
        var state = Load();
        var boards = space is { } parent ? state.Spaces.FirstOrDefault(item => item.Id == parent)?.Canvases : state.StandaloneCanvases;
        return boards?.FirstOrDefault(board => board.Id == id);
    }

    internal static DeskCanvas AddCanvas(Guid? space, string name)
    {
        var board = new DeskCanvas(Guid.NewGuid(), Label(name), []);
        EditCanvases(space, boards => [.. boards, board]);
        return board;
    }

    internal static void ChangeCanvas(Guid? space, Guid id, Func<DeskCanvas, DeskCanvas> edit) =>
        EditCanvases(space, boards =>
        {
            if (!boards.Any(board => board.Id == id))
                throw new IOException("This canvas is no longer in this location.");
            return boards.Select(board => board.Id == id ? edit(board) : board).ToArray();
        });

    internal static void RemoveCanvas(Guid? space, Guid id) =>
        EditCanvases(space, boards => boards.Where(board => board.Id != id).ToArray());

    internal static void MakeCanvasStandalone(Guid space, Guid id) => DockData.Update<Collection>(FileName, saved =>
    {
        var current = saved ?? new([], []);
        var desk = current.Spaces.FirstOrDefault(item => item.Id == space);
        var board = desk?.Canvases?.FirstOrDefault(item => item.Id == id) ??
            throw new IOException("This canvas is no longer in that deskspace.");
        return current with
        {
            StandaloneCanvases = [.. current.StandaloneCanvases ?? [], board],
            Spaces = current.Spaces.Select(item => item.Id == space
                ? item with { Canvases = item.Canvases!.Where(canvas => canvas.Id != id).ToArray() } : item).ToArray()
        };
    });

    private static void EditCanvases(Guid? space, Func<DeskCanvas[], DeskCanvas[]> edit)
    {
        if (space is { } parent)
        {
            Change(parent, item => item with { Canvases = edit(item.Canvases ?? []) });
            return;
        }
        DockData.Update<Collection>(FileName, saved =>
        {
            var current = saved ?? new([], []);
            return current with { StandaloneCanvases = edit(current.StandaloneCanvases ?? []) };
        });
    }

    internal static DeskNode Node(string title, string notes, double x, double y, Guid? person = null)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentException("The position is invalid.");
        return new(Guid.NewGuid(), Label(title), Clip(notes, 8000), x, y, person);
    }

    internal static DeskMap AddEdge(DeskMap map, Guid from, Guid to, string label)
    {
        if (from == to || !map.Nodes.Any(node => node.Id == from) || !map.Nodes.Any(node => node.Id == to))
            throw new ArgumentException("Choose two different nodes.");
        var edges = map.Edges.Where(edge => !((edge.From == from && edge.To == to) || (edge.From == to && edge.To == from)));
        return map with { Edges = [.. edges, new(from, to, Clip(label.Trim(), 60))] };
    }

    internal static DeskMap RemoveNode(DeskMap map, Guid id) => map with
    {
        Nodes = map.Nodes.Where(node => node.Id != id).ToArray(),
        Edges = map.Edges.Where(edge => edge.From != id && edge.To != id).ToArray()
    };

    internal static string Label(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is < 1 or > 100 || trimmed.Any(char.IsControl))
            throw new ArgumentException("Enter a name up to 100 characters.");
        return trimmed;
    }

    internal static string Clip(string text, int limit) => text[..Math.Min(limit, text.Length)];
}
