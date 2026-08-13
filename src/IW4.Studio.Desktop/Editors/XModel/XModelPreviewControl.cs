using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Editors.XModel;

/// <summary>
/// Avalonia host and camera input for the retained Silk.NET XModel
/// renderer in IW4.Render.OpenGl.
/// </summary>
public sealed class XModelPreviewControl : OpenGlControlBase
{
    public static readonly StyledProperty<XModelRenderScene?> SceneProperty =
        AvaloniaProperty.Register<XModelPreviewControl, XModelRenderScene?>(
            nameof(Scene));

    public static readonly StyledProperty<int> SelectedLodIndexProperty =
        AvaloniaProperty.Register<XModelPreviewControl, int>(
            nameof(SelectedLodIndex),
            -1);

    public static readonly StyledProperty<bool> ShowWireframeProperty =
        AvaloniaProperty.Register<XModelPreviewControl, bool>(
            nameof(ShowWireframe));
    public static readonly StyledProperty<bool> ShowCollisionProperty =
        AvaloniaProperty.Register<XModelPreviewControl, bool>(nameof(ShowCollision));

    public static readonly StyledProperty<bool> UseStudioEnvironmentProperty =
        AvaloniaProperty.Register<XModelPreviewControl, bool>(
            nameof(UseStudioEnvironment),
            true);

    public static readonly StyledProperty<bool> ShowBoneTagsProperty =
        AvaloniaProperty.Register<XModelPreviewControl, bool>(
            nameof(ShowBoneTags));

    private const float FieldOfView = MathF.PI / 4f;
    private SilkXModelViewerRenderer? _renderer;
    private XModelRenderLod? _uploadedLod;
    private bool _uploadRequired = true;
    private int _reportedLodIndex = -1;
    private XModelViewerUploadResult? _uploadResult;
    private string? _rendererFailure;
    private long _rendererStatusRevision;
    private Control? _cameraInput;
    private IPointer? _cameraPointer;
    private CameraDragMode _cameraDragMode;
    private Point _lastPointerPosition;
    private float _yaw = MathF.PI * 0.75f;
    private float _pitch = MathF.PI * 0.18f;
    private float _zoom = 1f;
    private Vector3 _panOffset;
    private IReadOnlyList<ProjectedBoneTag> _projectedBoneTags = [];

