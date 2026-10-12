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

/// <summary>WPF-like StatusBar: items are docked left, the last one fills (use HorizontalAlignment=Right on it).</summary>
public class StatusBar : ItemsControl
{
}

public class StatusBarItem : ContentControl
{
}
