using System;
using Avalonia.Input;
using Avalonia.Threading;

namespace FModel.Framework.Ui;

/// <summary>Replacement of WPF's static Clipboard class.</summary>
public static class Clipboard
{
    public static void SetText(string text)
    {
        if (text == null) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var clipboard = AppWindows.ActiveTopLevel?.Clipboard;
            if (clipboard == null) return;
            UiThread.WaitFor(() => clipboard.SetTextAsync(text));
        });
    }

    public static string GetText()
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var clipboard = AppWindows.ActiveTopLevel?.Clipboard;
            return clipboard == null ? string.Empty : UiThread.WaitFor(() => clipboard.GetTextAsync()) ?? string.Empty;
        });
    }

    public static void Clear()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var clipboard = AppWindows.ActiveTopLevel?.Clipboard;
            if (clipboard != null) UiThread.WaitFor(() => clipboard.ClearAsync());
        });
    }

    /// <summary>Puts a PNG image on the clipboard (image/png on Linux, "PNG" on Windows).</summary>
    public static void SetImage(byte[] pngBytes, string fileName = null)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var clipboard = AppWindows.ActiveTopLevel?.Clipboard;
            if (clipboard == null) return;

            var data = new DataObject();
            data.Set("image/png", pngBytes);
            if (OperatingSystem.IsWindows())
                data.Set("PNG", pngBytes);
            if (!string.IsNullOrEmpty(fileName))
                data.Set("text/html", $"<img src=\"{fileName}\"/>");
            UiThread.WaitFor(() => clipboard.SetDataObjectAsync(data));
        });
    }
}
