using System;
using System.Diagnostics;
using System.IO;

namespace FModel.Framework.Ui;

/// <summary>Cross-platform helpers to open URLs/files/folders with the desktop environment.</summary>
public static class Shell
{
    /// <summary>Opens a URL, file or folder with the default application (xdg-open on Linux).</summary>
    public static void Open(string target)
    {
        if (string.IsNullOrEmpty(target)) return;
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
    }

    /// <summary>Opens the file manager with <paramref name="path"/> selected (explorer /select on Windows,
    /// org.freedesktop.FileManager1 on Linux, falls back to opening the containing folder).</summary>
    public static void RevealInFileManager(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        path = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows())
        {
            Process.Start("explorer.exe", $"/select, \"{path}\"");
            return;
        }

        if (Directory.Exists(path) && !File.Exists(path))
        {
            Open(path);
            return;
        }

        try
        {
            var uri = new Uri(path).AbsoluteUri;
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dbus-send",
                ArgumentList =
                {
                    "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                    "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems",
                    $"array:string:{uri}", "string:"
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process != null && process.WaitForExit(3000) && process.ExitCode == 0)
                return;
        }
        catch
        {
            // dbus-send not available
        }

        Open(Path.GetDirectoryName(path));
    }
}
