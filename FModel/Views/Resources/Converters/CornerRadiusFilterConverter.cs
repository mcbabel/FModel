using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

/// <summary>Keeps only some corners of a CornerRadius (AdonisUI ValuesToCornerRadiusConverter usages).</summary>
public sealed class CornerRadiusFilterConverter(bool topLeft, bool topRight, bool bottomRight, bool bottomLeft) : IValueConverter
{
    public static readonly CornerRadiusFilterConverter NoTopLeft = new(false, true, true, true);
    public static readonly CornerRadiusFilterConverter TopRightOnly = new(false, true, false, false);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var r = value is CornerRadius c ? c : default;
        return new CornerRadius(topLeft ? r.TopLeft : 0, topRight ? r.TopRight : 0, bottomRight ? r.BottomRight : 0, bottomLeft ? r.BottomLeft : 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
