using SkiaSharp;

namespace Iskra.Android;

internal static class PaintColorExtensions
{
    public static string ToHex(this SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}
