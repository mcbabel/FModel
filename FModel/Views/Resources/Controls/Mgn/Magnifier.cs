using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace FModel.Views.Resources.Controls;

/// <summary>Magnifying lens (see MagnifierManager). Templates "CircleTemplate"/"RectangleTemplate" in ControlStyles.axaml fill a shape with <see cref="LensBrush"/>.</summary>
public class Magnifier : TemplatedControl
{
    private const double DEFAULT_SIZE = 100d;

    public static readonly StyledProperty<EFrameType> FrameTypeProperty =
        AvaloniaProperty.Register<Magnifier, EFrameType>(nameof(FrameType), EFrameType.Circle);
    public EFrameType FrameType
    {
        get => GetValue(FrameTypeProperty);
        set => SetValue(FrameTypeProperty, value);
    }

    public static readonly StyledProperty<bool> IsUsingZoomOnMouseWheelProperty =
        AvaloniaProperty.Register<Magnifier, bool>(nameof(IsUsingZoomOnMouseWheel), true);
    public bool IsUsingZoomOnMouseWheel
    {
        get => GetValue(IsUsingZoomOnMouseWheelProperty);
        set => SetValue(IsUsingZoomOnMouseWheelProperty, value);
    }

    public bool IsFrozen { get; private set; }

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<Magnifier, double>(nameof(Radius), DEFAULT_SIZE / 2);
    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public static readonly StyledProperty<Visual> TargetProperty =
        AvaloniaProperty.Register<Magnifier, Visual>(nameof(Target));
    public Visual Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<Magnifier, double>(nameof(ZoomFactor), 0.5, validate: v => v >= 0);
    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public static readonly StyledProperty<double> ZoomFactorOnMouseWheelProperty =
        AvaloniaProperty.Register<Magnifier, double>(nameof(ZoomFactorOnMouseWheel), 0.1d, validate: v => v >= 0);
    public double ZoomFactorOnMouseWheel
    {
        get => GetValue(ZoomFactorOnMouseWheelProperty);
        set => SetValue(ZoomFactorOnMouseWheelProperty, value);
    }

    public static readonly DirectProperty<Magnifier, IBrush> LensBrushProperty =
        AvaloniaProperty.RegisterDirect<Magnifier, IBrush>(nameof(LensBrush), o => o.LensBrush);
    private VisualBrush _lensBrush;
    /// <summary>Brush showing <see cref="ViewBox"/> of <see cref="Target"/> (WPF PART_VisualBrush).</summary>
    public IBrush LensBrush
    {
        get => _lensBrush;
        private set
        {
            var old = _lensBrush;
            _lensBrush = (VisualBrush) value;
            RaisePropertyChanged(LensBrushProperty, old, _lensBrush);
        }
    }

    private Rect _viewBox;
    /// <summary>Area of <see cref="Target"/> (in its own coordinates) displayed by the lens.</summary>
    public Rect ViewBox
    {
        get => _viewBox;
        set
        {
            if (_viewBox == value) return;
            _viewBox = value;
            UpdateBrush();
        }
    }

    static Magnifier()
    {
        WidthProperty.OverrideDefaultValue<Magnifier>(DEFAULT_SIZE);
        HeightProperty.OverrideDefaultValue<Magnifier>(DEFAULT_SIZE);
    }

    public Magnifier()
    {
        UpdateSizeFromRadius();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FrameTypeProperty || change.Property == RadiusProperty)
            UpdateSizeFromRadius();
        else if (change.Property == ZoomFactorProperty || change.Property == BoundsProperty ||
                 change.Property == WidthProperty || change.Property == HeightProperty)
            UpdateViewBox();
        else if (change.Property == TargetProperty)
            UpdateBrush();
    }

    private void UpdateSizeFromRadius()
    {
        if (FrameType != EFrameType.Circle) return;

        var newSize = Radius * 2;
        if (!Helper.AreVirtuallyEqual(Width, newSize))
            Width = newSize;
        if (!Helper.AreVirtuallyEqual(Height, newSize))
            Height = newSize;
    }

    public void Freeze(bool freeze)
    {
        IsFrozen = freeze;
    }

    private void UpdateViewBox()
    {
        var width = double.IsNaN(Width) ? Bounds.Width : Width;
        var height = double.IsNaN(Height) ? Bounds.Height : Height;
        ViewBox = new Rect(ViewBox.Position, new Size(width * ZoomFactor, height * ZoomFactor));
    }

    private void UpdateBrush()
    {
        // a new brush instance so the lens is re-rendered for each new viewbox
        LensBrush = Target == null ? null : new VisualBrush
        {
            Visual = Target,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            SourceRect = new RelativeRect(_viewBox, RelativeUnit.Absolute)
        };
    }
}
