using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WindowsDock;

internal static class DockDeskImages
{
    private const int MaxPixels = 2400;

    internal static string? Choose(Window owner)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose an image", CheckFileExists = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff"
        };
        if (picker.ShowDialog(owner) != true)
            return null;
        using var input = File.OpenRead(picker.FileName);
        if (input.Length > 32 * 1024 * 1024)
            throw new ArgumentException("Choose an image under 32 MB.");
        var info = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var wide = info.PixelWidth >= info.PixelHeight;
        input.Position = 0;
        var source = new BitmapImage();
        source.BeginInit();
        source.StreamSource = input;
        source.CacheOption = BitmapCacheOption.OnLoad;
        if (wide)
            source.DecodePixelWidth = Math.Min(MaxPixels, info.PixelWidth);
        else
            source.DecodePixelHeight = Math.Min(MaxPixels, info.PixelHeight);
        source.EndInit();
        return Keep(source);
    }

    internal static string Keep(BitmapSource source)
    {
        var size = Math.Max(source.PixelWidth, source.PixelHeight);
        if (size > MaxPixels)
        {
            var factor = (double)MaxPixels / size;
            source = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(factor, factor));
        }
        using var output = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(output);
        var bytes = output.ToArray();
        var id = Convert.ToHexString(SHA256.HashData(bytes));
        var name = Asset(id);
        if (!File.Exists(DockData.FilePath(name)))
            DockData.Write(name, path => File.WriteAllBytes(path, bytes));
        return id;
    }

    internal static BitmapSource? Open(string? id, int size = 0)
    {
        if (id == null || id.Length != 64 || id.Any(ch => !Uri.IsHexDigit(ch)))
            return null;
        try
        {
            using var input = File.OpenRead(DockData.FilePath(Asset(id)));
            var source = new BitmapImage();
            source.BeginInit();
            source.StreamSource = input;
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.DecodePixelWidth = size;
            source.EndInit();
            source.Freeze();
            return source;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    private static string Asset(string id) => Path.Combine("desk-images", id + ".png");
}
