using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace FModel.Views.Resources.Controls.Aup;

[TemplatePart("PART_Timeclock", typeof(Grid))]
[TemplatePart("PART_Time", typeof(TextBlock))]
public sealed class Timeclock : TemplatedControl
{
    public enum EClockType
    {
        TimeElapsed,
        TimeRemaining
    }

    private const string DefaultTimeFormat = "hh\\:mm\\:ss\\.ff";

    public static readonly StyledProperty<ISource> SourceProperty =
        AvaloniaProperty.Register<Timeclock, ISource>(nameof(Source));

    public static readonly StyledProperty<EClockType> ClockTypeProperty =
        AvaloniaProperty.Register<Timeclock, EClockType>(nameof(ClockType), EClockType.TimeElapsed);

    public static readonly StyledProperty<FontFamily> LabelFontProperty =
        AvaloniaProperty.Register<Timeclock, FontFamily>(nameof(LabelFont), new FontFamily("Segoe UI"));

    public static readonly StyledProperty<IBrush> LabelForegroundProperty =
        AvaloniaProperty.Register<Timeclock, IBrush>(nameof(LabelForeground), Brushes.Coral);

    public static readonly StyledProperty<FontFamily> TimeFontProperty =
        AvaloniaProperty.Register<Timeclock, FontFamily>(nameof(TimeFont), new FontFamily("Ebrima"));

    public static readonly StyledProperty<IBrush> TimeForegroundProperty =
        AvaloniaProperty.Register<Timeclock, IBrush>(nameof(TimeForeground), Brushes.Silver);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<Timeclock, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string> TimeFormatProperty =
        AvaloniaProperty.Register<Timeclock, string>(nameof(TimeFormat), DefaultTimeFormat, coerce: (_, v) =>
        {
            try
            {
                TimeSpan.Zero.ToString(v);
                return v;
            }
            catch
            {
                return DefaultTimeFormat;
            }
        });

    public ISource Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public EClockType ClockType { get => GetValue(ClockTypeProperty); set => SetValue(ClockTypeProperty, value); }
    public FontFamily LabelFont { get => GetValue(LabelFontProperty); set => SetValue(LabelFontProperty, value); }
    public IBrush LabelForeground { get => GetValue(LabelForegroundProperty); set => SetValue(LabelForegroundProperty, value); }
    public FontFamily TimeFont { get => GetValue(TimeFontProperty); set => SetValue(TimeFontProperty, value); }
    public IBrush TimeForeground { get => GetValue(TimeForegroundProperty); set => SetValue(TimeForegroundProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string TimeFormat { get => GetValue(TimeFormatProperty); set => SetValue(TimeFormatProperty, value); }

    static Timeclock()
    {
        CornerRadiusProperty.OverrideDefaultValue<Timeclock>(new CornerRadius(3));
    }

    private ISource _source;
    private Grid _timeclockGrid;
    private TextBlock _timeText;

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
        else if (change.Property == ClockTypeProperty || change.Property == TimeFormatProperty)
        {
            CalculateTime();
        }
    }

    private void OnSourceEvent(object sender, SourceEventArgs e)
    {
        if (e is not { Event: ESourceEventType.Loading } || Source == null) return;
        var label = Source.PlayedFile.FileName;
        Dispatcher.UIThread.Post(() =>
        {
            Label = label;
            CalculateTime();
        });
    }

    private void OnSourcePropertyChangedEvent(object sender, SourcePropertyChangedEventArgs e)
    {
        if (_timeText == null || e.Property != ESourceProperty.Position) return;

        CalculateTime();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _timeclockGrid = e.NameScope.Find<Grid>("PART_Timeclock");
        _timeText = e.NameScope.Find<TextBlock>("PART_Time");

        OnSourceEvent(this, null);
    }

    private void CalculateTime()
    {
        if (_source != null)
        {
            var position = _source.PlayedFile.Position;
            var length = _source.PlayedFile.Duration;
            Post(() =>
            {
                if (_timeText == null) return;
                _timeText.Text = ClockType switch
                {
                    EClockType.TimeElapsed => position.ToString(TimeFormat),
                    EClockType.TimeRemaining => (length - position).ToString(TimeFormat),
                    _ => _timeText.Text
                };
            });
        }
        else
        {
            ZeroTime();
        }
    }

    private void ZeroTime()
    {
        Post(() =>
        {
            if (_timeText != null)
                _timeText.Text = TimeSpan.Zero.ToString(TimeFormat);
        });
    }

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
