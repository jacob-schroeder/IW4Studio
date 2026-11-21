using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using IW4.Formats.SourceFormat.XAnim;

namespace IW4.Studio.Desktop.Editors.XAnim;

/// <summary>Displays named XAnim tracks in their separate local coordinate spaces.</summary>
public sealed class XAnimBoneTracksControl : Control
{
    public static readonly StyledProperty<XAnimPlaybackClip?> AnimationClipProperty =
        AvaloniaProperty.Register<XAnimBoneTracksControl, XAnimPlaybackClip?>(nameof(AnimationClip));

    public static readonly StyledProperty<double> FrameProperty =
        AvaloniaProperty.Register<XAnimBoneTracksControl, double>(nameof(Frame));

    private const double Inset = 12;
    private const double Gap = 6;
    private const double TileWidth = 158;
    private const double MinimumTileHeight = 128;

    private static readonly Axes Light = new(
        new Pen(new SolidColorBrush(Color.FromRgb(185, 91, 82)), 2.3),
        new Pen(new SolidColorBrush(Color.FromRgb(69, 143, 109)), 2.3),
        new Pen(new SolidColorBrush(Color.FromRgb(76, 121, 170)), 2.3),
        new Pen(new SolidColorBrush(Color.FromArgb(45, 72, 88, 91)), 1));

    private static readonly Axes Dark = new(
        new Pen(new SolidColorBrush(Color.FromRgb(226, 139, 127)), 2.3),
        new Pen(new SolidColorBrush(Color.FromRgb(116, 194, 150)), 2.3),
        new Pen(new SolidColorBrush(Color.FromRgb(120, 170, 219)), 2.3),
        new Pen(new SolidColorBrush(Color.FromArgb(55, 194, 211, 214)), 1));

    private XAnimLocalBoneTransform[] _sample = [];
    private XAnimLocalBoneTransform[] _firstFrame = [];
    private FormattedText[] _names = [];
    private IBrush? _nameBrush;
    private double _nameWidth;

    static XAnimBoneTracksControl() =>
        AffectsRender<XAnimBoneTracksControl>(AnimationClipProperty, FrameProperty);

    public XAnimBoneTracksControl()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public XAnimPlaybackClip? AnimationClip
    {
        get => GetValue(AnimationClipProperty);
        set => SetValue(AnimationClipProperty, value);
    }

    public double Frame
    {
        get => GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 580;
        int count = AnimationClip?.BoneCount ?? 0;
        int columns = ColumnCount(width, count);
        int rows = (count + columns - 1) / columns;
        return new Size(width, rows == 0 ? 0 : Inset * 2 + rows * MinimumTileHeight + (rows - 1) * Gap);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != AnimationClipProperty)
            return;

        XAnimPlaybackClip? clip = AnimationClip;
        _sample = clip is null ? [] : new XAnimLocalBoneTransform[clip.BoneCount];
        _firstFrame = new XAnimLocalBoneTransform[_sample.Length];
        _names = [];
        _nameBrush = null;
        if (clip is not null)
            clip.Sample(0f, _firstFrame);
        InvalidateMeasure();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        XAnimPlaybackClip? clip = AnimationClip;
        if (clip is null || clip.BoneCount == 0 || Bounds.Width <= Inset * 2)
            return;

        clip.Sample(float.IsFinite((float)Frame) ? (float)Frame : 0f, _sample);
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        IBrush textBrush = this.TryFindResource("StudioTextBrush", ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush : dark ? Brushes.White : Brushes.Black;
        Axes axes = dark ? Dark : Light;
        int columns = ColumnCount(Bounds.Width, clip.BoneCount);
        int rows = (clip.BoneCount + columns - 1) / columns;
        double tileWidth = (Bounds.Width - Inset * 2 - (columns - 1) * Gap) / columns;
        double tileHeight = Math.Min(250, (Bounds.Height - Inset * 2 - (rows - 1) * Gap) / rows);
        double gridTop = (Bounds.Height - rows * tileHeight - (rows - 1) * Gap) * 0.5;
        double glyphHeight = Math.Max(1, tileHeight - 26);
        float scale = (float)Math.Clamp(Math.Min(tileWidth - 12, glyphHeight) / 85, 0.5, 2.8);
        EnsureNames(clip, textBrush, tileWidth);

        for (int index = 0; index < clip.BoneCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            int rowCount = Math.Min(columns, clip.BoneCount - row * columns);
            double left = (Bounds.Width - rowCount * tileWidth - (rowCount - 1) * Gap) * 0.5 + column * (tileWidth + Gap);
            double top = gridTop + row * (tileHeight + Gap);
            Point origin = new(left + tileWidth * 0.5, top + glyphHeight * 0.5);
            DrawTrack(context, origin, _sample[index], _firstFrame[index], scale, axes, textBrush);
            context.DrawText(_names[index], new Point(left + 8, origin.Y + 32 * scale + 4));
        }
    }

