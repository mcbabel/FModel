using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FModel.Views.Resources.Controls;

/// <summary>Replacement of WPF MouseBinding (LeftDoubleClick / MiddleClick) as attached properties.</summary>
public sealed class MouseBindings
{
    public static readonly AttachedProperty<ICommand> DoubleClickCommandProperty =
        AvaloniaProperty.RegisterAttached<MouseBindings, Control, ICommand>("DoubleClickCommand");
    public static readonly AttachedProperty<object> DoubleClickCommandParameterProperty =
        AvaloniaProperty.RegisterAttached<MouseBindings, Control, object>("DoubleClickCommandParameter");
    public static readonly AttachedProperty<ICommand> MiddleClickCommandProperty =
        AvaloniaProperty.RegisterAttached<MouseBindings, Control, ICommand>("MiddleClickCommand");
    public static readonly AttachedProperty<object> MiddleClickCommandParameterProperty =
        AvaloniaProperty.RegisterAttached<MouseBindings, Control, object>("MiddleClickCommandParameter");

    public static ICommand GetDoubleClickCommand(Control o) => o.GetValue(DoubleClickCommandProperty);
    public static void SetDoubleClickCommand(Control o, ICommand v) => o.SetValue(DoubleClickCommandProperty, v);
    public static object GetDoubleClickCommandParameter(Control o) => o.GetValue(DoubleClickCommandParameterProperty);
    public static void SetDoubleClickCommandParameter(Control o, object v) => o.SetValue(DoubleClickCommandParameterProperty, v);
    public static ICommand GetMiddleClickCommand(Control o) => o.GetValue(MiddleClickCommandProperty);
    public static void SetMiddleClickCommand(Control o, ICommand v) => o.SetValue(MiddleClickCommandProperty, v);
    public static object GetMiddleClickCommandParameter(Control o) => o.GetValue(MiddleClickCommandParameterProperty);
    public static void SetMiddleClickCommandParameter(Control o, object v) => o.SetValue(MiddleClickCommandParameterProperty, v);

    static MouseBindings()
    {
        DoubleClickCommandProperty.Changed.AddClassHandler<Control>((c, _) =>
        {
            c.RemoveHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
            c.AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
        });
        MiddleClickCommandProperty.Changed.AddClassHandler<Control>((c, _) =>
        {
            c.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            c.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        });
    }

    private static void OnDoubleTapped(object sender, TappedEventArgs e)
    {
        if (sender is not Control c || GetDoubleClickCommand(c) is not { } command) return;
        var parameter = GetDoubleClickCommandParameter(c);
        if (!command.CanExecute(parameter)) return;
        command.Execute(parameter);
        e.Handled = true;
    }

    private static void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle || sender is not Control c || GetMiddleClickCommand(c) is not { } command) return;
        var parameter = GetMiddleClickCommandParameter(c);
        if (!command.CanExecute(parameter)) return;
        command.Execute(parameter);
        e.Handled = true;
    }
}
