using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace FModel.Views.Resources.Controls;

/// <summary>Builds the application resource DataTemplate whose key is the (string) item.</summary>
public class OnTagDataTemplateSelector : IDataTemplate
{
    public bool Match(object data) => data is string;

    public Control Build(object param)
        => param is string s ? TypeDataTemplateSelector.Find(s)?.Build(param) : null;
}
