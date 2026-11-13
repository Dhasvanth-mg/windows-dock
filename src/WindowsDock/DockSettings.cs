using System.IO;
using System.Text.Json;

namespace WindowsDock;

internal sealed record Pin(string Name, string Target);
internal sealed record DockSettings
{
    public int Version { get; init; } = 1;
    public int IconSize { get; init; } = 32;
    public int Opacity { get; init; } = 85;
    public int Radius { get; init; } = 21;
    public int Elevation { get; init; } = 10;
    public bool Clock { get; init; } = true;
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
                    Radius: >= 0 and <= 36, Elevation: >= 0 and <= 80, Pins.Length: <= 24 } &&
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
