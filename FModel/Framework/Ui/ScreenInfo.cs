using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;

namespace FModel.Framework.Ui;

/// <summary>Replacement for WPF SystemParameters / WinForms Screen.</summary>
public static class ScreenInfo
{
    private static Screen GetPrimary()
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var screens = AppWindows.Main?.Screens ?? AppWindows.Active?.Screens;
            return screens?.Primary ?? (screens?.All.Count > 0 ? screens.All[0] : null);
        });
    }

    /// <summary>Working area of the primary screen in physical pixels.</summary>
    public static PixelRect PrimaryWorkingAreaPixels => GetPrimary()?.WorkingArea ?? new PixelRect(0, 0, 1920, 1040);

    /// <summary>DPI scale factor of the primary screen.</summary>
    public static double PrimaryScaling => GetPrimary()?.Scaling ?? 1.0;

    /// <summary>Working area of the primary screen in device independent pixels (WPF MaximizedPrimaryScreenWidth/Height equivalent).</summary>
    public static Size PrimaryWorkingAreaDips
    {
        get
        {
            var screen = GetPrimary();
            if (screen == null) return new Size(1920, 1040);
            return screen.WorkingArea.Size.ToSize(screen.Scaling);
        }
    }

    /// <summary>Working area (DIPs) of the primary screen as seen from <paramref name="window"/> (usable before it is shown).</summary>
    public static Size PrimaryWorkingAreaDipsOf(Avalonia.Controls.Window window)
    {
        var screen = window.Screens?.Primary ?? (window.Screens?.All.Count > 0 ? window.Screens.All[0] : null);
        if (screen == null) return new Size(1920, 1040);
        return screen.WorkingArea.Size.ToSize(screen.Scaling);
    }
}
