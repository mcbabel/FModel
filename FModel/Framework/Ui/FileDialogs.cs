using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace FModel.Framework.Ui;

// Synchronous, WPF-like (Microsoft.Win32 / Ookii) file dialogs built on Avalonia's StorageProvider
// (uses the XDG desktop portal / GTK dialogs on Linux).

public abstract class FileDialogBase
{
    public string Title { get; set; }
    public string InitialDirectory { get; set; }
    /// <summary>WPF filter syntax: "Description (*.a)|*.a;*.b|All Files (*.*)|*.*"</summary>
    public string Filter { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DefaultExt { get; set; }

    protected static TopLevel Owner => AppWindows.ActiveTopLevel;

    protected List<FilePickerFileType> ParseFilter()
    {
        var result = new List<FilePickerFileType>();
        if (string.IsNullOrWhiteSpace(Filter)) return result;

        var parts = Filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
        }
        return result;
    }

    protected async Task<IStorageFolder> GetStartLocation(IStorageProvider provider)
    {
        var dir = InitialDirectory;
        if (string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(FileName) && Path.IsPathRooted(FileName))
            dir = Path.GetDirectoryName(FileName);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        try { return await provider.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }
}

public class OpenFileDialog : FileDialogBase
{
    public bool Multiselect { get; set; }
    public string[] FileNames { get; private set; } = [];

    public bool? ShowDialog()
    {
        var owner = Owner;
        if (owner == null) return false;

        var files = UiThread.WaitFor(async () => await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Title,
            AllowMultiple = Multiselect,
            FileTypeFilter = ParseFilter(),
            SuggestedStartLocation = await GetStartLocation(owner.StorageProvider)
        }));

        FileNames = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToArray();
        if (FileNames.Length == 0) return false;
        FileName = FileNames[0];
        return true;
    }
}

public class SaveFileDialog : FileDialogBase
{
    public bool OverwritePrompt { get; set; } = true;

    public bool? ShowDialog()
    {
        var owner = Owner;
        if (owner == null) return false;

        var types = ParseFilter();
        var defaultExt = DefaultExt?.TrimStart('.');
        if (string.IsNullOrEmpty(defaultExt))
            defaultExt = types.FirstOrDefault()?.Patterns?.FirstOrDefault()?.TrimStart('*', '.') is { Length: > 0 } ext && ext != "*" ? ext : Path.GetExtension(FileName)?.TrimStart('.');

        var file = UiThread.WaitFor(async () => await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Title,
            SuggestedFileName = Path.GetFileName(FileName),
            DefaultExtension = defaultExt,
            FileTypeChoices = types,
            ShowOverwritePrompt = OverwritePrompt,
            SuggestedStartLocation = await GetStartLocation(owner.StorageProvider)
        }));

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return false;
        FileName = path;
        return true;
    }
}

/// <summary>Replacement for Ookii's FolderBrowserDialog.</summary>
public class FolderBrowserDialog
{
    public string Description { get; set; }
    public string SelectedPath { get; set; } = string.Empty;
    public bool ShowNewFolderButton { get; set; } = true;
    public bool Multiselect { get; set; }
    public string[] SelectedPaths { get; private set; } = [];

    public bool? ShowDialog()
    {
        var owner = AppWindows.ActiveTopLevel;
        if (owner == null) return false;

        var folders = UiThread.WaitFor(async () =>
        {
            IStorageFolder start = null;
            if (!string.IsNullOrEmpty(SelectedPath) && Directory.Exists(SelectedPath))
            {
                try { start = await owner.StorageProvider.TryGetFolderFromPathAsync(SelectedPath); }
                catch { /* ignored */ }
            }

            return await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Description ?? "Select a folder",
                AllowMultiple = Multiselect,
                SuggestedStartLocation = start
            });
        });

        SelectedPaths = folders.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToArray();
        if (SelectedPaths.Length == 0) return false;
        SelectedPath = SelectedPaths[0];
        return true;
    }
}
