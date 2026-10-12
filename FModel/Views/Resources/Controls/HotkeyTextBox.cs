using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using FModel.Framework;
using FModel.Framework.Ui;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// https://tyrrrz.me/blog/hotkey-editor-control-in-wpf
/// </summary>
public class HotkeyTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public static readonly StyledProperty<Hotkey> HotKeyProperty =
        AvaloniaProperty.Register<HotkeyTextBox, Hotkey>(nameof(HotKey), new Hotkey(Key.None), defaultBindingMode: BindingMode.TwoWay);

    public Hotkey HotKey
    {
        get => GetValue(HotKeyProperty);
        set => SetValue(HotKeyProperty, value);
    }

    public HotkeyTextBox()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        CaretBrush = Brushes.Transparent;
        ContextFlyout = null;
        ContextMenu = null;

        Text = HotKey?.ToString();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HotKeyProperty)
            Text = HotKey?.ToString() ?? string.Empty;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        // Get modifiers and key data
        var modifiers = Keyboard.FromKeyModifiers(e.KeyModifiers);
        var key = e.Key;

        switch (key)
        {
            // If nothing was pressed - return
            case Key.None:
                return;
            // If Delete/Backspace/Escape is pressed without modifiers - clear current value and return
            case Key.Delete or Key.Back or Key.Escape when modifiers == ModifierKeys.None:
                HotKey = new Hotkey(Key.None);
                return;
            // If the only key pressed is one of the modifier keys - return
            case Key.LeftCtrl:
            case Key.RightCtrl:
            case Key.LeftAlt:
            case Key.RightAlt:
            case Key.LeftShift:
            case Key.RightShift:
            case Key.LWin:
            case Key.RWin:
            case Key.Clear:
            case Key.OemClear:
            case Key.Apps:
            // If Enter/Space/Tab is pressed without modifiers - return
            case Key.Enter or Key.Space or Key.Tab when modifiers == ModifierKeys.None:
                return;
            default:
                // Set value
                HotKey = new Hotkey(key, modifiers);
                break;
        }
    }
}
