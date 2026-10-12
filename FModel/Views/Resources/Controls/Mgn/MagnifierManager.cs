using Avalonia.Controls.Primitives;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Attached property <c>MagnifierManager.Magnifier</c>: while the left button is pressed over the element,
/// a magnifying lens follows the pointer (mouse wheel changes the zoom).
/// </summary>
public class MagnifierManager : AvaloniaObject
{
    private MagnifierAdorner _adorner;
    private Control _element;

    public static readonly AttachedProperty<Magnifier> MagnifierProperty =
        AvaloniaProperty.RegisterAttached<MagnifierManager, Control, Magnifier>("Magnifier");

    static MagnifierManager()
    {
        MagnifierProperty.Changed.AddClassHandler<Control>((target, e) =>
        {
            if (e.NewValue is Magnifier magnifier)
                new MagnifierManager().AttachToMagnifier(target, magnifier);
        });
    }

    public static void SetMagnifier(Control element, Magnifier value) => element.SetValue(MagnifierProperty, value);
    public static Magnifier GetMagnifier(Control element) => element.GetValue(MagnifierProperty);

    private void ElementOnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || GetMagnifier(_element) is { IsFrozen: true })
            return;

        HideAdorner();
    }

    private void ElementOnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_element).Properties.IsLeftButtonPressed)
            return;

        ShowAdorner();
        _adorner.UpdatePosition(e.GetPosition(_element));
    }

    private void ElementOnPointerMoved(object sender, PointerEventArgs e)
    {
        if (_adorner.IsVisible)
            _adorner.UpdatePosition(e.GetPosition(_element));
    }

    private void ElementOnPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        if (GetMagnifier(_element) is not { IsUsingZoomOnMouseWheel: true } magnifier) return;

        switch (e.Delta.Y)
        {
            case < 0:
            {
                var newValue = magnifier.ZoomFactor + magnifier.ZoomFactorOnMouseWheel;
                magnifier.SetCurrentValue(Magnifier.ZoomFactorProperty, newValue);
                break;
            }
            case > 0:
            {
                var newValue = magnifier.ZoomFactor >= magnifier.ZoomFactorOnMouseWheel ? magnifier.ZoomFactor - magnifier.ZoomFactorOnMouseWheel : 0d;
                magnifier.SetCurrentValue(Magnifier.ZoomFactorProperty, newValue);
                break;
            }
        }

        _adorner.UpdateViewBox();
    }

    private void AttachToMagnifier(Control element, Magnifier magnifier)
    {
        _element = element;
        _element.AddHandler(InputElement.PointerPressedEvent, ElementOnPointerPressed, RoutingStrategies.Bubble, true);
        _element.AddHandler(InputElement.PointerReleasedEvent, ElementOnPointerReleased, RoutingStrategies.Bubble, true);
        _element.AddHandler(InputElement.PointerMovedEvent, ElementOnPointerMoved, RoutingStrategies.Bubble, true);
        _element.AddHandler(InputElement.PointerWheelChangedEvent, ElementOnPointerWheelChanged, RoutingStrategies.Bubble, true);

        magnifier.Target = _element;

        _adorner = new MagnifierAdorner(_element, magnifier) { IsVisible = false };
    }

    private void ShowAdorner()
    {
        VerifyAdornerLayer();
        _adorner.IsVisible = true;
    }

    private void VerifyAdornerLayer()
    {
        if (_adorner.Parent != null) return;
        var layer = AdornerLayer.GetAdornerLayer(_element);
        layer?.Children.Add(_adorner);
    }

    private void HideAdorner()
    {
        if (_adorner.IsVisible)
            _adorner.IsVisible = false;
    }
}
