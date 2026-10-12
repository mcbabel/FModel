using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

/// <summary>Returns true (for IsVisible) when the value equals the parameter (case insensitive).</summary>
public sealed class ProjectNameToVisibilityConverter : IValueConverter
{
    public static readonly ProjectNameToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
