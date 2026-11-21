using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace IW4.Studio.Desktop.Editors.LoadedSounds;

/// <summary>A decoded-audio peak display with a seekable timeline.</summary>
public sealed class LoadedSoundWaveformControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty =
        AvaloniaProperty.Register<LoadedSoundWaveformControl, IReadOnlyList<float>?>(nameof(Peaks));

    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<LoadedSoundWaveformControl, TimeSpan>(nameof(Duration));

    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<LoadedSoundWaveformControl, TimeSpan>(nameof(Position));

    private static readonly Palette Light = new(
        new SolidColorBrush(Color.FromRgb(108, 121, 134)),
        new SolidColorBrush(Color.FromRgb(15, 112, 132)),
        new SolidColorBrush(Color.FromRgb(28, 48, 59)),
        new SolidColorBrush(Color.FromRgb(184, 194, 199)));

    private static readonly Palette Dark = new(
        new SolidColorBrush(Color.FromRgb(149, 166, 178)),
        new SolidColorBrush(Color.FromRgb(107, 213, 226)),
        new SolidColorBrush(Color.FromRgb(241, 248, 249)),
        new SolidColorBrush(Color.FromRgb(76, 93, 105)));

    private IPointer? _dragPointer;

    static LoadedSoundWaveformControl() =>
        AffectsRender<LoadedSoundWaveformControl>(PeaksProperty, DurationProperty, PositionProperty);

    public LoadedSoundWaveformControl()
    {
        ClipToBounds = true;
        Focusable = true;
        AutomationProperties.SetHelpText(this,
            "Click or drag to seek. Use arrow keys to seek in small steps, or Home and End to jump to either end.");
        UpdateAutomationName();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<float>? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public TimeSpan Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public TimeSpan Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public event EventHandler<TimeSpan>? SeekRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DurationProperty ||
            change.Property == PositionProperty ||
            change.Property == PeaksProperty)
            UpdateAutomationName();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 20 || height < 35)
            return;

        Palette colors = ActualThemeVariant == ThemeVariant.Dark ? Dark : Light;
        double labelTop = height - 20;
        double graphTop = 4;
        double graphBottom = labelTop - 5;
        double center = (graphTop + graphBottom) / 2;
        double halfHeight = Math.Max(1, (graphBottom - graphTop) / 2 - 3);
        double fraction = Duration > TimeSpan.Zero
            ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1)
            : 0;

        context.DrawLine(new Pen(colors.Guide, 1),
            new Point(0, center), new Point(width, center));

        if (Peaks is { Count: > 0 } peaks)
        {
            double slot = width / peaks.Count;
            double barWidth = Math.Max(1, Math.Min(4, slot * 0.7));
            var played = new Pen(colors.Played, barWidth);
            var unplayed = new Pen(colors.Unplayed, barWidth);
            for (int i = 0; i < peaks.Count; i++)
            {
                float value = peaks[i];
                double magnitude = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
                double barHeight = Math.Max(1, magnitude * halfHeight);
                double x = (i + 0.5) * slot;
                context.DrawLine(
                    x <= width * fraction ? played : unplayed,
                    new Point(x, center - barHeight),
                    new Point(x, center + barHeight));
            }
        }
        else
        {
            DrawText(context, "Waveform unavailable", colors.Unplayed,
                new Point(5, Math.Max(graphTop, center - 7)), 11);
        }

        double playheadX = Math.Clamp(width * fraction, 1, width - 1);
        context.DrawLine(new Pen(colors.Playhead, 2),
            new Point(playheadX, graphTop), new Point(playheadX, graphBottom));

        DrawText(context, Format(Position), colors.Playhead, new Point(0, labelTop), 11);
        string durationText = Format(Duration);
        var durationLabel = new FormattedText(durationText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, colors.Playhead);
        context.DrawText(durationLabel, new Point(width - durationLabel.Width, labelTop));

        if (IsFocused)
            context.DrawRectangle(null, new Pen(colors.Played, 1),
                new Rect(0.5, 0.5, width - 1, height - 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Duration <= TimeSpan.Zero)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
        SeekAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;

        SeekAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;

        SeekAt(e.GetPosition(this).X);
        e.Pointer.Capture(null);
        _dragPointer = null;
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragPointer = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Duration <= TimeSpan.Zero)
            return;

        double seconds = Position.TotalSeconds;
        double step = Math.Min(1, Duration.TotalSeconds / 20);
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) step *= 10;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                seconds -= step;
                break;
            case Key.Right:
            case Key.Up:
                seconds += step;
                break;
            case Key.PageDown:
                seconds -= 10;
                break;
            case Key.PageUp:
                seconds += 10;
                break;
            case Key.Home:
                seconds = 0;
                break;
            case Key.End:
                seconds = Duration.TotalSeconds;
                break;
            default:
                return;
        }

        Seek(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration.TotalSeconds)));
        e.Handled = true;
    }

    private void SeekAt(double x)
    {
        if (Bounds.Width <= 0 || !double.IsFinite(x))
            return;
        Seek(TimeSpan.FromSeconds(Duration.TotalSeconds * Math.Clamp(x / Bounds.Width, 0, 1)));
    }

    private void Seek(TimeSpan position)
    {
        Position = position;
        SeekRequested?.Invoke(this, position);
    }

    private void UpdateAutomationName() =>
        AutomationProperties.SetName(this,
            $"Sound timeline, {Format(Position)} of {Format(Duration)}" +
            (Peaks is { Count: > 0 } ? ", waveform available" : ", waveform unavailable"));

    private static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static void DrawText(
        DrawingContext context, string value, IBrush brush, Point point, double size) =>
        context.DrawText(new FormattedText(value, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, size, brush), point);

    private sealed record Palette(
        IBrush Unplayed, IBrush Played, IBrush Playhead, IBrush Guide);
}
