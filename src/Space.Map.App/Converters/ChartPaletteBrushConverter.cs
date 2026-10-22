using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Space.Map.App.Controls;

namespace Space.Map.App.Converters;

public sealed class ChartPaletteBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var index = value is int colorIndex ? colorIndex : 0;
        return ChartPalette.GetBrush(index);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
