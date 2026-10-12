using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FModel.Settings;

namespace FModel.Views.Resources.Converters;

/// <summary>Returns true (for IsVisible) when the endpoint of the given type (ConverterParameter) is valid.</summary>
public class EndpointToTypeConverter : IValueConverter
{
    public static readonly EndpointToTypeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not EEndpointType type) throw new NotImplementedException();
        return UserSettings.IsEndpointValid(type, out _);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
