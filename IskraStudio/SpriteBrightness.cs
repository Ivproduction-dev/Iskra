using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IskraStudio;

internal static class SpriteBrightness
{
    private static readonly ConditionalWeakTable<BitmapSource, ConcurrentDictionary<double, BitmapSource>> Cache = new();

    public static BitmapSource Apply(BitmapSource source, double brightness)
    {
        if (brightness >= 100d)
        {
            return source;
        }

        return Cache.GetValue(source, _ => new ConcurrentDictionary<double, BitmapSource>())
            .GetOrAdd(brightness, value => CreateAdjustedBitmap(source, value));
    }

    private static BitmapSource CreateAdjustedBitmap(BitmapSource source, double brightness)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var multiplier = brightness / 100d;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)Math.Round(pixels[index] * multiplier);
            pixels[index + 1] = (byte)Math.Round(pixels[index + 1] * multiplier);
            pixels[index + 2] = (byte)Math.Round(pixels[index + 2] * multiplier);
        }

        var adjusted = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight,
            converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        adjusted.Freeze();
        return adjusted;
    }
}
