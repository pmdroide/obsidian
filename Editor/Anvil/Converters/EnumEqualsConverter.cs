using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Anvil.Converters;

public class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
