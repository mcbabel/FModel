using System;
using Avalonia;

namespace FModel;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
    }

    // also used by the visual designer
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                EnableIme = true,
                // the XDG desktop portal file picker doesn't reliably show up for our (synchronous) dialogs on every
                // desktop: use the GTK file chooser (or Avalonia's built-in one when GTK is missing).
                // FMODEL_USE_PORTAL=1 switches back to the portal.
                UseDBusFilePicker = Environment.GetEnvironmentVariable("FMODEL_USE_PORTAL") == "1"
            })
            .LogToTrace();
}
