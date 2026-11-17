using System.IO;
using System.Text.Json;

namespace WindowsDock;

internal sealed record DockWidgetLayout(string Pinned, string[] Favorites)
{
    internal const string FileName = "widget-layout.json";
    internal const int FavoriteLimit = 4;
    private static readonly string[] Ids =
        ["audio", "audio-presets", "meetings", "clipboard", "github", "workspaces", "downloads", "focus", "notifications", "assistant", "activity", "jobs", "calculator", "wifi", "bluetooth", "favorites", "voice", "file-search", "deskspace"];
    private sealed record Profile(int Version, DockWidgetLayout Layout);

    internal static string WidgetId(string id) => Array.Find(Ids, item => item.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Choose a widget from the Widgets tab.");

    internal static DockWidgetLayout Load() => Normalize(DockData.Read<DockWidgetLayout>(FileName));

    internal static DockWidgetLayout Pin(string id) => DockData.Update<DockWidgetLayout>(FileName, saved =>
    {
        var layout = Normalize(saved);
        return new(id, layout.Favorites.Select(item => item == id ? layout.Pinned : item).ToArray());
    });

    internal static DockWidgetLayout SetFavorite(string id, bool enabled) => DockData.Update<DockWidgetLayout>(FileName, saved =>
    {
        var layout = Normalize(saved);
        if (!enabled)
            return layout with { Favorites = layout.Favorites.Where(item => item != id).ToArray() };
        if (id == layout.Pinned || layout.Favorites.Length == FavoriteLimit || layout.Favorites.Contains(id))
            return layout;
        return layout with { Favorites = [.. layout.Favorites, id] };
    });

    internal static DockWidgetLayout MoveFavorite(string id, int direction) => DockData.Update<DockWidgetLayout>(FileName, saved =>
    {
        var layout = Normalize(saved);
        var index = Array.IndexOf(layout.Favorites, id);
        var next = index + Math.Sign(direction);
        if (index >= 0 && next >= 0 && next < layout.Favorites.Length)
            (layout.Favorites[index], layout.Favorites[next]) = (layout.Favorites[next], layout.Favorites[index]);
        return layout;
    });

    internal static string Export() => JsonSerializer.Serialize(new Profile(1, Load()), new JsonSerializerOptions { WriteIndented = true });

    internal static DockWidgetLayout Import(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 16 * 1024)
            throw new ArgumentException("Choose a Windows Dock widget layout file.");
        var profile = JsonSerializer.Deserialize<Profile>(file);
        if (profile is not { Version: 1, Layout: { Favorites: not null } layout } ||
            !Ids.Contains(layout.Pinned) || layout.Favorites.Length > FavoriteLimit ||
            layout.Favorites.Any(id => !Ids.Contains(id) || id == layout.Pinned) ||
            layout.Favorites.Distinct().Count() != layout.Favorites.Length)
            throw new ArgumentException("Choose a supported Windows Dock widget layout file.");
        return DockData.Update<DockWidgetLayout>(FileName, _ => layout);
    }

    private static DockWidgetLayout Normalize(DockWidgetLayout? saved)
    {
        var pinned = Ids.Contains(saved?.Pinned) ? saved!.Pinned : "audio";
        var favorites = saved?.Favorites ?? DockData.Read<string[]>("widget-favorites.json")
            ?? ["focus", "assistant", "notifications", "activity"];
        return new(pinned, favorites.Where(id => id != pinned && Ids.Contains(id)).Distinct().Take(FavoriteLimit).ToArray());
    }
}
