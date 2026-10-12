using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace FModel.Views.Resources.Controls.Aup;

[TemplatePart("PART_ControlContainer", typeof(Grid))]
[TemplatePart("PART_TimelineArea", typeof(Border))]
[TemplatePart("PART_MousePosition", typeof(Rectangle))]
[TemplatePart("PART_Timeline", typeof(Grid))]
[TemplatePart("PART_Length", typeof(Grid))]
[TemplatePart("PART_ProgressLine", typeof(Border))]
public sealed class Timeline : TemplatedControl
{
    public static readonly StyledProperty<ISource> SourceProperty =
        AvaloniaProperty.Register<Timeline, ISource>(nameof(Source));

    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<Timeline, TimeSpan>(nameof(Position), TimeSpan.Zero);

    public static readonly StyledProperty<IBrush> TickBrushProperty =
        AvaloniaProperty.Register<Timeline, IBrush>(nameof(TickBrush), Brushes.Red);

    public static readonly StyledProperty<IBrush> TimeBrushProperty =
        AvaloniaProperty.Register<Timeline, IBrush>(nameof(TimeBrush), Brushes.Blue);

    public static readonly StyledProperty<IBrush> ProgressLineBrushProperty =
        AvaloniaProperty.Register<Timeline, IBrush>(nameof(ProgressLineBrush), Brushes.Violet);

    public static readonly StyledProperty<IBrush> ProgressBrushProperty =
        AvaloniaProperty.Register<Timeline, IBrush>(nameof(ProgressBrush), Brushes.DarkGreen);

    public static readonly StyledProperty<IBrush> MousePositionBrushProperty =
        AvaloniaProperty.Register<Timeline, IBrush>(nameof(MousePositionBrush), Brushes.Brown);

