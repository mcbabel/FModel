using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        var enumType = value.GetType();
        if (!enumType.IsEnum)
            return false;

        var target = parameter is string text
            ? Enum.Parse(enumType, text, ignoreCase: true)
            : parameter;

        return value.Equals(target);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
            return BindingOperations.DoNothing;

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (parameter is string text && enumType is { IsEnum: true })
            return Enum.Parse(enumType, text, ignoreCase: true);
        return parameter;
    }
}
