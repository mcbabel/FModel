using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace FModel.Framework.Ui;

/// <summary>True when value.ToString() equals one of the '|' separated names in the parameter (DataTrigger replacement for enums).</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public static readonly EnumMatchConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        var str = value.ToString();
        return parameter.ToString()!.Split('|', StringSplitOptions.TrimEntries).Contains(str);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Returns <see cref="TrueValue"/> or <see cref="FalseValue"/> depending on a boolean.</summary>
public sealed class BoolToObjectConverter : IValueConverter
{
    public object TrueValue { get; set; }
    public object FalseValue { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
