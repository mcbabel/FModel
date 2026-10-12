using Avalonia;
using Avalonia.VisualTree;

namespace FModel.Extensions;

public static class VisualTreeExtensions
{
    /// <summary>Returns the element itself or its closest visual (or, outside the visual tree, logical) ancestor of type T.</summary>
    public static T FindAncestor<T>(this StyledElement current) where T : class
    {
        while (current != null)
        {
            if (current is T t)
                return t;
            current = current is Visual visual && visual.GetVisualParent() is StyledElement visualParent
                ? visualParent
                : current.Parent;
        }
        return null;
    }

    public static T? FindVisualChild<T>(this Visual parent) where T : class
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is T result)
                return result;

            if (FindVisualChild<T>(child) is { } descendant)
                return descendant;
        }

        return null;
    }
}
