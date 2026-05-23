using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Anvil.Converters;

public class DepthToMarginConverter : IValueConverter
{
    public double Step { get; set; } = 14;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int i ? i : 0;
        return new Thickness(depth * Step, 0, 0, 0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
