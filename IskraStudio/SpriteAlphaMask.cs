using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IskraStudio;

internal sealed class SpriteAlphaMask
{
    private static readonly ConditionalWeakTable<BitmapSource, SpriteAlphaMask> cache = new();
    private readonly byte[] alpha;

    private SpriteAlphaMask(int width, int height, byte[] alpha)
    {
        Width = width;
        Height = height;
        this.alpha = alpha;
    }

    public int Width { get; }
    public int Height { get; }

    public static bool IsOpaqueAt(BitmapSource source, Point point, double renderedWidth, double renderedHeight,
        bool uniformToFill = false)
    {
        return cache.GetValue(source, Create).ContainsOpaquePixel(point, renderedWidth, renderedHeight, uniformToFill);
    }

    internal bool ContainsOpaquePixel(Point point, double renderedWidth, double renderedHeight,
        bool uniformToFill = false)
    {
        if (renderedWidth <= 0 || renderedHeight <= 0) return false;

        double pixelX;
        double pixelY;
        if (uniformToFill)
        {
            var scale = Math.Max(renderedWidth / Width, renderedHeight / Height);
            var offsetX = (renderedWidth - Width * scale) / 2;
            var offsetY = (renderedHeight - Height * scale) / 2;
            pixelX = (point.X - offsetX) / scale;
            pixelY = (point.Y - offsetY) / scale;
        }
        else
        {
            pixelX = point.X * Width / renderedWidth;
            pixelY = point.Y * Height / renderedHeight;
        }

        var x = (int)Math.Floor(pixelX);
        var y = (int)Math.Floor(pixelY);
        return x >= 0 && x < Width && y >= 0 && y < Height && alpha[y * Width + x] > 0;
    }

    internal static SpriteAlphaMask FromBgra32(int width, int height, byte[] pixels)
    {
        var alpha = new byte[width * height];
        for (var index = 0; index < alpha.Length; index++)
        {
            alpha[index] = pixels[index * 4 + 3];
        }
        return new SpriteAlphaMask(width, height, alpha);
    }

    private static SpriteAlphaMask Create(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return FromBgra32(converted.PixelWidth, converted.PixelHeight, pixels);
    }
}
