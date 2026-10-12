using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Replacement of WPF MenuItem.InputGestureText: free text (e.g. a bound FModel Hotkey) shown by the FModel MenuItem template
/// through a HotkeyPresenter. When not set, the MenuItem style fills it from MenuItem.InputGesture.
/// </summary>
public sealed class MenuItemGesture
{
    public static readonly AttachedProperty<string> TextProperty =
        AvaloniaProperty.RegisterAttached<MenuItemGesture, MenuItem, string>("Text");

    public static string GetText(MenuItem o) => o.GetValue(TextProperty);
    public static void SetText(MenuItem o, string value) => o.SetValue(TextProperty, value);

    public static readonly IValueConverter ToStringConverter = new FuncValueConverter<object, string>(o => o?.ToString());
}
