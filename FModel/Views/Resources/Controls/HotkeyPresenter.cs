using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace FModel.Views.Resources.Controls;

/// <summary>Displays each part of a gesture ("Ctrl+Shift+F") in its own little box (style class "HotkeyPresenter" in ControlStyles.axaml).</summary>
public class HotkeyPresenter : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    /// <summary>Gesture as a string ("Ctrl+F") or a <see cref="KeyGesture"/> (e.g. bound to MenuItem.InputGesture).</summary>
    public static readonly StyledProperty<object> GestureProperty =
        AvaloniaProperty.Register<HotkeyPresenter, object>(nameof(Gesture));

    public object Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public HotkeyPresenter()
    {
        Classes.Add("HotkeyPresenter");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != GestureProperty) return;

        var gesture = change.NewValue switch
        {
            string s => s,
            KeyGesture g => g.ToString(),
            _ => null
        };
        ItemsSource = string.IsNullOrEmpty(gesture)
            ? null
            : gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
