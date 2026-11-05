using System.Globalization;

namespace IskraStudio;

internal static class ImageEffectSyntax
{
    internal static bool TryParsePercent(string source, out double value) =>
        double.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value is >= 0 and <= 100;
}
