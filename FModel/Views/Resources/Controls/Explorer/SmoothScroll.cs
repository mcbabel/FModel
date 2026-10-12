using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FModel.Extensions;

namespace FModel.Views.Resources.Controls.TiledExplorer;

/// <summary>
/// Attached behavior to reduce mouse-wheel scroll sensitivity for elements containing a ScrollViewer.
/// Attach to the ListBox (or its Style) with IsEnabled="True" and optionally set Factor to control strength.
/// Smaller Factor -> smaller scroll per notch.
/// </summary>
public sealed class SmoothScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, Control, bool>("IsEnabled");

    public static readonly AttachedProperty<double> FactorProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, Control, double>("Factor", 0.25);

    public static void SetIsEnabled(Control obj, bool value) => obj.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Control obj) => obj.GetValue(IsEnabledProperty);
    public static void SetFactor(Control obj, double value) => obj.SetValue(FactorProperty, value);
    public static double GetFactor(Control obj) => obj.GetValue(FactorProperty);

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((element, e) =>
        {
            element.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPreviewMouseWheel);
            if (e.NewValue is true)
                element.AddHandler(InputElement.PointerWheelChangedEvent, OnPreviewMouseWheel, RoutingStrategies.Tunnel);
        });
    }

    private static void OnPreviewMouseWheel(object sender, PointerWheelEventArgs e)
    {
        if (sender is not Control control || e.Delta.Y == 0) return;

        var sv = control as ScrollViewer ?? control.FindVisualChild<ScrollViewer>();
        if (sv == null) return;

        var factor = GetFactor(control);
        if (double.IsNaN(factor) || factor <= 0) factor = 0.25;

        // Avalonia reports 1 per notch (WPF: 120)
        var notches = e.Delta.Y;

        // Base pixels per notch (tweakable); smaller value gives smoother/less jumpy scroll
        const double basePixelsPerNotch = 50.0;
        var adjustedPixels = notches * basePixelsPerNotch * factor;

        var scrollableHeight = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        var scrollableWidth = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);

        // Prefer vertical scrolling when possible
        if (scrollableHeight > 0)
        {
            sv.Offset = new Vector(sv.Offset.X, Math.Clamp(sv.Offset.Y - adjustedPixels, 0, scrollableHeight));
            e.Handled = true;
            return;
        }

        if (scrollableWidth > 0)
        {
            sv.Offset = new Vector(Math.Clamp(sv.Offset.X - adjustedPixels, 0, scrollableWidth), sv.Offset.Y);
            e.Handled = true;
        }
    }
}