    public ISource Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public TimeSpan Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public IBrush TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }
    public IBrush TimeBrush { get => GetValue(TimeBrushProperty); set => SetValue(TimeBrushProperty, value); }
    public IBrush ProgressLineBrush { get => GetValue(ProgressLineBrushProperty); set => SetValue(ProgressLineBrushProperty, value); }
    public IBrush ProgressBrush { get => GetValue(ProgressBrushProperty); set => SetValue(ProgressBrushProperty, value); }
    public IBrush MousePositionBrush { get => GetValue(MousePositionBrushProperty); set => SetValue(MousePositionBrushProperty, value); }

    private ISource _source;
    private Grid _controlContainer;
    private Border _timelineArea;
    private Rectangle _position;
    private Grid _timelineGrid;
    private Grid _lengthGrid;
    private Border _progressLine;
    private bool _hasPosition;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
        {
            if (_source != null)
            {
                _source.SourceEvent -= OnSourceEvent;
                _source.SourcePropertyChangedEvent -= OnSourcePropertyChangedEvent;
            }

            _source = Source;
            if (_source == null) return;
            _source.SourceEvent += OnSourceEvent;
            _source.SourcePropertyChangedEvent += OnSourcePropertyChangedEvent;
            OnSourceEvent(this, null);
        }
        else if (change.Property == TickBrushProperty || change.Property == TimeBrushProperty)
        {
            UpdateTimeline();
        }
    }

    private void OnSourceEvent(object sender, SourceEventArgs e)
    {
        if (e is not { Event: ESourceEventType.Loading } || Source == null) return;
        Dispatcher.UIThread.Post(UpdateTimeline);
    }

    private void OnSourcePropertyChangedEvent(object sender, SourcePropertyChangedEventArgs e)
    {
        if (e.Property != ESourceProperty.Position) return;

        var position = (TimeSpan) e.Value;
        Dispatcher.UIThread.Post(() =>
        {
            Position = position;
            _hasPosition = true;
            UpdateProgress();
        });
    }

    private void UpdateProgress()
    {
        if (_lengthGrid == null || _progressLine == null) return;

        var width = _lengthGrid.Bounds.Width;
        if (!_hasPosition)
        {
            // WPF template bound the progress width to the control width until the first position update
            _progressLine.Width = Bounds.Width;
            return;
        }

        var x = 0d;
        var duration = _source?.PlayedFile.Duration ?? TimeSpan.Zero;
        if (duration.TotalMilliseconds != 0)
        {
            x = Position.TotalMilliseconds / duration.TotalMilliseconds * width;
        }

        _progressLine.Width = Math.Max(0, x);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_lengthGrid != null)
            _lengthGrid.SizeChanged -= OnLengthSizeChanged;
        if (_timelineArea != null)
        {
            _timelineArea.PointerEntered -= OnAreaPointerEntered;
            _timelineArea.PointerExited -= OnAreaPointerExited;
            _timelineArea.PointerMoved -= OnAreaPointerMoved;
            _timelineArea.PointerPressed -= OnAreaPointerPressed;
        }

        _controlContainer = e.NameScope.Find<Grid>("PART_ControlContainer");
        _timelineGrid = e.NameScope.Find<Grid>("PART_Timeline");
        _lengthGrid = e.NameScope.Find<Grid>("PART_Length");
        _progressLine = e.NameScope.Find<Border>("PART_ProgressLine");
        _timelineArea = e.NameScope.Find<Border>("PART_TimelineArea");
        _position = e.NameScope.Find<Rectangle>("PART_MousePosition");

        if (_lengthGrid != null)
            _lengthGrid.SizeChanged += OnLengthSizeChanged;
        if (_timelineArea != null)
        {
            _timelineArea.PointerEntered += OnAreaPointerEntered;
            _timelineArea.PointerExited += OnAreaPointerExited;
            _timelineArea.PointerMoved += OnAreaPointerMoved;
            _timelineArea.PointerPressed += OnAreaPointerPressed;
        }

        OnSourceEvent(this, null);
    }

    private void OnLengthSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTimeline();
        UpdateProgress();
    }

    private void OnAreaPointerEntered(object sender, PointerEventArgs e)
    {
        if (_position != null) _position.IsVisible = true;
    }

    private void OnAreaPointerExited(object sender, PointerEventArgs e)
    {
        if (_position != null) _position.IsVisible = false;
    }

    private void OnAreaPointerMoved(object sender, PointerEventArgs e)
    {
        if (_position == null) return;
        _position.Margin = new Thickness(e.GetPosition(_timelineArea).X, 0, _position.Bounds.Width, 0);
    }

    private void OnAreaPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_timelineArea).Properties.IsLeftButtonPressed || _timelineArea.Bounds.Width <= 0) return;
        Source?.SkipTo(e.GetPosition(_timelineArea).X / _timelineArea.Bounds.Width);
    }

    private void UpdateTimeline()
    {
        if (_source == null || _source.PlayedFile.Duration == TimeSpan.Zero || _lengthGrid == null ||
            _lengthGrid.Bounds.Width < 1 || _lengthGrid.Bounds.Height < 1) return;

        _lengthGrid.Children.Clear();

        var tickBrush = TickBrush;
        var timeBrush = TimeBrush;

        // Draw the bottom border
        _lengthGrid.Children.Add(new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = tickBrush
        });

        // Determine the number of major ticks that we should display.
        // This depends on the width of the timeline.
        var width = _lengthGrid.Bounds.Width;
        var majorTickCount = Math.Floor(width / 100);
        var totalSeconds = _source.PlayedFile.Duration.TotalSeconds;
        var majorTickSecondInterval = Math.Floor(totalSeconds / majorTickCount);
        majorTickSecondInterval = Math.Ceiling(majorTickSecondInterval / 10) * 10;
        var minorTickInterval = majorTickSecondInterval / 5 == 0 ? 1 : majorTickSecondInterval / 5;
        var minorTickCount = totalSeconds / minorTickInterval;

        for (var i = 0; i < minorTickCount; i++)
        {
            var interval = i * minorTickInterval;
            var positionPercent = interval / totalSeconds;
            var x = positionPercent * width;

            if (interval % majorTickSecondInterval != 0)
            {
                // Minor tick
                _lengthGrid.Children.Add(new Border
                {
                    Width = 1,
                    Height = 7,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = tickBrush,
                    Margin = new Thickness(x, 0, 0, 0)
                });
            }
            else
            {
                // Major tick
                _lengthGrid.Children.Add(new Border
                {
                    Width = 1,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = tickBrush,
                    Margin = new Thickness(x, 0, 0, 0)
                });

                // Add time label
                var ts = TimeSpan.FromSeconds(interval);
                _lengthGrid.Children.Add(new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = timeBrush,
                    Text = ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"mm\:ss"),
                    Margin = new Thickness(x + 5, 0, 0, 7)
                });
            }
        }
    }
}
