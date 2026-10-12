using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace FModel.Framework.Ui;

/// <summary>WPF-like GroupBox (AdonisUI look, see Themes/Adonis).</summary>
public class GroupBox : HeaderedContentControl
{
    public static readonly StyledProperty<IBrush> HeaderBackgroundProperty =
        AvaloniaProperty.Register<GroupBox, IBrush>(nameof(HeaderBackground));

    public IBrush HeaderBackground
    {
        get => GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }
}

/// <summary>WPF-like StatusBar: items are laid out in a DockPanel (docked left, the last one fills);
/// an item with HorizontalAlignment=Right is docked right by the theme.</summary>
public class StatusBar : ItemsControl
{
    protected override bool NeedsContainerOverride(object item, int index, out object recycleKey)
    {
        if (item is Separator)
        {
            recycleKey = null;
            return false;
        }

        return NeedsContainer<StatusBarItem>(item, out recycleKey);
    }

    protected override Control CreateContainerForItemOverride(object item, int index, object recycleKey) => new StatusBarItem();
}

public class StatusBarItem : ContentControl
{
}
