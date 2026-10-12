using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Replacement of WPF MenuItem.InputGestureText: free text (e.g. a bound FModel Hotkey) shown by the FModel MenuItem template
/// through a HotkeyPresenter. When not set, the MenuItem style fills it from MenuItem.InputGesture.
/// </summary>
public sealed class MenuItemGesture
{
    public static readonly AttachedProperty<object> TextProperty =
        AvaloniaProperty.RegisterAttached<MenuItemGesture, MenuItem, object>("Text");

    public static object GetText(MenuItem o) => o.GetValue(TextProperty);
    public static void SetText(MenuItem o, object value) => o.SetValue(TextProperty, value);

    static MenuItemGesture()
    {
        // the theme's MenuItem template displays MenuItem.InputGesture: translate the FModel hotkey text into a KeyGesture
        TextProperty.Changed.AddClassHandler<MenuItem>((item, e) =>
        {
            var text = e.NewValue?.ToString()?.Replace(" + ", "+").Replace("Win+", "Meta+");
            KeyGesture gesture = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { gesture = KeyGesture.Parse(text); }
                catch { /* not a valid gesture, nothing to display */ }
            }
            item.InputGesture = gesture;
        });
    }

    public static readonly IValueConverter ToStringConverter = new FuncValueConverter<object, string>(o => o?.ToString());
}
