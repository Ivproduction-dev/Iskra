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

    internal bool ContainsOpaquePixel(double x, double y, bool flipX = false, bool flipY = false)
    {
        if (flipX) x = Width - x;
        if (flipY) y = Height - y;
        var pixelX = (int)Math.Floor(x);
        var pixelY = (int)Math.Floor(y);
        return pixelX >= 0 && pixelX < Width && pixelY >= 0 && pixelY < Height && alpha[pixelY * Width + pixelX] > 0;
    }

    public static bool Intersects(BitmapSource first, Rect firstBounds, bool firstFlipX,
        BitmapSource second, Rect secondBounds, bool secondFlipX)
    {
        return Intersects(first, firstBounds, firstBounds.Width, firstBounds.Height, firstFlipX, false, 0d,
            second, secondBounds, secondBounds.Width, secondBounds.Height, secondFlipX, false, 0d);
    }

    public static bool Intersects(BitmapSource first, Rect firstBounds, double firstWidth, double firstHeight,
        bool firstFlipX, bool firstFlipY, double firstDirection,
        BitmapSource second, Rect secondBounds, double secondWidth, double secondHeight,
        bool secondFlipX, bool secondFlipY, double secondDirection)
    {
        var overlap = Rect.Intersect(firstBounds, secondBounds);
        if (overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0) return false;
        var firstMask = cache.GetValue(first, Create);
        var secondMask = cache.GetValue(second, Create);
        var stepX = Math.Min(firstWidth / firstMask.Width, secondWidth / secondMask.Width);
        var stepY = Math.Min(firstHeight / firstMask.Height, secondHeight / secondMask.Height);
        if (!double.IsFinite(stepX) || !double.IsFinite(stepY) || stepX <= 0 || stepY <= 0) return false;

        var firstCenter = new Point(firstBounds.Left + firstBounds.Width / 2d, firstBounds.Top + firstBounds.Height / 2d);
        var secondCenter = new Point(secondBounds.Left + secondBounds.Width / 2d, secondBounds.Top + secondBounds.Height / 2d);
        var firstRadians = firstDirection * Math.PI / 180d;
        var secondRadians = secondDirection * Math.PI / 180d;
        var firstCosine = Math.Cos(firstRadians);
        var firstSine = Math.Sin(firstRadians);
        var secondCosine = Math.Cos(secondRadians);
        var secondSine = Math.Sin(secondRadians);

        var samplesX = Math.Ceiling(overlap.Width / stepX);
        var samplesY = Math.Ceiling(overlap.Height / stepY);
        const double maximumSamples = 250_000d;
        if (samplesX * samplesY > maximumSamples)
        {
            var factor = Math.Sqrt(samplesX * samplesY / maximumSamples);
            stepX *= factor;
            stepY *= factor;
        }

        for (var y = overlap.Top + stepY / 2d; y < overlap.Bottom; y += stepY)
        {
            for (var x = overlap.Left + stepX / 2d; x < overlap.Right; x += stepX)
            {
                if (ContainsOpaqueAt(firstMask, x, y, firstCenter, firstWidth, firstHeight,
                        firstCosine, firstSine, firstFlipX, firstFlipY) &&
                    ContainsOpaqueAt(secondMask, x, y, secondCenter, secondWidth, secondHeight,
                        secondCosine, secondSine, secondFlipX, secondFlipY)) return true;
            }
        }
        return false;
    }

    private static bool ContainsOpaqueAt(SpriteAlphaMask mask, double worldX, double worldY, Point center,
        double width, double height, double cosine, double sine, bool flipX, bool flipY)
    {
        var offsetX = worldX - center.X;
        var offsetY = worldY - center.Y;
        var localX = offsetX * cosine + offsetY * sine + width / 2d;
        var localY = -offsetX * sine + offsetY * cosine + height / 2d;
        if (localX < 0d || localX >= width || localY < 0d || localY >= height) return false;
        return mask.ContainsOpaquePixel(localX * mask.Width / width, localY * mask.Height / height, flipX, flipY);
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
