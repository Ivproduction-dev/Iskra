using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace IskraStudio.Tests;

public sealed class PaintSkiaTests
{
    private static SKBitmap Bitmap(int width, int height, params int[] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        var span = bitmap.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    span.Slice(y * bitmap.RowBytes + x * 4, 4),
                    pixels[y * width + x]);
            }
        }
        return bitmap;
    }

    private static int Pixel(byte alpha, byte red, byte green, byte blue) =>
        (alpha << 24) | (red << 16) | (green << 8) | blue;

    [Fact]
    public void UnpremultipliedBitmapDrawsAndEncodesWithoutColorLoss()
    {
        var source = Bitmap(2, 2,
            Pixel(255, 255, 0, 0), Pixel(128, 0, 255, 0),
            Pixel(0, 0, 0, 255), Pixel(255, 0, 0, 255));

        using var target = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), null);
            canvas.Flush();
        }

        var drawn = target.GetPixel(1, 0);
        Assert.InRange(drawn.Alpha, 127, 128);
        Assert.InRange(drawn.Green, 254, 255);
        Assert.Equal(0, drawn.Red);

        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data.ToArray());

        Assert.NotNull(decoded);
        var red = decoded.GetPixel(0, 0);
        Assert.Equal(255, red.Alpha);
        Assert.Equal(255, red.Red);
        Assert.Equal(0, red.Green);

        var semi = decoded.GetPixel(1, 0);
        Assert.InRange(semi.Alpha, 127, 128);
        Assert.InRange(semi.Green, 254, 255);
        Assert.Equal(0, semi.Red);
    }

    [Fact]
    public void TransparentPixelsRemainTransparentInPng()
    {
        var source = Bitmap(1, 1, Pixel(0, 255, 0, 0));

        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data.ToArray());

        Assert.NotNull(decoded);
        var pixel = decoded.GetPixel(0, 0);
        Assert.Equal(0, pixel.Alpha);
    }

    [Fact]
    public void TextRendersIntoUnpremultipliedBitmap()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(240, 90, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var font = new SKFont(SKTypeface.Default, 32);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("Искра", 6, 56, SKTextAlign.Left, font, paint);
            canvas.Flush();
        }

        var painted = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    painted++;
                }
            }
        }

        Assert.True(painted > 0);
    }

    [Fact]
    public void BitmapPixelsCanBeCopiedBackFromSkia()
    {
        var pixels = new[] { Pixel(255, 10, 20, 30), Pixel(128, 200, 100, 50) };
        using var bitmap = Bitmap(2, 1, pixels);

        var destination = new int[pixels.Length];
        var span = bitmap.GetPixelSpan();
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = BinaryPrimitives.ReadInt32LittleEndian(
                span.Slice(index * 4, 4));
        }

        Assert.Equal(pixels, destination);
    }

    [Fact]
    public void UnpremultipliedBitmapInfoMatchesPaintEngineLayout()
    {
        var bitmap = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));

        Assert.Equal(4, bitmap.BytesPerPixel);
        Assert.True(bitmap.RowBytes >= 12);
        Assert.Equal(SKColorType.Bgra8888, bitmap.ColorType);
        Assert.Equal(SKAlphaType.Unpremul, bitmap.AlphaType);
        bitmap.Dispose();
    }
}
