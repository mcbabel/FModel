using Avalonia;
using Avalonia.Controls;

namespace FModel.Views.Resources.Controls;

/// <summary>Control placed in the AdornerLayer over the magnified element, it positions the lens on the pointer.</summary>
public class MagnifierAdorner : Control
{
    private readonly Magnifier _magnifier;
    private Point _currentMousePosition;

    public MagnifierAdorner(Control element, Magnifier magnifier)
    {
        _magnifier = magnifier;
        IsHitTestVisible = false;
        ClipToBounds = false;

        AdornerLayer.SetAdornedElement(this, element);
        AdornerLayer.SetIsClipEnabled(this, false);

        LogicalChildren.Add(_magnifier);
        VisualChildren.Add(_magnifier);
    }

    /// <summary>Pointer position relative to the adorned element.</summary>
    public void UpdatePosition(Point position)
    {
        if (_magnifier.IsFrozen) return;

        _currentMousePosition = position;
        UpdateViewBox();
        InvalidateArrange();
    }

    public void UpdateViewBox()
    {
        // the target is the adorned element: its coordinates are the adorner ones
        var size = _magnifier.ViewBox.Size;
        _magnifier.ViewBox = new Rect(new Point(_currentMousePosition.X - size.Width / 2, _currentMousePosition.Y - size.Height / 2), size);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _magnifier.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = _currentMousePosition.X - _magnifier.Width / 2;
        var y = _currentMousePosition.Y - _magnifier.Height / 2;
        _magnifier.Arrange(new Rect(x, y, _magnifier.Width, _magnifier.Height));
        return finalSize;
    }
}
