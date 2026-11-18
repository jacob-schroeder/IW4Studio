using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IW4.Game.Assets.Tracer;

namespace IW4.Studio.Desktop.Editors.Tracer;

public sealed class TracerPreviewControl : Control
{
    public static readonly StyledProperty<TracerDefAsset?> TracerProperty =
        AvaloniaProperty.Register<TracerPreviewControl, TracerDefAsset?>(nameof(Tracer));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<TracerPreviewControl, bool>(nameof(IsPlaying));

    public static readonly StyledProperty<bool> EndOnProperty =
        AvaloniaProperty.Register<TracerPreviewControl, bool>(nameof(EndOn));

    private const int RibbonSegments = 48;
    private static readonly IBrush StageBrush = new SolidColorBrush(Color.FromRgb(17, 20, 25));
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.FromArgb(48, 151, 175, 196)), 1);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromMilliseconds(1000d / 30)
    };
    private readonly Stopwatch _playback = new();
    private readonly Color[] _colors = new Color[TracerDefAsset.ColorCount];
    private Visual[] _ancestors = [];
    private LinearGradientBrush? _ribbonBrush;
    private double _inspectionSample;
    private bool _attached;

    static TracerPreviewControl() => AffectsRender<TracerPreviewControl>(TracerProperty, EndOnProperty);

    public TracerPreviewControl()
    {
        ClipToBounds = true;
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    public TracerDefAsset? Tracer
    {
        get => GetValue(TracerProperty);
        set => SetValue(TracerProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public bool EndOn
    {
        get => GetValue(EndOnProperty);
        set => SetValue(EndOnProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ancestors = this.GetVisualAncestors().ToArray();
        foreach (Visual ancestor in _ancestors)
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        _attached = true;
        UpdateClock();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdateClock();
        foreach (Visual ancestor in _ancestors)
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        _ancestors = [];
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TracerProperty)
            UpdateColors();
        if (change.Property == TracerProperty || change.Property == IsPlayingProperty)
        {
            UpdateClock();
            InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty && _attached)
        {
            UpdateClock();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var stage = new Rect(Bounds.Size);
        if (!double.IsFinite(stage.Width) || !double.IsFinite(stage.Height))
            return;

        context.DrawRectangle(StageBrush, null, stage);
        double size = Math.Min(stage.Width, stage.Height);
        if (size <= 8)
            return;

        var center = new Point(stage.Width / 2, stage.Height / 2);
        context.DrawLine(GuidePen, new Point(0, center.Y), new Point(stage.Width, center.Y));
        if (EndOn)
            context.DrawLine(GuidePen, new Point(center.X, 0), new Point(center.X, stage.Height));

        if (Tracer is not { } tracer || _ribbonBrush is not { } brush || !HasBeam(tracer))
            return;

        // Fit native distances to the preview and bound the number of visible screw turns.
        double length = Math.Max(1, stage.Width * 0.68 * Fit(tracer.BeamLength, 100));
        double width = Math.Max(0.75, size * 0.26 * Fit(tracer.BeamWidth, 8));
        double distance = Positive(tracer.ScrewDistance);
        double radius = distance > 0 ? size * 0.33 * Fit(tracer.ScrewRadius, 20) : 0;
        double turns = distance > 0 ? Math.Min(6, tracer.BeamLength / distance) : 0;
        if (EndOn && radius > 0.5)
            context.DrawEllipse(null, GuidePen, center, radius, radius);

        double progress = GetProgress(tracer);
        bool betweenSweeps = progress > 1;
        if (betweenSweeps && IsPlaying)
            return;
        if (betweenSweeps)
            progress = 0.5;

        double angle = radius > 0 ? Math.Tau * progress * Math.Min(6, 400 / distance) : 0;
        if (EndOn)
        {
            double sample = betweenSweeps ? _inspectionSample : progress;
            double sectionAngle = angle + (sample - 1) * turns * Math.Tau;
            var section = new Point(
                center.X + radius * Math.Cos(sectionAngle),
                center.Y + radius * Math.Sin(sectionAngle));
            context.DrawEllipse(new SolidColorBrush(ColorAt(sample)), null, section, width / 2, width / 2);
            return;
        }

        double left = stage.Width * (0.05 + 0.9 * progress) - length / 2;
        if (radius <= 0.5)
        {
            context.DrawRectangle(brush, null, new Rect(left, center.Y - width / 2, length, width));
            return;
        }

        var ribbon = new StreamGeometry();
        using (StreamGeometryContext path = ribbon.Open())
        {
            path.BeginFigure(RibbonPoint(0, -width / 2), isFilled: true);
            for (int index = 1; index <= RibbonSegments; index++)
                path.LineTo(RibbonPoint(index, -width / 2));
            for (int index = RibbonSegments; index >= 0; index--)
                path.LineTo(RibbonPoint(index, width / 2));
            path.EndFigure(isClosed: true);
        }
        context.DrawGeometry(brush, null, ribbon);

        Point RibbonPoint(int index, double edge)
        {
            double offset = index / (double)RibbonSegments;
            double phase = angle + (offset - 1) * turns * Math.Tau;
            return new Point(left + length * offset, center.Y + radius * Math.Sin(phase) + edge);
        }
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty)
            UpdateClock();
    }

    private void UpdateClock()
    {
        if (_attached && IsPlaying && _ribbonBrush is not null && IsVisible &&
            _ancestors.All(ancestor => ancestor.IsVisible) && Tracer is { } tracer &&
            HasBeam(tracer) && Positive(tracer.Speed) > 0)
        {
            _playback.Start();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _playback.Stop();
        }
    }

    private double GetProgress(TracerDefAsset tracer)
    {
        double speed = Positive(tracer.Speed);
        if (speed == 0)
            return 0.5;

        // Compress movement and draw cadence into readable preview times.
        double flight = Math.Max(0.36, 2.8 / (1 + Math.Log10(1 + speed / 200)));
        double gap = Math.Min(1.5, 0.12 + 0.16 * Math.Log2(1d + tracer.DrawInterval));
        return ((_playback.Elapsed.TotalSeconds + flight / 2) % (flight + gap)) / flight;
    }

    private void UpdateColors()
    {
        var colors = Tracer?.Colors;
        var stops = new GradientStops();
        int strongestSample = 0;
        for (int index = 0; index < _colors.Length; index++)
        {
            TracerColor sample = colors is not null && index < colors.Count ? colors[index] : default;
            Color color = Color.FromArgb(Channel(sample.Alpha), Channel(sample.Red), Channel(sample.Green), Channel(sample.Blue));
            _colors[index] = color;
            if (color.A > _colors[strongestSample].A)
                strongestSample = index;
            stops.Add(new GradientStop(color, index / (double)(_colors.Length - 1)));
        }

        _inspectionSample = strongestSample / (double)(_colors.Length - 1);
        _ribbonBrush = _colors[strongestSample].A > 0 ? new LinearGradientBrush
        {
            StartPoint = RelativePoint.TopLeft,
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = stops
        } : null;
    }

    private Color ColorAt(double progress)
    {
        double sampleIndex = progress * (_colors.Length - 1);
        int index = Math.Min((int)sampleIndex, _colors.Length - 2);
        double fraction = sampleIndex - index;
        Color start = _colors[index];
        Color end = _colors[index + 1];
        return Color.FromArgb(Mix(start.A, end.A), Mix(start.R, end.R), Mix(start.G, end.G), Mix(start.B, end.B));

        byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * fraction);
    }

    private static bool HasBeam(TracerDefAsset tracer) => Positive(tracer.BeamLength) > 0 && Positive(tracer.BeamWidth) > 0;
    private static double Positive(float value) => float.IsFinite(value) ? Math.Max(value, 0) : 0;
    private static double Fit(float value, double midpoint)
    {
        double positive = Positive(value);
        return positive / (positive + midpoint);
    }

    private static byte Channel(float value) => (byte)Math.Round(Math.Min(1, Positive(value)) * byte.MaxValue);
}
