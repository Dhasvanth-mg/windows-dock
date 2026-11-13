using System.IO;
using System.Text.Json;

namespace WindowsDock;

internal sealed record Pin(string Name, string Target);
internal sealed record LayoutItem(string Section, string Label, string Target);
internal sealed record DockSettings
{
    public int Version { get; init; } = 1;
    public int IconSize { get; init; } = 32;
    public int Opacity { get; init; } = 85;
    public int Radius { get; init; } = 21;
    public int Elevation { get; init; } = 10;
    public bool Clock { get; init; } = true;
    public bool AutoHide { get; init; }
    public int HideDelay { get; init; } = 700;
    public int EdgeDepth { get; init; } = 4;
    public int Spacing { get; init; } = 2;
    public string Tint { get; init; } = "Graphite";
    public bool Use24Hour { get; init; }
    public bool SystemLinks { get; init; } = true;
    public string[] Widgets { get; init; } = [];
    public LayoutItem[] Layout { get; init; } = [];
    public Pin[] Pins { get; init; } = [new("File Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))];

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsDock", "dock.json");

    public static DockSettings Load(bool safeMode)
    {
        if (safeMode)
            return new();
        foreach (var file in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                var config = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(file));
                if (config is { Version: 1, IconSize: >= 20 and <= 64, Opacity: >= 25 and <= 100,
                    Radius: >= 0 and <= 36, Elevation: >= 0 and <= 80, Pins.Length: <= 24, HideDelay: >= 200 and <= 3000, EdgeDepth: >= 2 and <= 24,
                    Spacing: >= 0 and <= 10, Tint: "Graphite" or "Ocean" or "Violet", Widgets.Length: <= 6, Layout.Length: <= 24 } &&
                    config.Layout.All(item => item is { Section: "Before pins" or "After pins" or "Before widgets" or "Before clock", Label: { Length: <= 60 }, Target: { Length: <= 2048 } } &&
                        (item.Target.Length == 0 || IsShortcut(item.Target))) &&
                    config.Pins.All(pin => pin is not null && !string.IsNullOrWhiteSpace(pin.Name) &&
                        !string.IsNullOrWhiteSpace(pin.Target) && Path.IsPathFullyQualified(pin.Target)))
                    return config;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        return new();
    }

    public static bool IsShortcut(string target) =>
        (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Host.Length > 0) ||
        (Path.IsPathFullyQualified(target) && !target.StartsWith(@"\\", StringComparison.Ordinal) &&
            Path.GetExtension(target).ToLowerInvariant() is ".exe" or ".lnk");

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath))
            File.Replace(temporary, FilePath, FilePath + ".bak");
        else
            File.Move(temporary, FilePath);
    }
}
