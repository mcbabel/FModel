using Avalonia.Controls;

namespace FModel.Extensions;

public static class ListBoxExtensions
{
    /// <summary>Scrolls the item into view, realizes its container and returns it.</summary>
    public static ListBoxItem RevealItem(this ListBox list, object item)
    {
        var index = list.Items.IndexOf(item);
        if (index < 0)
            return null;

        list.ApplyTemplate();
        list.UpdateLayout();
        list.ScrollIntoView(index);
        list.UpdateLayout();

        var container = list.ContainerFromIndex(index) as ListBoxItem;
        container?.BringIntoView();
        return container;
    }
}
