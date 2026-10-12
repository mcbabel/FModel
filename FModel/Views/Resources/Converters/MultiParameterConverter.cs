using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

public class MultiParameterConverter : IMultiValueConverter
{
    public static readonly MultiParameterConverter Instance = new();

    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.ToArray();
    }
}
