using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

/// <summary>Returns true (for IsVisible) when the value is an int greater than zero.</summary>
public class IntGreaterThanZeroToVisibilityConverter : IValueConverter
{
    public static readonly IntGreaterThanZeroToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