    public XModelPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        SizeChanged += Preview_SizeChanged;
        AttachCameraInput(this);
    }

    internal event EventHandler? ProjectedBoneTagsChanged;

    internal event EventHandler? RendererStatusChanged;

    internal IReadOnlyList<ProjectedBoneTag> ProjectedBoneTags =>
        _projectedBoneTags;

    internal int ReportedLodIndex => _reportedLodIndex;

    internal XModelViewerUploadResult? UploadResult => _uploadResult;

    internal string? RendererFailure => _rendererFailure;

    internal long RendererStatusRevision => _rendererStatusRevision;

    public XModelRenderScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public int SelectedLodIndex
    {
        get => GetValue(SelectedLodIndexProperty);
        set => SetValue(SelectedLodIndexProperty, value);
    }

    public bool ShowWireframe
    {
        get => GetValue(ShowWireframeProperty);
        set => SetValue(ShowWireframeProperty, value);
    }
    public bool ShowCollision
    {
        get => GetValue(ShowCollisionProperty);
        set => SetValue(ShowCollisionProperty, value);
    }

    public bool UseStudioEnvironment
    {
        get => GetValue(UseStudioEnvironmentProperty);
        set => SetValue(UseStudioEnvironmentProperty, value);
    }

    public bool ShowBoneTags
    {
        get => GetValue(ShowBoneTagsProperty);
        set => SetValue(ShowBoneTagsProperty, value);
    }

    public void Fit()
    {
        _yaw = MathF.PI * 0.75f;
        _pitch = MathF.PI * 0.18f;
        _zoom = 1f;
        _panOffset = Vector3.Zero;
        RefreshProjectedBoneTags();
        RequestNextFrameRendering();
    }

    internal void AttachCameraInput(Control input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (ReferenceEquals(_cameraInput, input))
            return;

        if (_cameraInput is not null)
        {
            _cameraInput.PointerPressed -= CameraInput_PointerPressed;
            _cameraInput.PointerMoved -= CameraInput_PointerMoved;
            _cameraInput.PointerReleased -= CameraInput_PointerReleased;
            _cameraInput.PointerWheelChanged -=
                CameraInput_PointerWheelChanged;
            _cameraInput.PointerCaptureLost -=
                CameraInput_PointerCaptureLost;
        }

        _cameraPointer?.Capture(null);
        _cameraPointer = null;
        _cameraDragMode = CameraDragMode.None;
        _cameraInput = input;
        _cameraInput.PointerPressed += CameraInput_PointerPressed;
        _cameraInput.PointerMoved += CameraInput_PointerMoved;
        _cameraInput.PointerReleased += CameraInput_PointerReleased;
        _cameraInput.PointerWheelChanged +=
            CameraInput_PointerWheelChanged;
        _cameraInput.PointerCaptureLost += CameraInput_PointerCaptureLost;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _renderer = new SilkXModelViewerRenderer(
                GL.GetApi(gl.GetProcAddress));
            _uploadRequired = true;
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   ArgumentException or
                   NotSupportedException)
        {
            _renderer = null;
            PublishRendererStatus(
                SelectedLodIndex,
                null,
                exception.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _uploadedLod = null;
        _uploadRequired = true;
        PublishRendererStatus(-1, null, null);
    }

    protected override void OnOpenGlLost()
    {
        _renderer = null;
        _uploadedLod = null;
        _uploadRequired = true;
        PublishRendererStatus(-1, null, null);
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_renderer is not { } renderer)
            return;
        XModelRenderLod? lod = Scene?.Lods.FirstOrDefault(candidate =>
            candidate.LodIndex == SelectedLodIndex);
        if (_uploadRequired || !ReferenceEquals(_uploadedLod, lod))
        {
            _uploadRequired = false;
            _uploadedLod = lod;
            try
            {
                XModelViewerUploadResult uploadResult =
                    renderer.Upload(lod);
                PublishRendererStatus(
                    lod?.LodIndex ?? -1,
                    uploadResult,
                    null);
            }
            catch (Exception exception) when (exception is
                       InvalidOperationException or
                       InvalidDataException or
                       ArgumentException or
                       NotSupportedException or
                       OverflowException)
            {
                PublishRendererStatus(
                    lod?.LodIndex ?? -1,
                    null,
                    exception.Message);
            }
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
        RenderCamera camera = lod is not null && lod.Bounds.IsValid
            ? CreateCamera(lod.Bounds, pixelSize)
            : CreateEmptyCamera();
        try
        {
            renderer.Render(
                framebuffer,
                pixelSize.Width,
                pixelSize.Height,
                camera,
                materialTimeSeconds: 0f,
                studioEnvironmentEnabled: UseStudioEnvironment,
                showWireframe: ShowWireframe,
                showCollision: ShowCollision);
            if (!ShowWireframe &&
                _uploadResult is not null &&
                _rendererFailure is not null)
            {
                PublishRendererStatus(
                    lod?.LodIndex ?? -1,
                    _uploadResult,
                    null);
            }
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   InvalidDataException or
                   ArgumentException or
                   NotSupportedException or
                   OverflowException)
        {
            PublishRendererStatus(
                lod?.LodIndex ?? -1,
                _uploadResult,
                exception.Message);
        }
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty ||
            change.Property == SelectedLodIndexProperty)
        {
            _uploadedLod = null;
            _uploadRequired = true;
            PublishRendererStatus(-1, null, null);
            Fit();
        }
        else if (change.Property == ShowWireframeProperty || change.Property == ShowCollisionProperty ||
                 change.Property == UseStudioEnvironmentProperty)
        {
            RequestNextFrameRendering();
        }
        else if (change.Property == ShowBoneTagsProperty)
        {
            RefreshProjectedBoneTags();
        }
    }

    private void CameraInput_PointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (sender is not Control input || _cameraPointer is not null)
            return;

        PointerPointProperties properties =
            e.GetCurrentPoint(input).Properties;
        CameraDragMode dragMode = properties.IsLeftButtonPressed
            ? CameraDragMode.Orbit
            : properties.IsRightButtonPressed ||
                properties.IsMiddleButtonPressed
                ? CameraDragMode.Pan
                : CameraDragMode.None;
        if (dragMode == CameraDragMode.None)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _cameraPointer = e.Pointer;
        _cameraDragMode = dragMode;
        _lastPointerPosition = e.GetPosition(input);
        e.Pointer.Capture(input);
        e.Handled = true;
    }

    private void CameraInput_PointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (sender is not Control input ||
            !ReferenceEquals(e.Pointer, _cameraPointer))
        {
            return;
        }

        Point position = e.GetPosition(input);
        Avalonia.Vector delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        if (_cameraDragMode == CameraDragMode.Orbit)
        {
            _yaw += (float)delta.X * 0.01f;
            _pitch = Math.Clamp(
                _pitch + (float)delta.Y * 0.01f,
                -MathF.PI * 0.48f,
                MathF.PI * 0.48f);
        }
        else if (_cameraDragMode == CameraDragMode.Pan)
        {
            Pan(delta);
        }

        RefreshProjectedBoneTags();
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void CameraInput_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _cameraPointer))
        {
            return;
        }

        _cameraPointer = null;
        _cameraDragMode = CameraDragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CameraInput_PointerWheelChanged(
        object? sender,
        PointerWheelEventArgs e)
    {
        double wheel = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(wheel) || wheel == 0)
            return;

        _zoom = Math.Clamp(
            _zoom * MathF.Exp((float)wheel * 0.14f),
            0.35f,
            12f);
        RefreshProjectedBoneTags();
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void CameraInput_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _cameraPointer = null;
        _cameraDragMode = CameraDragMode.None;
    }

    private void Preview_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        RefreshProjectedBoneTags();
        RequestNextFrameRendering();
    }

    private void Pan(Avalonia.Vector delta)
    {
        XModelRenderLod? lod = Scene?.Lods.FirstOrDefault(candidate =>
            candidate.LodIndex == SelectedLodIndex);
        if (lod is null ||
            !lod.Bounds.IsValid ||
            !double.IsFinite(delta.X) ||
            !double.IsFinite(delta.Y))
        {
            return;
        }

        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        RenderCamera camera = CreateCamera(lod.Bounds, pixelSize);
        float distance = Vector3.Distance(
            camera.Position,
            lod.Bounds.Center + _panOffset);
        float worldUnitsPerPixel =
            2f * distance * MathF.Tan(FieldOfView * 0.5f) /
            (float)Math.Max(1d, Bounds.Height);
        _panOffset += (
            camera.Right * (float)-delta.X +
            camera.Up * (float)delta.Y) * worldUnitsPerPixel;
    }

    private RenderCamera CreateCamera(
        RenderBounds bounds,
        PixelSize pixelSize)
    {
        Vector3 center = bounds.Center + _panOffset;
        float radius = MathF.Max(bounds.Radius, 0.001f);
        float aspect = MathF.Max(
            0.01f,
            pixelSize.Width / (float)Math.Max(1, pixelSize.Height));
        float distance = CalculateCameraDistance(radius, aspect);
        Vector3 eyeDirection = CreateViewDirection();
        Vector3 eye = center + eyeDirection * distance;
        Vector3 forward = -eyeDirection;
        float cameraYaw = MathF.Atan2(forward.X, -forward.Z);
        float cameraPitch = MathF.Asin(Math.Clamp(
            forward.Y,
            -1f,
            1f));
        float near = MathF.Max(
            0.0001f,
            MathF.Min(radius * 0.01f, (distance - radius) * 0.5f));
        float far = MathF.Max(near + 1f, distance + radius * 2f);
        return new RenderCamera(
            eye,
            cameraYaw,
            cameraPitch,
            FieldOfView,
            near,
            far);
    }

    private static RenderCamera CreateEmptyCamera() => new(
        new Vector3(0f, 0f, 1f),
        YawRadians: 0f,
        PitchRadians: 0f,
        FieldOfView,
        NearPlane: 0.01f,
        FarPlane: 16f);

    private float CalculateCameraDistance(float radius, float aspect)
    {
        float verticalHalfFov = FieldOfView * 0.5f;
        float horizontalHalfFov = MathF.Atan(
            MathF.Tan(verticalHalfFov) * aspect);
        float limitingHalfFov = MathF.Min(
            verticalHalfFov,
            horizontalHalfFov);
        float fittedDistance = radius /
            MathF.Max(0.01f, MathF.Sin(limitingHalfFov)) * 1.08f;
        return MathF.Max(
            radius * 1.08f,
            fittedDistance / _zoom);
    }

    private Vector3 CreateViewDirection()
    {
        float cosPitch = MathF.Cos(_pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Cos(_yaw) * cosPitch,
            MathF.Sin(_pitch),
            MathF.Sin(_yaw) * cosPitch));
    }

    private void RefreshProjectedBoneTags()
    {
        XModelRenderScene? scene = Scene;
        XModelRenderLod? lod = scene?.Lods.FirstOrDefault(candidate =>
            candidate.LodIndex == SelectedLodIndex);
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
        if (!ShowBoneTags ||
            scene is null ||
            scene.Bones.Count == 0 ||
            lod is null ||
            !lod.Bounds.IsValid ||
            pixelSize.Width <= 0 ||
            pixelSize.Height <= 0 ||
            !double.IsFinite(scaling) ||
            scaling <= 0)
        {
            SetProjectedBoneTags([]);
            return;
        }

        RenderCamera camera = CreateCamera(lod.Bounds, pixelSize);
        float aspect = pixelSize.Width /
            (float)Math.Max(1, pixelSize.Height);
        Matrix4x4 viewProjection =
            SilkXModelViewerRenderer.CreateHostViewProjection(
                camera,
                aspect);
        double logicalWidth = pixelSize.Width / scaling;
        double logicalHeight = pixelSize.Height / scaling;
        var projected = new List<ProjectedBoneTag>(scene.Bones.Count);
        foreach (XModelRenderBone bone in scene.Bones)
        {
            if (string.IsNullOrWhiteSpace(bone.Name) ||
                !IsFinite(bone.Position))
            {
                continue;
            }

            Vector4 clip = Vector4.Transform(
                new Vector4(bone.Position, 1f),
                viewProjection);
            if (!float.IsFinite(clip.X) ||
                !float.IsFinite(clip.Y) ||
                !float.IsFinite(clip.Z) ||
                !float.IsFinite(clip.W) ||
                clip.W <= 0.00001f)
            {
                continue;
            }

            float reciprocalW = 1f / clip.W;
            float x = clip.X * reciprocalW;
            float y = clip.Y * reciprocalW;
            float z = clip.Z * reciprocalW;
            if (x is < -1f or > 1f ||
                y is < -1f or > 1f ||
                z is < -1f or > 1f)
            {
                continue;
            }

            projected.Add(new ProjectedBoneTag(
                bone.Name,
                new Point(
                    (x * 0.5 + 0.5) * logicalWidth,
                    (1 - (y * 0.5 + 0.5)) * logicalHeight)));
        }
        SetProjectedBoneTags(projected);
    }

    private void SetProjectedBoneTags(
        IReadOnlyList<ProjectedBoneTag> projected)
    {
        if (_projectedBoneTags.Count == projected.Count &&
            _projectedBoneTags.Select((tag, index) =>
                    tag == projected[index])
                .All(equal => equal))
        {
            return;
        }

        _projectedBoneTags = projected;
        ProjectedBoneTagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishRendererStatus(
        int lodIndex,
        XModelViewerUploadResult? uploadResult,
        string? rendererFailure)
    {
        if (_reportedLodIndex == lodIndex &&
            string.Equals(
                _rendererFailure,
                rendererFailure,
                StringComparison.Ordinal) &&
            UploadResultsEqual(_uploadResult, uploadResult))
        {
            return;
        }

        _reportedLodIndex = lodIndex;
        _uploadResult = uploadResult;
        _rendererFailure = rendererFailure;
        _rendererStatusRevision = checked(_rendererStatusRevision + 1);
        RendererStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool UploadResultsEqual(
        XModelViewerUploadResult? left,
        XModelViewerUploadResult? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.ExecutableGroupCount == right.ExecutableGroupCount &&
        left.BlockedGroupCount == right.BlockedGroupCount &&
        left.Diagnostics.SequenceEqual(
            right.Diagnostics,
            StringComparer.Ordinal);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private enum CameraDragMode
    {
        None,
        Orbit,
        Pan
    }

    internal sealed record ProjectedBoneTag(string Name, Point Position);
}

public sealed class XModelBoneTagOverlay : Control
{
    private static readonly IBrush BoneMarkerBrush =
        new SolidColorBrush(Color.FromRgb(255, 194, 92));
    private static readonly IBrush BoneLabelBrush =
        new SolidColorBrush(Color.FromRgb(238, 242, 247));
    private static readonly IPen BoneMarkerPen = new Pen(
        new SolidColorBrush(Color.FromRgb(45, 49, 58)),
        1);
    private XModelPreviewControl? _preview;
    private IReadOnlyList<BoneTagVisual> _boneTags = [];

    internal void Attach(XModelPreviewControl preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ReferenceEquals(_preview, preview))
            return;

        if (_preview is not null)
        {
            _preview.ProjectedBoneTagsChanged -=
                Preview_ProjectedBoneTagsChanged;
        }

        _preview = preview;
        _preview.ProjectedBoneTagsChanged +=
            Preview_ProjectedBoneTagsChanged;
        RefreshBoneTags();
    }

    internal void Detach()
    {
        if (_preview is null)
            return;

        _preview.ProjectedBoneTagsChanged -=
            Preview_ProjectedBoneTagsChanged;
        _preview = null;
        _boneTags = [];
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_boneTags.Count == 0)
            return;

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            foreach (BoneTagVisual tag in _boneTags)
            {
                context.DrawEllipse(
                    BoneMarkerBrush,
                    BoneMarkerPen,
                    new Rect(
                        tag.Position.X - 3,
                        tag.Position.Y - 3,
                        6,
                        6));
                context.DrawText(
                    tag.Label,
                    new Point(
                        tag.Position.X + 6,
                        tag.Position.Y - tag.Label.Height * 0.5));
            }
        }
    }

    private void Preview_ProjectedBoneTagsChanged(
        object? sender,
        EventArgs e) => RefreshBoneTags();

    private void RefreshBoneTags()
    {
        if (_preview is null || _preview.ProjectedBoneTags.Count == 0)
        {
            _boneTags = [];
            InvalidateVisual();
            return;
        }

        var boneTags = new BoneTagVisual[
            _preview.ProjectedBoneTags.Count];
        for (int index = 0; index < boneTags.Length; index++)
        {
            XModelPreviewControl.ProjectedBoneTag projected =
                _preview.ProjectedBoneTags[index];
            var label = new FormattedText(
                projected.Name,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10,
                BoneLabelBrush)
            {
                MaxTextWidth = Math.Max(
                    1,
                    _preview.Bounds.Width - projected.Position.X - 10),
                MaxTextHeight = 18,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            boneTags[index] = new BoneTagVisual(
                projected.Position,
                label);
        }

        _boneTags = boneTags;
        InvalidateVisual();
    }

    private sealed record BoneTagVisual(
        Point Position,
        FormattedText Label);
}
