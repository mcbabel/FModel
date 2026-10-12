using Avalonia;
using Avalonia.Controls;

namespace FModel.Views.Resources.Controls;

public sealed class ListBoxItemBehavior
{
    public static readonly AttachedProperty<bool> IsBroughtIntoViewWhenSelectedProperty =
        AvaloniaProperty.RegisterAttached<ListBoxItemBehavior, ListBoxItem, bool>("IsBroughtIntoViewWhenSelected");

    public static bool GetIsBroughtIntoViewWhenSelected(ListBoxItem listBoxItem) => listBoxItem.GetValue(IsBroughtIntoViewWhenSelectedProperty);
    public static void SetIsBroughtIntoViewWhenSelected(ListBoxItem listBoxItem, bool value) => listBoxItem.SetValue(IsBroughtIntoViewWhenSelectedProperty, value);

    static ListBoxItemBehavior()
    {
        ListBoxItem.IsSelectedProperty.Changed.AddClassHandler<ListBoxItem>((item, e) =>
        {
            if (e.NewValue is true && GetIsBroughtIntoViewWhenSelected(item))
                item.BringIntoView();
        });
    }
}
