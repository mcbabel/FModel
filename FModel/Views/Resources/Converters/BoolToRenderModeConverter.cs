using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace FModel.Views.Resources.Converters;

public class BoolToRenderModeConverter : IValueConverter
{
    public static readonly BoolToRenderModeConverter Instance = new();

    public BitmapInterpolationMode Convert(bool value) => (BitmapInterpolationMode) Convert(value, null, null, null);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            true => BitmapInterpolationMode.None, // nearest neighbor
            _ => BitmapInterpolationMode.HighQuality
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
