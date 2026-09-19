using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IW4.Assets.Assets.Fx;
using IW4.Render.EditorPreview;

namespace IW4.Studio.Desktop.Editors.Fx;

/// <summary>
/// Interactive analytical presentation of a sampled FX frame. Sprite cards
/// use the compiled visual state; engine-integrated element kinds are drawn as
/// deliberately distinct proxies rather than simulated materials, models,
/// lights, sounds, decals, trails, or nested effects.
/// </summary>
public sealed class FxPreviewControl : Control
{
    public static readonly StyledProperty<FxPreviewFrame?> FrameProperty =
        AvaloniaProperty.Register<FxPreviewControl, FxPreviewFrame?>(
            nameof(Frame));

    public static readonly StyledProperty<int> SelectedElementIndexProperty =
        AvaloniaProperty.Register<FxPreviewControl, int>(
            nameof(SelectedElementIndex),
            -1);

    private static readonly IBrush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(13, 16, 21));
    private static readonly Pen GridPen =
        new(new SolidColorBrush(Color.FromArgb(38, 127, 139, 154)), 1);
    private static readonly Pen GridMajorPen =
        new(new SolidColorBrush(Color.FromArgb(64, 144, 158, 174)), 1);
    private static readonly Pen XAxisPen =
        new(new SolidColorBrush(Color.FromArgb(155, 219, 93, 89)), 1.35);
    private static readonly Pen YAxisPen =
        new(new SolidColorBrush(Color.FromArgb(155, 91, 207, 130)), 1.35);
    private static readonly Pen ZAxisPen =
        new(new SolidColorBrush(Color.FromArgb(155, 83, 151, 223)), 1.35);
    private static readonly Pen VelocityPen =
        new(new SolidColorBrush(Color.FromArgb(115, 188, 200, 214)), 1);
    private static readonly Pen SelectedPen =
        new(new SolidColorBrush(Color.FromRgb(255, 190, 74)), 2.1);
    private static readonly IBrush ProxyFillBrush =
        new SolidColorBrush(Color.FromArgb(58, 106, 184, 211));
    private static readonly Pen ProxyPen =
        new(new SolidColorBrush(Color.FromArgb(215, 116, 205, 230)), 1.5);
    private static readonly IBrush ModelFillBrush =
        new SolidColorBrush(Color.FromArgb(35, 177, 150, 232));
    private static readonly Pen ModelPen =
        new(new SolidColorBrush(Color.FromArgb(220, 190, 166, 239)), 1.4);
    private static readonly Pen SoundPen =
        new(new SolidColorBrush(Color.FromArgb(220, 113, 205, 255)), 1.4);
    private static readonly Pen DecalPen =
        new(new SolidColorBrush(Color.FromArgb(220, 231, 126, 187)), 1.4);
    private static readonly IBrush DecalFillBrush =
        new SolidColorBrush(Color.FromArgb(42, 231, 126, 187));
    private static readonly Pen RunnerPen =
        new(new SolidColorBrush(Color.FromArgb(220, 113, 225, 153)), 1.5);

    private const float DefaultYaw = -0.68f;
    private const float DefaultPitch = -0.32f;
    private const float VelocityLookAheadSeconds = 0.08f;
    private const float MaximumVelocityDisplacement = 192f;

    private IPointer? _dragPointer;
    private Point _lastPointerPosition;
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _zoom = 1f;
    private bool _hasWorldBounds;
    private Vector3 _worldMinimum;
    private Vector3 _worldMaximum;
    private FxPreviewFrame? _lastRenderedFrame;
    private float _lastRenderedElapsedMilliseconds;

    static FxPreviewControl() =>
        AffectsRender<FxPreviewControl>(
            FrameProperty,
            SelectedElementIndexProperty);

    public FxPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += Preview_PointerPressed;
        PointerMoved += Preview_PointerMoved;
        PointerReleased += Preview_PointerReleased;
        PointerCaptureLost += Preview_PointerCaptureLost;
        PointerWheelChanged += Preview_PointerWheelChanged;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    public FxPreviewFrame? Frame
    {
        get => GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public int SelectedElementIndex
    {
        get => GetValue(SelectedElementIndexProperty);
        set => SetValue(SelectedElementIndexProperty, value);
    }

    /// <summary>Restores the isometric camera and forgets sampled bounds.</summary>
    public void Fit()
    {
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _zoom = 1f;
        ClearWorldBounds();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, Bounds);

        FxPreviewFrame? frame = Frame;
        TrackFrameSequence(frame);
        if (frame?.Instances is { Count: > 0 } instances)
            ExpandWorldBounds(instances);

        Projection projection = CreateProjection();
        DrawGroundGrid(context, projection);

        if (frame?.Instances is not { Count: > 0 } drawableInstances)
            return;

        ProjectedInstance[] projected = drawableInstances
            .Where(candidate => IsFinite(candidate.Position))
            .Select(candidate => Project(candidate, projection))
            .OrderByDescending(candidate => candidate.Position.Depth)
            .ToArray();

        foreach (ProjectedInstance item in projected)
        {
            bool selected =
                item.Instance.ElementIndex == SelectedElementIndex;
            DrawVelocity(context, item, selected);
            DrawInstance(context, item, projection.Scale, selected);
        }
    }

    private void TrackFrameSequence(FxPreviewFrame? frame)
    {
        if (ReferenceEquals(frame, _lastRenderedFrame))
            return;

        if (frame is null ||
            frame.ElapsedMilliseconds + 0.5f <
            _lastRenderedElapsedMilliseconds)
        {
            ClearWorldBounds();
        }

        _lastRenderedFrame = frame;
        _lastRenderedElapsedMilliseconds =
            frame?.ElapsedMilliseconds ?? 0f;
    }

    private void ExpandWorldBounds(
        IReadOnlyList<FxPreviewInstance> instances)
    {
        IncludeWorldPoint(Vector3.Zero);
        foreach (FxPreviewInstance instance in instances)
        {
            if (!IsFinite(instance.Position))
                continue;

            float visualScale = VisualScale(instance);
            float sizeX = float.IsFinite(instance.Size.X)
                ? MathF.Abs(instance.Size.X) * visualScale
                : 0f;
            float sizeY = float.IsFinite(instance.Size.Y)
                ? MathF.Abs(instance.Size.Y) * visualScale
                : 0f;
            float radius = Math.Clamp(
                MathF.Max(sizeX, sizeY),
                0f,
                256f);
            Vector3 extent = new(radius);
            IncludeWorldPoint(instance.Position - extent);
            IncludeWorldPoint(instance.Position + extent);
            IncludeWorldPoint(VelocityEndpoint(instance));
        }
    }

    private void IncludeWorldPoint(Vector3 point)
    {
        if (!IsFinite(point))
            return;

        if (!_hasWorldBounds)
        {
            _worldMinimum = point;
            _worldMaximum = point;
            _hasWorldBounds = true;
            return;
        }

        _worldMinimum = Vector3.Min(_worldMinimum, point);
        _worldMaximum = Vector3.Max(_worldMaximum, point);
    }

    private void ClearWorldBounds()
    {
        _hasWorldBounds = false;
        _worldMinimum = default;
        _worldMaximum = default;
    }

    private Projection CreateProjection()
    {
        Matrix4x4 view = Matrix4x4.CreateRotationY(_yaw) *
            Matrix4x4.CreateRotationX(_pitch);
        Vector3 minimum = _hasWorldBounds
            ? _worldMinimum
            : new Vector3(-64f, -24f, -64f);
        Vector3 maximum = _hasWorldBounds
            ? _worldMaximum
            : new Vector3(64f, 64f, 64f);

        Vector3 size = maximum - minimum;
        Vector3 padding = new(
            MathF.Max(8f, size.X * 0.08f),
            MathF.Max(8f, size.Y * 0.08f),
            MathF.Max(8f, size.Z * 0.08f));
        minimum -= padding;
        maximum += padding;

        Vector2 projectedMinimum = new(float.PositiveInfinity);
        Vector2 projectedMaximum = new(float.NegativeInfinity);
        foreach (Vector3 corner in BoundsCorners(minimum, maximum))
        {
            Vector3 rotated = Vector3.Transform(corner, view);
            Vector2 point = new(rotated.X, -rotated.Y);
            projectedMinimum = Vector2.Min(projectedMinimum, point);
            projectedMaximum = Vector2.Max(projectedMaximum, point);
        }

        Vector2 projectedSize = projectedMaximum - projectedMinimum;
        double availableWidth = Math.Max(1d, Bounds.Width - 64d);
        double availableHeight = Math.Max(1d, Bounds.Height - 64d);
        double fitScale = Math.Min(
            availableWidth / Math.Max(1f, projectedSize.X),
            availableHeight / Math.Max(1f, projectedSize.Y));
        double scale = Math.Clamp(fitScale * _zoom, 0.015, 80d);
        Vector2 center = (projectedMinimum + projectedMaximum) * 0.5f;
        return new Projection(view, center, scale, Bounds.Center);
    }

    private void DrawGroundGrid(
        DrawingContext context,
        Projection projection)
    {
        float extent = 64f;
        if (_hasWorldBounds)
        {
            extent = MathF.Max(
                extent,
                MathF.Max(
                    MathF.Max(MathF.Abs(_worldMinimum.X),
                        MathF.Abs(_worldMaximum.X)),
                    MathF.Max(MathF.Abs(_worldMinimum.Z),
                        MathF.Abs(_worldMaximum.Z))));
        }

        float step = NiceGridStep(extent / 7f);
        float gridExtent = MathF.Ceiling(extent / step) * step;
        int lineCount = Math.Min(16, (int)MathF.Ceiling(gridExtent / step));
        for (int index = -lineCount; index <= lineCount; index++)
        {
            float value = index * step;
            Pen pen = index == 0
                ? GridMajorPen
                : index % 5 == 0 ? GridMajorPen : GridPen;
            context.DrawLine(
                pen,
                projection.Project(new Vector3(-gridExtent, 0f, value)).Screen,
                projection.Project(new Vector3(gridExtent, 0f, value)).Screen);
            context.DrawLine(
                pen,
                projection.Project(new Vector3(value, 0f, -gridExtent)).Screen,
                projection.Project(new Vector3(value, 0f, gridExtent)).Screen);
        }

        Point origin = projection.Project(Vector3.Zero).Screen;
        context.DrawLine(
            XAxisPen,
            origin,
            projection.Project(new Vector3(gridExtent, 0f, 0f)).Screen);
        context.DrawLine(
            ZAxisPen,
            origin,
            projection.Project(new Vector3(0f, 0f, gridExtent)).Screen);
        context.DrawLine(
            YAxisPen,
            origin,
            projection.Project(new Vector3(0f, gridExtent * 0.45f, 0f)).Screen);
    }

    private static ProjectedInstance Project(
        FxPreviewInstance instance,
        Projection projection) =>
        new(
            instance,
            projection.Project(instance.Position),
            projection.Project(VelocityEndpoint(instance)));

    private static Vector3 VelocityEndpoint(FxPreviewInstance instance)
    {
        if (!IsFinite(instance.Position) || !IsFinite(instance.Velocity))
            return instance.Position;

        Vector3 displacement = instance.Velocity * VelocityLookAheadSeconds;
        float length = displacement.Length();
        if (!float.IsFinite(length))
            return instance.Position;
        if (length > MaximumVelocityDisplacement)
        {
            displacement *= MaximumVelocityDisplacement / length;
        }

        return instance.Position + displacement;
    }

    private static void DrawVelocity(
        DrawingContext context,
        ProjectedInstance item,
        bool selected)
    {
        Point start = item.Position.Screen;
        Point end = item.VelocityEnd.Screen;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length < 4d)
            return;

        Pen pen = selected ? SelectedPen : VelocityPen;
        context.DrawLine(pen, start, end);
        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Min(7d, length * 0.35d);
        double sideX = -unitY;
        double sideY = unitX;
        Point left = new(
            end.X - unitX * headLength + sideX * headLength * 0.45d,
            end.Y - unitY * headLength + sideY * headLength * 0.45d);
        Point right = new(
            end.X - unitX * headLength - sideX * headLength * 0.45d,
            end.Y - unitY * headLength - sideY * headLength * 0.45d);
        context.DrawLine(pen, end, left);
        context.DrawLine(pen, end, right);
    }

    private static void DrawInstance(
        DrawingContext context,
        ProjectedInstance item,
        double projectionScale,
        bool selected)
    {
        FxPreviewInstance instance = item.Instance;
        Point center = item.Position.Screen;
        float visualScale = VisualScale(instance);
        float sizeX = float.IsFinite(instance.Size.X)
            ? MathF.Abs(instance.Size.X)
            : 0f;
        float sizeY = float.IsFinite(instance.Size.Y)
            ? MathF.Abs(instance.Size.Y)
            : 0f;
        double halfWidth = Math.Clamp(
            sizeX * visualScale * projectionScale,
            3d,
            72d);
        double halfHeight = Math.Clamp(
            sizeY * visualScale * projectionScale,
            3d,
            72d);
        float rotation = float.IsFinite(instance.Rotation)
            ? instance.Rotation
            : 0f;
        Pen outline = selected ? SelectedPen : ProxyPen;

        switch (instance.ElementType)
        {
            case FxElemType.SpriteBillboard:
                DrawSpriteCard(
                    context,
                    center,
                    halfWidth,
                    halfHeight,
                    rotation,
                    instance.Color,
                    selected,
                    oriented: false);
                break;
            case FxElemType.SpriteOriented:
                DrawSpriteCard(
                    context,
                    center,
                    halfWidth,
                    halfHeight,
                    rotation,
                    instance.Color,
                    selected,
                    oriented: true);
                break;
            case FxElemType.Tail:
                DrawTail(context, item, halfWidth, halfHeight, outline);
                break;
            case FxElemType.Trail:
                DrawTrail(context, item, halfWidth, outline);
                break;
            case FxElemType.Cloud:
                DrawCloud(context, center, halfWidth, halfHeight, outline);
                break;
            case FxElemType.SparkCloud:
                DrawSparkCloud(context, center, halfWidth, outline);
                break;
            case FxElemType.SparkFountain:
                DrawSparkFountain(context, center, halfWidth, outline);
                break;
            case FxElemType.Model:
                DrawModel(context, center, halfWidth, halfHeight,
                    selected ? SelectedPen : ModelPen);
                break;
            case FxElemType.OmniLight:
                DrawOmniLight(
                    context,
                    center,
                    halfWidth,
                    instance.Color,
                    selected);
                break;
            case FxElemType.SpotLight:
                DrawSpotLight(
                    context,
                    item,
                    halfWidth,
                    instance.Color,
                    selected);
                break;
            case FxElemType.Sound:
                DrawSound(context, center, halfWidth,
                    selected ? SelectedPen : SoundPen);
                break;
            case FxElemType.Decal:
                DrawDecal(context, center, halfWidth, halfHeight,
                    rotation,
                    selected ? SelectedPen : DecalPen);
                break;
            case FxElemType.Runner:
                DrawRunner(context, center, halfWidth,
                    selected ? SelectedPen : RunnerPen);
                break;
            default:
                DrawDefaultProxy(context, center, halfWidth, outline);
                break;
        }

        if (selected)
        {
            DrawSelectionBrackets(
                context,
                center,
                Math.Max(halfWidth, halfHeight) + 5d);
        }
    }

    private static void DrawSpriteCard(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        float rotation,
        Vector4 color,
        bool selected,
        bool oriented)
    {
        Point[] corners =
        [
            RotateOffset(center, -halfWidth, -halfHeight, rotation),
            RotateOffset(center, halfWidth, -halfHeight, rotation),
            RotateOffset(center, halfWidth, halfHeight, rotation),
            RotateOffset(center, -halfWidth, halfHeight, rotation)
        ];
        Color visualColor = ToColor(color);
        var fill = new SolidColorBrush(visualColor);
        Color borderColor = Color.FromArgb(
            (byte)Math.Max(110, (int)visualColor.A),
            visualColor.R,
            visualColor.G,
            visualColor.B);
        Pen border = selected
            ? SelectedPen
            : new Pen(new SolidColorBrush(borderColor), 1.35);
        context.DrawGeometry(fill, border, CreatePolygon(corners));

        if (oriented)
        {
            context.DrawLine(border, corners[0], corners[2]);
            context.DrawLine(border, corners[1], corners[3]);
        }
    }

    private static void DrawTail(
        DrawingContext context,
        ProjectedInstance item,
        double halfWidth,
        double halfHeight,
        Pen outline)
    {
        (double unitX, double unitY) = Direction(item);
        double sideX = -unitY;
        double sideY = unitX;
        double length = Math.Max(14d, halfHeight * 2d);
        double width = Math.Max(2.5d, halfWidth);
        Point center = item.Position.Screen;
        Point back = new(center.X - unitX * length, center.Y - unitY * length);
        Point[] wedge =
        [
            new(center.X + sideX * width, center.Y + sideY * width),
            new(center.X - sideX * width, center.Y - sideY * width),
            new(back.X - sideX * width * 0.18d,
                back.Y - sideY * width * 0.18d),
            new(back.X + sideX * width * 0.18d,
                back.Y + sideY * width * 0.18d)
        ];
        context.DrawGeometry(ProxyFillBrush, outline, CreatePolygon(wedge));
    }

    private static void DrawTrail(
        DrawingContext context,
        ProjectedInstance item,
        double halfWidth,
        Pen outline)
    {
        (double unitX, double unitY) = Direction(item);
        double sideX = -unitY;
        double sideY = unitX;
        Point previous = item.Position.Screen;
        double segmentLength = Math.Max(5d, halfWidth * 0.45d);
        for (int index = 1; index <= 6; index++)
        {
            double side = index % 2 == 0 ? 2.5d : -2.5d;
            Point next = new(
                item.Position.Screen.X - unitX * segmentLength * index +
                    sideX * side,
                item.Position.Screen.Y - unitY * segmentLength * index +
                    sideY * side);
            context.DrawLine(outline, previous, next);
            previous = next;
        }
        context.DrawEllipse(ProxyFillBrush, outline,
            item.Position.Screen, 3.5d, 3.5d);
    }

    private static void DrawCloud(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        Pen outline)
    {
        double radiusX = Math.Max(6d, halfWidth * 0.7d);
        double radiusY = Math.Max(4d, halfHeight * 0.65d);
        context.DrawEllipse(ProxyFillBrush, outline,
            new Point(center.X - radiusX * 0.55d, center.Y + 1d),
            radiusX * 0.7d, radiusY * 0.72d);
        context.DrawEllipse(ProxyFillBrush, outline,
            new Point(center.X + radiusX * 0.5d, center.Y + 1d),
            radiusX * 0.65d, radiusY * 0.68d);
        context.DrawEllipse(ProxyFillBrush, outline,
            new Point(center.X, center.Y - radiusY * 0.38d),
            radiusX * 0.76d, radiusY * 0.82d);
    }

    private static void DrawSparkCloud(
        DrawingContext context,
        Point center,
        double halfWidth,
        Pen outline)
    {
        double radius = Math.Max(8d, halfWidth);
        for (int index = 0; index < 8; index++)
        {
            double angle = index * Math.PI / 4d;
            Point inner = new(
                center.X + Math.Cos(angle) * 2d,
                center.Y + Math.Sin(angle) * 2d);
            Point outer = new(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius);
            context.DrawLine(outline, inner, outer);
        }
        context.DrawEllipse(ProxyFillBrush, outline, center, 3.5d, 3.5d);
    }

    private static void DrawSparkFountain(
        DrawingContext context,
        Point center,
        double halfWidth,
        Pen outline)
    {
        double height = Math.Max(12d, halfWidth * 1.4d);
        for (int index = -2; index <= 2; index++)
        {
            double spread = index * 0.22d;
            Point tip = new(
                center.X + spread * height,
                center.Y - height * (1d - Math.Abs(spread) * 0.55d));
            Point control = new(
                (center.X + tip.X) * 0.5d - spread * 5d,
                (center.Y + tip.Y) * 0.5d - 2d);
            context.DrawLine(outline, center, control);
            context.DrawLine(outline, control, tip);
            context.DrawEllipse(ProxyFillBrush, outline, tip, 1.8d, 1.8d);
        }
    }

    private static void DrawModel(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        Pen outline)
    {
        double width = Math.Max(7d, halfWidth * 0.75d);
        double height = Math.Max(7d, halfHeight * 0.75d);
        double offset = Math.Clamp(width * 0.34d, 3d, 10d);
        Point[] front = RectanglePoints(center, width, height);
        Point shiftedCenter = new(center.X + offset, center.Y - offset);
        Point[] back = RectanglePoints(shiftedCenter, width, height);
        context.DrawGeometry(ModelFillBrush, outline, CreatePolygon(front));
        context.DrawGeometry(null, outline, CreatePolygon(back));
        for (int index = 0; index < front.Length; index++)
            context.DrawLine(outline, front[index], back[index]);
    }

    private static void DrawOmniLight(
        DrawingContext context,
        Point center,
        double halfWidth,
        Vector4 visualColor,
        bool selected)
    {
        CreateLightStyle(
            visualColor,
            selected,
            out IBrush fill,
            out Pen outline);
        double radius = Math.Clamp(halfWidth * 0.75d, 7d, 24d);
        context.DrawEllipse(fill, outline, center,
            radius * 0.42d, radius * 0.42d);
        for (int index = 0; index < 8; index++)
        {
            double angle = index * Math.PI / 4d;
            Point start = new(
                center.X + Math.Cos(angle) * radius * 0.58d,
                center.Y + Math.Sin(angle) * radius * 0.58d);
            Point end = new(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius);
            context.DrawLine(outline, start, end);
        }
    }

    private static void DrawSpotLight(
        DrawingContext context,
        ProjectedInstance item,
        double halfWidth,
        Vector4 visualColor,
        bool selected)
    {
        CreateLightStyle(
            visualColor,
            selected,
            out IBrush fill,
            out Pen outline);
        (double unitX, double unitY) = Direction(item);
        double sideX = -unitY;
        double sideY = unitX;
        double length = Math.Clamp(halfWidth * 1.7d, 14d, 42d);
        double spread = length * 0.42d;
        Point center = item.Position.Screen;
        Point tip = new(center.X + unitX * length, center.Y + unitY * length);
        Point[] cone =
        [
            center,
            new(tip.X + sideX * spread, tip.Y + sideY * spread),
            new(tip.X - sideX * spread, tip.Y - sideY * spread)
        ];
        context.DrawGeometry(fill, outline, CreatePolygon(cone));
        context.DrawEllipse(fill, outline, center, 3d, 3d);
    }

    private static void DrawSound(
        DrawingContext context,
        Point center,
        double halfWidth,
        Pen outline)
    {
        double radius = Math.Clamp(halfWidth, 7d, 25d);
        context.DrawEllipse(ProxyFillBrush, outline, center, 2.5d, 2.5d);
        for (int index = 1; index <= 3; index++)
        {
            double ring = radius * index / 3d;
            context.DrawEllipse(null, outline, center, ring, ring * 0.66d);
        }
    }

    private static void DrawDecal(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        float rotation,
        Pen outline)
    {
        double width = Math.Max(7d, halfWidth);
        double height = Math.Max(5d, halfHeight * 0.6d);
        Point[] diamond =
        [
            RotateOffset(center, 0d, -height, rotation),
            RotateOffset(center, width, 0d, rotation),
            RotateOffset(center, 0d, height, rotation),
            RotateOffset(center, -width, 0d, rotation)
        ];
        context.DrawGeometry(DecalFillBrush, outline, CreatePolygon(diamond));
        context.DrawLine(outline, diamond[0], diamond[2]);
        context.DrawLine(outline, diamond[1], diamond[3]);
    }

    private static void DrawRunner(
        DrawingContext context,
        Point center,
        double halfWidth,
        Pen outline)
    {
        double radius = Math.Clamp(halfWidth * 0.75d, 7d, 18d);
        Point[] hexagon = Enumerable.Range(0, 6)
            .Select(index =>
            {
                double angle = index * Math.PI / 3d;
                return new Point(
                    center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius);
            })
            .ToArray();
        context.DrawGeometry(ProxyFillBrush, outline, CreatePolygon(hexagon));
        Point left = new(center.X - radius * 0.35d, center.Y - radius * 0.38d);
        Point middle = new(center.X + radius * 0.1d, center.Y);
        Point right = new(center.X - radius * 0.35d, center.Y + radius * 0.38d);
        context.DrawLine(outline, left, middle);
        context.DrawLine(outline, middle, right);
        context.DrawLine(outline,
            new Point(middle.X + radius * 0.3d, middle.Y),
            new Point(center.X + radius * 0.65d, center.Y));
    }

    private static void DrawDefaultProxy(
        DrawingContext context,
        Point center,
        double halfWidth,
        Pen outline)
    {
        double radius = Math.Clamp(halfWidth, 6d, 16d);
        Rect bounds = new(
            center.X - radius,
            center.Y - radius,
            radius * 2d,
            radius * 2d);
        context.DrawRectangle(ProxyFillBrush, outline, bounds);
        context.DrawLine(outline, bounds.TopLeft, bounds.BottomRight);
        context.DrawLine(outline, bounds.TopRight, bounds.BottomLeft);
    }

    private static void DrawSelectionBrackets(
        DrawingContext context,
        Point center,
        double radius)
    {
        radius = Math.Clamp(radius, 9d, 82d);
        double segment = Math.Clamp(radius * 0.32d, 4d, 11d);
        Point topLeft = new(center.X - radius, center.Y - radius);
        Point topRight = new(center.X + radius, center.Y - radius);
        Point bottomLeft = new(center.X - radius, center.Y + radius);
        Point bottomRight = new(center.X + radius, center.Y + radius);
        context.DrawLine(SelectedPen, topLeft,
            new Point(topLeft.X + segment, topLeft.Y));
        context.DrawLine(SelectedPen, topLeft,
            new Point(topLeft.X, topLeft.Y + segment));
        context.DrawLine(SelectedPen, topRight,
            new Point(topRight.X - segment, topRight.Y));
        context.DrawLine(SelectedPen, topRight,
            new Point(topRight.X, topRight.Y + segment));
        context.DrawLine(SelectedPen, bottomLeft,
            new Point(bottomLeft.X + segment, bottomLeft.Y));
        context.DrawLine(SelectedPen, bottomLeft,
            new Point(bottomLeft.X, bottomLeft.Y - segment));
        context.DrawLine(SelectedPen, bottomRight,
            new Point(bottomRight.X - segment, bottomRight.Y));
        context.DrawLine(SelectedPen, bottomRight,
            new Point(bottomRight.X, bottomRight.Y - segment));
    }

    private static (double X, double Y) Direction(ProjectedInstance item)
    {
        double dx = item.VelocityEnd.Screen.X - item.Position.Screen.X;
        double dy = item.VelocityEnd.Screen.Y - item.Position.Screen.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        float rotation = float.IsFinite(item.Instance.Rotation)
            ? item.Instance.Rotation
            : 0f;
        return double.IsFinite(length) && length >= 0.1d
            ? (dx / length, dy / length)
            : (Math.Cos(rotation), Math.Sin(rotation));
    }

    private static Point RotateOffset(
        Point center,
        double x,
        double y,
        float rotation)
    {
        double cosine = Math.Cos(rotation);
        double sine = Math.Sin(rotation);
        return new Point(
            center.X + x * cosine - y * sine,
            center.Y + x * sine + y * cosine);
    }

    private static Point[] RectanglePoints(
        Point center,
        double halfWidth,
        double halfHeight) =>
    [
        new(center.X - halfWidth, center.Y - halfHeight),
        new(center.X + halfWidth, center.Y - halfHeight),
        new(center.X + halfWidth, center.Y + halfHeight),
        new(center.X - halfWidth, center.Y + halfHeight)
    ];

    private static StreamGeometry CreatePolygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0)
            return geometry;

        using StreamGeometryContext path = geometry.Open();
        path.BeginFigure(points[0], isFilled: true);
        for (int index = 1; index < points.Count; index++)
            path.LineTo(points[index]);
        path.EndFigure(isClosed: true);
        return geometry;
    }

    private static Color ToColor(Vector4 value)
    {
        float red = float.IsFinite(value.X) ? Math.Clamp(value.X, 0f, 1f) : 1f;
        float green = float.IsFinite(value.Y) ? Math.Clamp(value.Y, 0f, 1f) : 1f;
        float blue = float.IsFinite(value.Z) ? Math.Clamp(value.Z, 0f, 1f) : 1f;
        float alpha = float.IsFinite(value.W) ? Math.Clamp(value.W, 0f, 1f) : 1f;
        return Color.FromArgb(
            (byte)Math.Round(alpha * 255f),
            (byte)Math.Round(red * 255f),
            (byte)Math.Round(green * 255f),
            (byte)Math.Round(blue * 255f));
    }

    private static void CreateLightStyle(
        Vector4 visualColor,
        bool selected,
        out IBrush fill,
        out Pen outline)
    {
        Color sampled = ToColor(visualColor);
        fill = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Min((int)sampled.A, 72),
            sampled.R,
            sampled.G,
            sampled.B));
        outline = selected
            ? SelectedPen
            : new Pen(
                new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Max((int)sampled.A, 150),
                    sampled.R,
                    sampled.G,
                    sampled.B)),
                1.45);
    }

    private static float VisualScale(FxPreviewInstance instance) =>
        instance.ElementType is
            FxElemType.Cloud or
            FxElemType.SparkCloud or
            FxElemType.Model
            ? float.IsFinite(instance.Scale)
                ? MathF.Abs(instance.Scale)
                : 1f
            : 1f;

    private static float NiceGridStep(float target)
    {
        if (!float.IsFinite(target) || target <= 0f)
            return 16f;

        float exponent = MathF.Floor(MathF.Log10(target));
        float power = MathF.Pow(10f, exponent);
        float normalized = target / power;
        float multiple = normalized <= 1f ? 1f :
            normalized <= 2f ? 2f : normalized <= 5f ? 5f : 10f;
        return multiple * power;
    }

    private static IEnumerable<Vector3> BoundsCorners(
        Vector3 minimum,
        Vector3 maximum)
    {
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    yield return new Vector3(
                        x == 0 ? minimum.X : maximum.X,
                        y == 0 ? minimum.Y : maximum.Y,
                        z == 0 ? minimum.Z : maximum.Z);
                }
            }
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private void Preview_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (_dragPointer is not null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragPointer = e.Pointer;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    private void Preview_PointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;

        Point position = e.GetPosition(this);
        Avalonia.Vector delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y))
            return;

        _yaw += (float)delta.X * 0.01f;
        _pitch = Math.Clamp(
            _pitch + (float)delta.Y * 0.01f,
            -MathF.PI * 0.47f,
            MathF.PI * 0.47f);
        InvalidateVisual();
        e.Handled = true;
    }

    private void Preview_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;

        _dragPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Preview_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e) => _dragPointer = null;

    private void Preview_PointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        double wheel = e.Delta.Y != 0d ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(wheel) || wheel == 0d)
            return;

        _zoom = Math.Clamp(
            _zoom * MathF.Exp((float)wheel * 0.14f),
            0.25f,
            10f);
        InvalidateVisual();
        e.Handled = true;
    }

    private readonly record struct ProjectedPoint(Point Screen, float Depth);

    private readonly record struct ProjectedInstance(
        FxPreviewInstance Instance,
        ProjectedPoint Position,
        ProjectedPoint VelocityEnd);

    private readonly record struct Projection(
        Matrix4x4 View,
        Vector2 Center,
        double Scale,
        Point ScreenCenter)
    {
        public ProjectedPoint Project(Vector3 value)
        {
            Vector3 rotated = Vector3.Transform(value, View);
            return new ProjectedPoint(
                new Point(
                    ScreenCenter.X + (rotated.X - Center.X) * Scale,
                    ScreenCenter.Y + (-rotated.Y - Center.Y) * Scale),
                rotated.Z);
        }
    }
}
