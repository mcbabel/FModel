using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using FModel.Framework.Ui;

namespace FModel.Views.Resources.Controls.Explorer;

/// <summary>Icon of a package in the packages list (WPF DataTriggers on Asset.Extension).</summary>
public sealed class AssetExtensionIconConverter : IValueConverter
{
    public static readonly AssetExtensionIconConverter Instance = new();
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = (value as string) switch
        {
            "uasset" => "Resources/asset.png",
            "ini" => "Resources/asset_ini.png",
            "png" => "Resources/asset_png.png",
            "psd" => "Resources/asset_psd.png",
            _ => "Resources/unknown_asset.png"
        };
        return _cache.GetOrAdd(path, AppWindows.LoadBitmap);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
