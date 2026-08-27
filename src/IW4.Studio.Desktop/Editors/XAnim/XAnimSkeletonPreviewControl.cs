using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IW4.Render.EditorPreview;

namespace IW4.Studio.Desktop.Editors.XAnim;

public sealed class XAnimSkeletonPreviewControl : Control
{
    public static readonly StyledProperty<XAnimPreviewScene?> SceneProperty =
        AvaloniaProperty.Register<XAnimSkeletonPreviewControl, XAnimPreviewScene?>(
            nameof(Scene));

    public static readonly StyledProperty<XAnimPreviewPose?> PoseProperty =
        AvaloniaProperty.Register<XAnimSkeletonPreviewControl, XAnimPreviewPose?>(
            nameof(Pose));

    private static readonly IBrush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(17, 20, 25));
    private static readonly Pen GridPen =
        new(new SolidColorBrush(Color.FromArgb(28, 151, 161, 175)), 1);
    private static readonly Pen StaticBonePen =
        new(new SolidColorBrush(Color.FromRgb(106, 119, 134)), 1.35);
    private static readonly Pen AnimatedBonePen =
        new(new SolidColorBrush(Color.FromRgb(89, 214, 132)), 2.1);
    private static readonly IBrush StaticJointBrush =
        new SolidColorBrush(Color.FromRgb(156, 168, 181));
    private static readonly IBrush AnimatedJointBrush =
        new SolidColorBrush(Color.FromRgb(111, 232, 151));
    private static readonly Pen JointOutlinePen =
        new(new SolidColorBrush(Color.FromRgb(19, 46, 29)), 1);
    private static readonly Matrix4x4 PreviewRotation =
        Matrix4x4.CreateRotationY(-0.62f) *
        Matrix4x4.CreateRotationX(-0.18f);

    private bool _hasViewBounds;
    private Vector2 _viewMinimum;
    private Vector2 _viewMaximum;

    static XAnimSkeletonPreviewControl() =>
        AffectsRender<XAnimSkeletonPreviewControl>(SceneProperty, PoseProperty);

    public XAnimSkeletonPreviewControl() => ClipToBounds = true;

    public XAnimPreviewScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public XAnimPreviewPose? Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, Bounds);
        DrawGrid(context);

        if (Pose?.Bones is not { Count: > 0 } bones ||
            Bounds.Width <= 1 ||
            Bounds.Height <= 1)
        {
            return;
        }

        ProjectedBone[] projected = ProjectBones(bones);
        ExpandViewBounds(projected);
        Projection projection = CreateProjection();

        for (int index = 0; index < projected.Length; index++)
        {
            XAnimPreviewBone bone = bones[index];
            if (bone.ParentIndex < 0 || bone.ParentIndex >= projected.Length)
                continue;

            bool animated = bone.IsAnimated ||
                bones[bone.ParentIndex].IsAnimated;
            context.DrawLine(
                animated ? AnimatedBonePen : StaticBonePen,
                projection.ToScreen(projected[bone.ParentIndex].Position),
                projection.ToScreen(projected[index].Position));
        }

        foreach (int index in Enumerable.Range(0, projected.Length)
                     .OrderBy(candidate => projected[candidate].Depth))
        {
            XAnimPreviewBone bone = bones[index];
            Point center = projection.ToScreen(projected[index].Position);
            double radius = bone.IsAnimated ? 3.4 : 2.5;
            context.DrawEllipse(
                bone.IsAnimated ? AnimatedJointBrush : StaticJointBrush,
                JointOutlinePen,
                center,
                radius,
                radius);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
            _hasViewBounds = false;
    }

    private void DrawGrid(DrawingContext context)
    {
        const double spacing = 32;
        double centerX = Bounds.Width * 0.5;
        double centerY = Bounds.Height * 0.5;
        double startX = centerX % spacing;
        double startY = centerY % spacing;
        for (double x = startX; x < Bounds.Width; x += spacing)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = startY; y < Bounds.Height; y += spacing)
            context.DrawLine(GridPen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private static ProjectedBone[] ProjectBones(
        IReadOnlyList<XAnimPreviewBone> bones)
    {
        var result = new ProjectedBone[bones.Count];
        for (int index = 0; index < bones.Count; index++)
        {
            Vector3 rotated = Vector3.Transform(
                bones[index].Position,
                PreviewRotation);
            result[index] = IsFinite(rotated)
                ? new ProjectedBone(new Vector2(rotated.X, -rotated.Y), rotated.Z)
                : default;
        }
        return result;
    }

    private void ExpandViewBounds(IReadOnlyList<ProjectedBone> bones)
    {
        Vector2 minimum = new(float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity);
        foreach (ProjectedBone bone in bones)
        {
            if (!IsFinite(bone.Position))
                continue;
            minimum = Vector2.Min(minimum, bone.Position);
            maximum = Vector2.Max(maximum, bone.Position);
        }

        if (!IsFinite(minimum) || !IsFinite(maximum))
            return;

        Vector2 size = maximum - minimum;
        Vector2 padding = new(
            MathF.Max(1.0f, size.X * 0.08f),
            MathF.Max(1.0f, size.Y * 0.08f));
        minimum -= padding;
        maximum += padding;
        if (!_hasViewBounds)
        {
            _viewMinimum = minimum;
            _viewMaximum = maximum;
            _hasViewBounds = true;
            return;
        }

        _viewMinimum = Vector2.Min(_viewMinimum, minimum);
        _viewMaximum = Vector2.Max(_viewMaximum, maximum);
    }

    private Projection CreateProjection()
    {
        if (!_hasViewBounds)
            return new Projection(Vector2.Zero, 1.0, Bounds.Center);

        Vector2 size = _viewMaximum - _viewMinimum;
        double availableWidth = Math.Max(1.0, Bounds.Width - 64.0);
        double availableHeight = Math.Max(1.0, Bounds.Height - 64.0);
        double scale = Math.Min(
            availableWidth / Math.Max(1.0, size.X),
            availableHeight / Math.Max(1.0, size.Y));
        scale = Math.Clamp(scale, 0.01, 48.0);
        Vector2 center = (_viewMinimum + _viewMaximum) * 0.5f;
        return new Projection(center, scale, Bounds.Center);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y);

    private readonly record struct ProjectedBone(Vector2 Position, float Depth);

    private readonly record struct Projection(
        Vector2 Center,
        double Scale,
        Point ScreenCenter)
    {
        public Point ToScreen(Vector2 point) => new(
            ScreenCenter.X + (point.X - Center.X) * Scale,
            ScreenCenter.Y + (point.Y - Center.Y) * Scale);
    }
}
