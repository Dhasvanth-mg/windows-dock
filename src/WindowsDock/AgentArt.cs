using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsDock;

internal static class AgentArt
{
    private const int Width = 16;
    private const int Height = 12;
    private const int Scale = 3;
    private static readonly Lazy<Dictionary<AgentState, Sprite>> Sprites = new(Load);

    internal static SolidColorBrush Tint(AgentState state) => Sprites.Value[state].Tint;
    internal static ImageSource Face(AgentState state) => Sprites.Value[state].Face;

    internal static SolidColorBrush Wash(SolidColorBrush tint, byte opacity)
    {
        var color = tint.Color;
        var brush = new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Dictionary<AgentState, Sprite> Load()
    {
        using var stream = typeof(AgentArt).Assembly.GetManifestResourceStream("WindowsDock.AgentFaces.txt")!;
        using var reader = new StreamReader(stream);
        var sprites = new Dictionary<AgentState, Sprite>();
        foreach (var state in Enum.GetValues<AgentState>())
        {
            var header = reader.ReadLine()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header is not [var name, var hex] || name != state.ToString() ||
                !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var tint))
                throw new InvalidDataException("Invalid agent sprite header.");
            var pixels = new uint[Width * Height * Scale * Scale];
            for (var y = 0; y < Height; y++)
            {
                if (reader.ReadLine() is not { Length: Width } row)
                    throw new InvalidDataException("Invalid agent sprite row.");
                for (var x = 0; x < Width; x++)
                {
                    var color = row[x] switch
                    {
                        '#' => 0xFF000000 | tint,
                        'o' => 0xFF201C2E,
                        '+' => 0xFFFFF1D9,
                        _ => 0u
                    };
                    for (var line = 0; line < Scale; line++)
                        Array.Fill(pixels, color, (y * Scale + line) * Width * Scale + x * Scale, Scale);
                }
            }
            var face = BitmapSource.Create(Width * Scale, Height * Scale, 96, 96, PixelFormats.Pbgra32, null,
                pixels, Width * Scale * sizeof(uint));
            face.Freeze();
            var brush = new SolidColorBrush(Color.FromRgb((byte)(tint >> 16), (byte)(tint >> 8), (byte)tint));
            brush.Freeze();
            sprites.Add(state, new(brush, face));
            _ = reader.ReadLine();
        }
        return sprites;
    }

    private sealed record Sprite(SolidColorBrush Tint, ImageSource Face);
}
