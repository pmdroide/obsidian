using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Anvil.Converters;

public class ColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Color c ? new SolidColorBrush(c) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
