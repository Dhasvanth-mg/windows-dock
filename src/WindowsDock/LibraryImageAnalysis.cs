using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WindowsDock;

internal sealed record LibraryAnalysis(string Text, string Tags, string Summary);

internal static class LibraryImageAnalysis
{
    private const int SampleSize = 224;

    internal static async Task<LibraryAnalysis> ReadAsync(byte[] bytes, CancellationToken token)
    {
        try
        {
            using var stream = await ImageStreamAsync(bytes, token).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
            var tags = new List<string>
            {
                "image",
                decoder.OrientedPixelWidth == decoder.OrientedPixelHeight ? "square" :
                    decoder.OrientedPixelWidth > decoder.OrientedPixelHeight ? "landscape" : "portrait",
                $"{decoder.OrientedPixelWidth} × {decoder.OrientedPixelHeight}"
            };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform { ScaledWidth = SampleSize, ScaledHeight = SampleSize },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)
                .AsTask(token).ConfigureAwait(false);
            var sample = pixels.DetachPixelData();
            string[] detected;
            try
            {
                tags.AddRange(Colors(sample));
                token.ThrowIfCancellationRequested();
                detected = LibraryImageClassifier.Predict(sample);
            }
            finally { CryptographicOperations.ZeroMemory(sample); }
            tags.AddRange(detected);
            var (text, ocrAvailable) = await RecognizeAsync(decoder, token).ConfigureAwait(false);
            var summary = ocrAvailable
                ? (text.Length == 0 ? "Image tags indexed. No text found." : "Image tags and recognized text indexed.")
                : "Image tags indexed. Windows text recognition was unavailable.";
            return new(IndexedDocumentText.Crop(text, IndexedDocumentText.TextLimit), string.Join(", ", tags), summary);
        }
        catch (Exception error) when (error is COMException or IOException or InvalidOperationException or ArgumentException or
            NotSupportedException or UnauthorizedAccessException)
        { return new("", "image", "The image is saved. It could not be decoded for analysis."); }
    }

    internal static async Task<System.Windows.Media.Imaging.BitmapSource?> PreviewAsync(byte[] bytes, CancellationToken token)
    {
        try
        {
            using var stream = await ImageStreamAsync(bytes, token).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
            var largest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            if (largest == 0)
                return null;
            var scale = Math.Min(1d, 640d / largest);
            var width = Math.Max(1, (uint)(decoder.PixelWidth * scale));
            var height = Math.Max(1, (uint)(decoder.PixelHeight * scale));
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = width, ScaledHeight = height },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token).ConfigureAwait(false);
            var pixels = data.DetachPixelData();
            try
            {
                var thumbnail = System.Windows.Media.Imaging.BitmapSource.Create((int)width, (int)height, 96, 96,
                    PixelFormats.Pbgra32, null, pixels, (int)width * 4);
                thumbnail.Freeze();
                return thumbnail;
            }
            finally { CryptographicOperations.ZeroMemory(pixels); }
        }
        catch (Exception error) when (error is COMException or IOException or ArgumentException or
            InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        { return null; }
    }

    private static async Task<InMemoryRandomAccessStream> ImageStreamAsync(byte[] bytes, CancellationToken token)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
                writer.DetachStream();
            }
            stream.Seek(0);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static async Task<(string Text, bool Available)> RecognizeAsync(BitmapDecoder decoder, CancellationToken token)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null && OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is { } language)
                engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine == null)
                return ("", false);
            var largest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            if (largest == 0)
                return ("", false);
            var scale = Math.Min(1d, Math.Min(2200, OcrEngine.MaxImageDimension) / (double)largest);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)),
                    ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)
                .AsTask(token).ConfigureAwait(false);
            var result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
            return (string.Join("\n", result.Lines.Select(line => line.Text)), true);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException or
            NotSupportedException or UnauthorizedAccessException)
        { return ("", false); }
    }

    private static string[] Colors(ReadOnlySpan<byte> pixels)
    {
        string[] labels = ["black", "gray", "white", "red", "orange", "yellow", "green", "cyan", "blue", "purple", "pink"];
        var counts = new int[labels.Length];
        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] < 96)
                continue;
            var (red, green, blue) = ((int)pixels[offset + 2], (int)pixels[offset + 1], (int)pixels[offset]);
            var high = Math.Max(red, Math.Max(green, blue));
            var low = Math.Min(red, Math.Min(green, blue));
            var spread = high - low;
            if (high < 39) { counts[0]++; continue; }
            if (spread < 29) { counts[high >= 219 ? 2 : 1]++; continue; }
            var hue = high == red ? 60d * (green - blue) / spread :
                high == green ? 120 + 60d * (blue - red) / spread : 240 + 60d * (red - green) / spread;
            counts[((hue + 360) % 360) switch
            {
                < 16 or >= 348 => 3, < 45 => 4, < 70 => 5, < 160 => 6, < 208 => 7, < 258 => 8, < 304 => 9, _ => 10
            }]++;
        }
        var total = counts.Sum();
        return labels.Where((_, index) => counts[index] > 0 && counts[index] >= total * .16).Take(4).ToArray();
    }
}
