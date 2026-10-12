using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using FModel.Framework.Audio;

namespace FModel.Views.Resources.Controls.Aup;

/// <summary>Frequency bars of the playing audio (custom drawn, replaces the WPF Grid of Borders).</summary>
public sealed class SpectrumAnalyzer : TemplatedControl
{
    public enum ScalingStrategy
    {
        Decibel,
        Linear,
        Sqrt
    }

    public static readonly StyledProperty<ISource> SourceProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, ISource>(nameof(Source));

    public static readonly StyledProperty<ScalingStrategy> SpectrumScalingStrategyProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, ScalingStrategy>(nameof(SpectrumScalingStrategy), ScalingStrategy.Linear);

    public static readonly StyledProperty<int> FrequencyBarCountProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, int>(nameof(FrequencyBarCount), 25, coerce: (_, v) => Math.Clamp(v, 1, 100));

    public static readonly StyledProperty<int> FrequencyBarSpacingProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, int>(nameof(FrequencyBarSpacing), 2);

    public static readonly StyledProperty<IBrush> FrequencyBarBrushProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, IBrush>(nameof(FrequencyBarBrush), Brushes.LightGreen);

    public static readonly StyledProperty<IBrush> FrequencyBarBorderBrushProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, IBrush>(nameof(FrequencyBarBorderBrush), Brushes.Transparent);

    public static readonly StyledProperty<CornerRadius> FrequencyBarCornerRadiusProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, CornerRadius>(nameof(FrequencyBarCornerRadius), new CornerRadius(1, 1, 0, 0));

    public static readonly StyledProperty<Thickness> FrequencyBarBorderThicknessProperty =
        AvaloniaProperty.Register<SpectrumAnalyzer, Thickness>(nameof(FrequencyBarBorderThickness), new Thickness(0));

    public ISource Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public ScalingStrategy SpectrumScalingStrategy { get => GetValue(SpectrumScalingStrategyProperty); set => SetValue(SpectrumScalingStrategyProperty, value); }
    public int FrequencyBarCount { get => GetValue(FrequencyBarCountProperty); set => SetValue(FrequencyBarCountProperty, value); }
    public int FrequencyBarSpacing { get => GetValue(FrequencyBarSpacingProperty); set => SetValue(FrequencyBarSpacingProperty, value); }
    public IBrush FrequencyBarBrush { get => GetValue(FrequencyBarBrushProperty); set => SetValue(FrequencyBarBrushProperty, value); }
    public IBrush FrequencyBarBorderBrush { get => GetValue(FrequencyBarBorderBrushProperty); set => SetValue(FrequencyBarBorderBrushProperty, value); }
    public CornerRadius FrequencyBarCornerRadius { get => GetValue(FrequencyBarCornerRadiusProperty); set => SetValue(FrequencyBarCornerRadiusProperty, value); }
    public Thickness FrequencyBarBorderThickness { get => GetValue(FrequencyBarBorderThicknessProperty); set => SetValue(FrequencyBarBorderThicknessProperty, value); }

    static SpectrumAnalyzer()
    {
        AffectsRender<SpectrumAnalyzer>(FrequencyBarBrushProperty, FrequencyBarBorderBrushProperty, FrequencyBarCornerRadiusProperty,
            FrequencyBarBorderThicknessProperty, FrequencyBarSpacingProperty, BackgroundProperty);
    }

    private const int ScaleFactorLinear = 50;
    private const int ScaleFactorSqr = 2;
    private const int MaxFftIndex = 4096 / 2 - 1;
    private const int MaximumFrequency = 48000;
    private const int MinimumFrequency = 20;
    private const int SpectrumResolution = 100;
    private const double MinDbValue = -90;
    private const double DbScale = 0 - MinDbValue;

