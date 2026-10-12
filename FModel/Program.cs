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
                // GLX/EGL is used when available, software rendering otherwise
                EnableIme = true
            })
            .LogToTrace();
}
