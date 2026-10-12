using Avalonia.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace FModel.Framework.Ui;

public static class AppWindows
{
    public static IClassicDesktopStyleApplicationLifetime Desktop =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public static IReadOnlyList<Window> All => Desktop?.Windows ?? (IReadOnlyList<Window>) [];

    public static Window Main => Desktop?.MainWindow;

    /// <summary>The currently active window, or the main window.</summary>
    public static Window Active => All.FirstOrDefault(w => w.IsActive) ?? Main ?? All.FirstOrDefault();

    public static TopLevel ActiveTopLevel => Active;

    /// <summary>Opens an embedded Avalonia resource, path relative to the project root (e.g. "Resources/folder.png").</summary>
    public static Stream OpenResource(string path)
    {
        path = path.TrimStart('/');
        if (path.StartsWith("FModel;component/")) path = path["FModel;component/".Length..];
        return AssetLoader.Open(new System.Uri($"avares://FModel/{path}"));
    }

    public static Bitmap LoadBitmap(string path)
    {
        using var stream = OpenResource(path);
        return new Bitmap(stream);
    }
}
