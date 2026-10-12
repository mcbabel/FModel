using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using FModel.Framework.Ui;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Converters;

public sealed class FolderTreeIconConverter : IValueConverter
{
    public static readonly FolderTreeIconConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = GetPath(value as string);
        return path == null ? null : _cache.GetOrAdd(path, AppWindows.LoadBitmap);
    }

    private static string GetPath(string name)
    {

        // This should get reworked to be aligned with asset explorer somehow

        return name?.ToLowerInvariant() switch
        {
            "config" => "Resources/gear.png",
            "plugins" => "Resources/puzzle.png",

            "windows" => "Resources/windows.png",
            "android" => "Resources/android.png",
            "ios" or "mac" => "Resources/apple.png",
            "linux" or "linuxaarch64" => "Resources/linux.png",
            "unix" => "Resources/unix.png",
            "platforms" => "Resources/pc.png",

            "weapons" => "Resources/weapon.png",
            "localization" or "internationalization" => "Resources/localization.png",
            "ui" => "Resources/ui.png",
            "sounds" or "audio" => "Resources/sound.png",
            "2dassets" or "textures" => "Resources/texture.png",
            "engine" or "enginecontent" => "Resources/engine.png",
            "materials" => "Resources/materialicon.png",
            "blueprints" => "Resources/blueprint.png",
            "cinematics" or "movies" => "Resources/cinematics.png",

            "fortnite" => "Resources/fortnite.png",
            "fortnitegame" => "Resources/fortnitebr.png",
            "worldexplorers" => "Resources/battlebreakers.png",
            "oakgame" => "Resources/borderlands.png",
            "shootergame" => "Resources/valorant.png",
            "roguecompany" => "Resources/roguecompany.png",
            "g3" => "Resources/spellbreak.png",
            "swgame" => "Resources/fallenorder.png",
            "prospect" => "Resources/thecycle.png",
            "creative" => "Resources/creative.png",
            "athena" => "Resources/athena.png",
            "stateofdecay2" => "Resources/stateofdecay2.png",
            _ => null
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
