using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.Sound;

/// <summary>
/// Draws parsed MPEG frame gain as the resting profile and replaces elapsed
/// bars with native-player output meter samples while audio is running.
/// </summary>
public sealed class SoundLevelVisualizer : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?>
        FrameGainProfileProperty = AvaloniaProperty.Register<
            SoundLevelVisualizer,
            IReadOnlyList<double>?>(nameof(FrameGainProfile));

    public static readonly StyledProperty<IReadOnlyList<double>?>
        LiveLevelsProperty = AvaloniaProperty.Register<
            SoundLevelVisualizer,
            IReadOnlyList<double>?>(nameof(LiveLevels));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<SoundLevelVisualizer, double>(
            nameof(Progress));

    public static readonly StyledProperty<IBrush?> RestingBrushProperty =
        AvaloniaProperty.Register<SoundLevelVisualizer, IBrush?>(
            nameof(RestingBrush));

    public static readonly StyledProperty<IBrush?> PlayedBrushProperty =
        AvaloniaProperty.Register<SoundLevelVisualizer, IBrush?>(
            nameof(PlayedBrush));

    static SoundLevelVisualizer() =>
        AffectsRender<SoundLevelVisualizer>(
            FrameGainProfileProperty,
            LiveLevelsProperty,
            ProgressProperty,
            RestingBrushProperty,
            PlayedBrushProperty);

    public SoundLevelVisualizer() => ClipToBounds = true;

    public IReadOnlyList<double>? FrameGainProfile
    {
        get => GetValue(FrameGainProfileProperty);
        set => SetValue(FrameGainProfileProperty, value);
    }

    public IReadOnlyList<double>? LiveLevels
    {
        get => GetValue(LiveLevelsProperty);
        set => SetValue(LiveLevelsProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public IBrush? RestingBrush
    {
        get => GetValue(RestingBrushProperty);
        set => SetValue(RestingBrushProperty, value);
    }

    public IBrush? PlayedBrush
    {
        get => GetValue(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IReadOnlyList<double> profile = FrameGainProfile ?? [];
        if (profile.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
            return;

        IBrush restingBrush = RestingBrush ?? Brushes.Gray;
        IBrush playedBrush = PlayedBrush ?? Brushes.LimeGreen;
        IReadOnlyList<double> liveLevels = LiveLevels ?? [];
        double progress = double.IsFinite(Progress)
            ? Math.Clamp(Progress, 0, 1)
            : 0;
        double slotWidth = Bounds.Width / profile.Count;
        double barWidth = Math.Clamp(slotWidth * 0.56, 1, 5);
        double availableHeight = Math.Max(2, Bounds.Height - 18);
        int playedThrough = progress <= 0
            ? -1
            : Math.Clamp(
                (int)Math.Floor(progress * profile.Count),
                0,
                profile.Count - 1);

        for (int index = 0; index < profile.Count; index++)
        {
            bool played = index <= playedThrough;
            bool hasLiveLevel = index < liveLevels.Count &&
                double.IsFinite(liveLevels[index]);
            double live = hasLiveLevel
                ? Normalize(liveLevels[index])
                : 0;
            double resting = Normalize(profile[index]);
            double level = played && hasLiveLevel
                ? live
                : resting;
            double barHeight = Math.Max(3, availableHeight * (0.16 + level * 0.84));
            double x = index * slotWidth + (slotWidth - barWidth) / 2;
            double y = (Bounds.Height - barHeight) / 2;
            context.FillRectangle(
                played ? playedBrush : restingBrush,
                new Rect(x, y, barWidth, barHeight),
                (float)Math.Min(2, barWidth / 2));
        }

        if (progress <= 0 || progress >= 1)
            return;

        double playheadX = Math.Clamp(
            Bounds.Width * progress,
            0.5,
            Math.Max(0.5, Bounds.Width - 0.5));
        context.DrawLine(
            new Pen(playedBrush, 1),
            new Point(playheadX, 4),
            new Point(playheadX, Bounds.Height - 4));
    }

    private static double Normalize(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : 0;
}
