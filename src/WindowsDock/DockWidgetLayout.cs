namespace WindowsDock;

internal sealed record DockWidgetLayout(string Pinned, string[] Favorites)
{
    internal const string FileName = "widget-layout.json";
    internal const int FavoriteLimit = 4;
    private static readonly string[] Ids =
        ["audio", "audio-presets", "meetings", "clipboard", "github", "workspaces", "downloads", "focus", "notifications", "assistant", "activity", "jobs", "calculator", "wifi", "bluetooth", "favorites"];

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

    private static DockWidgetLayout Normalize(DockWidgetLayout? saved)
    {
        var pinned = Ids.Contains(saved?.Pinned) ? saved!.Pinned : "audio";
        var favorites = saved?.Favorites ?? DockData.Read<string[]>("widget-favorites.json")
            ?? ["focus", "assistant", "notifications", "activity"];
        return new(pinned, favorites.Where(id => id != pinned && Ids.Contains(id)).Distinct().Take(FavoriteLimit).ToArray());
    }
}