    private void EnsureNames(XAnimPlaybackClip clip, IBrush brush, double tileWidth)
    {
        double width = Math.Max(1, tileWidth - 16);
        if (_names.Length == clip.BoneCount && ReferenceEquals(_nameBrush, brush) && _nameWidth == width)
            return;

        _names = new FormattedText[clip.BoneCount];
        for (int index = 0; index < _names.Length; index++)
        {
            _names[index] = new FormattedText(clip.BoneNames[index], CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 13, brush)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = width,
                MaxTextHeight = 18,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
        }
        _nameBrush = brush;
        _nameWidth = width;
    }

    private static void DrawTrack(DrawingContext context, Point origin,
        XAnimLocalBoneTransform transform, XAnimLocalBoneTransform first, float scale, Axes axes, IBrush pivotBrush)
    {
        Vector3 displacement = transform.Translation - first.Translation;
        float distance = displacement.Length();
        Vector2 travel = float.IsFinite(distance)
            ? Project(displacement) * (14f * scale / (1f + distance))
            : Vector2.Zero;
        Point pivot = new(origin.X + travel.X, origin.Y + travel.Y);

        context.DrawEllipse(null, axes.Guide, origin, 2.5, 2.5);
        if (travel.LengthSquared() > 0.01f)
            context.DrawLine(axes.Guide, origin, pivot);

        Quaternion rotation = transform.Rotation;
        if (MathF.Abs(Quaternion.Dot(rotation, first.Rotation)) < 0.9999f || travel.LengthSquared() > 1)
        {
            Vector2 rest = Project(Vector3.Transform(Vector3.UnitX, first.Rotation)) * (25 * scale);
            context.DrawLine(axes.Guide, origin, new Point(origin.X + rest.X, origin.Y + rest.Y));
        }
        Vector2 x = Project(Vector3.Transform(Vector3.UnitX, rotation)) * scale;
        Vector2 y = Project(Vector3.Transform(Vector3.UnitY, rotation)) * scale;
        Vector2 z = Project(Vector3.Transform(Vector3.UnitZ, rotation)) * scale;
        Point Offset(Vector2 delta) => new(pivot.X + delta.X, pivot.Y + delta.Y);

        // A tapered bone and two shorter axes make local twist visible even at a fixed pivot.
        Point tip = Offset(x * 25);
        Point left = Offset(x * 6 + y * 4);
        Point right = Offset(x * 6 - y * 4);
        context.DrawLine(axes.X, pivot, left);
        context.DrawLine(axes.X, left, tip);
        context.DrawLine(axes.X, tip, right);
        context.DrawLine(axes.X, right, pivot);
        context.DrawLine(axes.Y, pivot, Offset(y * 15));
        context.DrawLine(axes.Z, pivot, Offset(z * 15));
        context.DrawEllipse(pivotBrush, null, pivot, 2.8, 2.8);
    }

    private static Vector2 Project(Vector3 point) => new(
        (point.X - point.Y) * 0.7071f,
        (point.X + point.Y) * 0.4082f - point.Z * 0.8165f);

    private static int ColumnCount(double width, int boneCount)
    {
        int columnsThatFit = (int)Math.Floor((width - Inset * 2 + Gap) / (TileWidth + Gap));
        int balancedColumns = (int)Math.Ceiling(Math.Sqrt(boneCount * 1.5));
        return Math.Max(1, Math.Min(columnsThatFit, balancedColumns));
    }

    private readonly record struct Axes(Pen X, Pen Y, Pen Z, Pen Guide);
}
