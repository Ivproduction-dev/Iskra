using System.Globalization;
using System.Windows.Data;

namespace IskraStudio;

public sealed class InsetWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || parameter is null ||
            !double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var inset))
        {
            return Binding.DoNothing;
        }

        return Math.Max(0, width - inset);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
