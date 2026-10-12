using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls;

/// <summary>Picks <see cref="FolderTemplate"/> for <see cref="TreeItem"/> and <see cref="FileTemplate"/> for <see cref="GameFileViewModel"/>
/// (or the application resources named by FolderTemplateKey / FileTemplateKey).</summary>
public sealed class TypeDataTemplateSelector : IDataTemplate
{
    public IDataTemplate FolderTemplate { get; set; }
    public IDataTemplate FileTemplate { get; set; }
    public object FolderTemplateKey { get; set; }
    public object FileTemplateKey { get; set; }

    public bool Match(object data) => data is TreeItem or GameFileViewModel;

    public Control Build(object param)
    {
        var template = param switch
        {
            TreeItem => FolderTemplate ?? Find(FolderTemplateKey),
            GameFileViewModel => FileTemplate ?? Find(FileTemplateKey),
            _ => null
        };
        return template?.Build(param);
    }

    internal static IDataTemplate Find(object key)
    {
        var app = Application.Current;
        if (key == null || app == null) return null;
        return app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value as IDataTemplate : null;
    }
}
