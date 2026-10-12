using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

/// <summary>DirectoryFilesListBox: multiple selection for the Multiple/All/AllButNew/AllButModified loading modes.</summary>
public sealed class LoadingModeToSelectionModeConverter : IValueConverter
{
    public static readonly LoadingModeToSelectionModeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ELoadingMode.Multiple or ELoadingMode.All or ELoadingMode.AllButNew or ELoadingMode.AllButModified
            ? SelectionMode.Multiple
            : SelectionMode.Single;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