    private ISource _source;
    private SpectrumProvider _spectrumProvider;
    private int[] _spectrumIndexMax = new int[SpectrumResolution];
    private int _maximumFrequencyIndex;
    private int _minimumFrequencyIndex;
    private double[] _barHeights = [];
    private bool _barsVisible;

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
        }
        else if (change.Property == FrequencyBarCountProperty)
        {
            CreateBars();
        }
        else if (change.Property == BoundsProperty)
        {
            UpdateFrequencyMapping();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        CreateBars();
        UpdateFrequencyMapping();
    }

    private void OnSourceEvent(object sender, SourceEventArgs e)
    {
        if (e is not { Event: ESourceEventType.Loading }) return;
        _spectrumProvider = Source?.Spectrum;
        UpdateFrequencyMapping();
    }

    private void OnSourcePropertyChangedEvent(object sender, SourcePropertyChangedEventArgs e)
    {
        switch (e.Property)
        {
            case ESourceProperty.FftData:
                if (_source?.FftData is { } fftData)
                    UpdateSpectrum(SpectrumResolution, fftData);
                break;
            case ESourceProperty.HideToggle when (bool) e.Value == false:
            case ESourceProperty.PlaybackState when (PlaybackState) e.Value == PlaybackState.Playing:
                CreateBars();
                break;
            case ESourceProperty.HideToggle when (bool) e.Value:
                SilenceBars();
                break;
        }
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action, DispatcherPriority.Render);
    }

    private void CreateBars() => OnUiThread(() =>
    {
        _barHeights = new double[FrequencyBarCount];
        _barsVisible = true;
        InvalidateVisual();
    });

    private void SilenceBars() => OnUiThread(() =>
    {
        _barsVisible = false;
        InvalidateVisual();
    });

    private void UpdateFrequencyMapping()
    {
        if (_spectrumProvider == null) return;

        _maximumFrequencyIndex = Math.Min(_spectrumProvider.GetFftBandIndex(MaximumFrequency) + 1, MaxFftIndex);
        _minimumFrequencyIndex = Math.Min(_spectrumProvider.GetFftBandIndex(MinimumFrequency), MaxFftIndex);
        if (_spectrumIndexMax.Length != SpectrumResolution)
            _spectrumIndexMax = new int[SpectrumResolution];
        else
            Array.Clear(_spectrumIndexMax);

        var indexCount = _maximumFrequencyIndex - _minimumFrequencyIndex;
        var linearIndexBucketSize = Math.Round(indexCount / (double) SpectrumResolution, 3);
        for (var i = 1; i < SpectrumResolution; i++)
        {
            _spectrumIndexMax[i - 1] = _minimumFrequencyIndex + (int) (i * linearIndexBucketSize);
        }

        _spectrumIndexMax[^1] = _maximumFrequencyIndex;
    }

    private void UpdateSpectrum(double maxValue, IReadOnlyList<float> fftBuffer)
    {
        var spectrumScalingStrategy = SpectrumScalingStrategy;

        var lastValue = 0D;
        var spectrumPointIndex = 0;
        var dataPoints = new List<double>(SpectrumResolution);
        for (var i = _minimumFrequencyIndex; i <= _maximumFrequencyIndex && i < fftBuffer.Count; i++)
        {
            var tempVal = spectrumScalingStrategy switch
            {
                ScalingStrategy.Decibel => (20 * Math.Log10(fftBuffer[i]) - MinDbValue) / DbScale * maxValue,
                ScalingStrategy.Linear => fftBuffer[i] * ScaleFactorLinear * maxValue,
                ScalingStrategy.Sqrt => Math.Sqrt(fftBuffer[i]) * ScaleFactorSqr * maxValue,
                _ => 0D
            };

            var bAgain = true;
            var value = Math.Max(0, Math.Max(tempVal, 0));
            while (spectrumPointIndex <= _spectrumIndexMax.Length - 1 && i == _spectrumIndexMax[spectrumPointIndex])
            {
                if (!bAgain)
                    value = lastValue;

                if (value > maxValue)
                    value = maxValue;

                if (spectrumPointIndex > 0)
                    value = (lastValue + value) / 1.5;

                dataPoints.Add(value);
                lastValue = value;
                value = 0.0;
                spectrumPointIndex++;
                bAgain = false;
            }
        }

        OnUiThread(() =>
        {
            if (_barHeights.Length != FrequencyBarCount) _barHeights = new double[FrequencyBarCount];
            for (var i = 0; i < _barHeights.Length; i++)
                _barHeights[i] = i < dataPoints.Count ? dataPoints[i] / 100 * Bounds.Height : 0;
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background != null)
            context.FillRectangle(Background, bounds);

        if (!_barsVisible || _barHeights.Length == 0) return;

        var barSpacing = bounds.Width / _barHeights.Length;
        var barWidth = Math.Max(.5, barSpacing - FrequencyBarSpacing);
        var radius = FrequencyBarCornerRadius;
        var thickness = FrequencyBarBorderThickness;
        var pen = thickness.Left > 0 && FrequencyBarBorderBrush != null ? new Pen(FrequencyBarBorderBrush, thickness.Left) : null;
        var brush = FrequencyBarBrush;

        for (var i = 0; i < _barHeights.Length; i++)
        {
            var height = Math.Min(_barHeights[i], bounds.Height);
            if (height <= 0) continue;

            var rect = new Rect(i * barSpacing, bounds.Height - height, barWidth, height);
            context.DrawRectangle(brush, pen, new RoundedRect(rect,
                new Vector(radius.TopLeft, radius.TopLeft), new Vector(radius.TopRight, radius.TopRight),
                new Vector(radius.BottomRight, radius.BottomRight), new Vector(radius.BottomLeft, radius.BottomLeft)));
        }
    }
}
