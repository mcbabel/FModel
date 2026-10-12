using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Provides the two-way bindable attached property <c>CustomScrollViewer.VerticalOffset</c> for any <see cref="ScrollViewer"/>
/// (used by the TextEditor template to remember the scroll position of each tab).
/// </summary>
public class CustomScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public new static readonly AttachedProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.RegisterAttached<CustomScrollViewer, ScrollViewer, double>("VerticalOffset", double.NaN, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Just a flag that the ScrollChanged handler has been attached.</summary>
    private static readonly AttachedProperty<bool> _verticalScrollBindingProperty =
        AvaloniaProperty.RegisterAttached<CustomScrollViewer, ScrollViewer, bool>("VerticalScrollBinding");

    /// <summary>Set while an offset coming from the binding is waiting to be applied (layout may not be up to date yet).</summary>
    private static readonly AttachedProperty<bool> _pendingProperty =
        AvaloniaProperty.RegisterAttached<CustomScrollViewer, ScrollViewer, bool>("PendingVerticalOffset");

    private static bool _updatingFromScroll;

    static CustomScrollViewer()
    {
        VerticalOffsetProperty.Changed.AddClassHandler<ScrollViewer>(OnVerticalOffsetPropertyChanged);
    }

    public static double GetVerticalOffset(ScrollViewer element) => element.GetValue(VerticalOffsetProperty);
    public static void SetVerticalOffset(ScrollViewer element, double value) => element.SetValue(VerticalOffsetProperty, value);

    private static void OnVerticalOffsetPropertyChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (_updatingFromScroll || e.NewValue is not double v || double.IsNaN(v))
            return;

        BindVerticalOffset(scrollViewer);
        scrollViewer.SetValue(_pendingProperty, true);
        // apply once the new content has been measured
        Dispatcher.UIThread.Post(() =>
        {
            scrollViewer.SetValue(_pendingProperty, false);
            var target = GetVerticalOffset(scrollViewer);
            if (double.IsNaN(target)) return;
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, target);
        }, DispatcherPriority.Background);
    }

    private static void BindVerticalOffset(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(_verticalScrollBindingProperty))
            return;

        scrollViewer.SetValue(_verticalScrollBindingProperty, true);
        scrollViewer.ScrollChanged += (_, se) =>
        {
            if (se.OffsetDelta.Y == 0 || scrollViewer.GetValue(_pendingProperty))
                return;

            _updatingFromScroll = true;
            try
            {
                SetVerticalOffset(scrollViewer, scrollViewer.Offset.Y);
            }
            finally
            {
                _updatingFromScroll = false;
            }
        };
    }
}
