using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Editors.XModel;

/// <summary>
/// Avalonia host and orbit-camera input for the retained Silk.NET XModel
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

    private const float FieldOfView = MathF.PI / 4f;
    private SilkXModelViewerRenderer? _renderer;
    private XModelRenderLod? _uploadedLod;
    private IPointer? _orbitPointer;
    private Point _lastPointerPosition;
    private float _yaw = MathF.PI * 0.75f;
    private float _pitch = MathF.PI * 0.18f;
    private float _zoom = 1f;

    public XModelPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerCaptureLost += Preview_PointerCaptureLost;
    }

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

    public void Fit()
    {
        _yaw = MathF.PI * 0.75f;
        _pitch = MathF.PI * 0.18f;
        _zoom = 1f;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl) =>
        _renderer = new SilkXModelViewerRenderer(
            GL.GetApi(gl.GetProcAddress));

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _uploadedLod = null;
    }

    protected override void OnOpenGlLost()
    {
        _renderer = null;
        _uploadedLod = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        SilkXModelViewerRenderer renderer = _renderer
            ?? throw new InvalidOperationException(
                "The XModel OpenGL renderer was not initialized.");
        XModelRenderLod? lod = Scene?.Lods.FirstOrDefault(candidate =>
            candidate.LodIndex == SelectedLodIndex);
        if (!ReferenceEquals(_uploadedLod, lod))
        {
            renderer.Upload(lod);
            _uploadedLod = lod;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
        Matrix4x4 viewProjection = lod is not null && lod.Bounds.IsValid
            ? CreateViewProjection(lod.Bounds, pixelSize)
            : Matrix4x4.Identity;
        renderer.Render(
            framebuffer,
            pixelSize.Width,
            pixelSize.Height,
            viewProjection,
            ShowWireframe);
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty ||
            change.Property == SelectedLodIndexProperty)
        {
            _uploadedLod = null;
            Fit();
        }
        else if (change.Property == ShowWireframeProperty)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _orbitPointer = e.Pointer;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer, _orbitPointer))
            return;

        Point position = e.GetPosition(this);
        Avalonia.Vector delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        _yaw += (float)delta.X * 0.01f;
        _pitch = Math.Clamp(
            _pitch + (float)delta.Y * 0.01f,
            -MathF.PI * 0.48f,
            MathF.PI * 0.48f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left ||
            !ReferenceEquals(e.Pointer, _orbitPointer))
        {
            return;
        }

        _orbitPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double wheel = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        _zoom = Math.Clamp(
            _zoom * MathF.Exp((float)wheel * 0.14f),
            0.35f,
            4f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void Preview_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e) => _orbitPointer = null;

    private Matrix4x4 CreateViewProjection(
        MapRenderBounds bounds,
        PixelSize pixelSize)
    {
        Vector3 center = bounds.Center;
        float radius = MathF.Max(bounds.Radius, 0.001f);
        float aspect = MathF.Max(
            0.01f,
            pixelSize.Width / (float)Math.Max(1, pixelSize.Height));
        float verticalHalfFov = FieldOfView * 0.5f;
        float horizontalHalfFov = MathF.Atan(
            MathF.Tan(verticalHalfFov) * aspect);
        float limitingHalfFov = MathF.Min(
            verticalHalfFov,
            horizontalHalfFov);
        float fittedDistance = radius /
            MathF.Max(0.01f, MathF.Sin(limitingHalfFov)) * 1.08f;
        float distance = MathF.Max(
            radius * 1.08f,
            fittedDistance / _zoom);
        float cosPitch = MathF.Cos(_pitch);
        var offset = new Vector3(
            MathF.Cos(_yaw) * cosPitch,
            MathF.Sin(_pitch),
            MathF.Sin(_yaw) * cosPitch) * distance;
        Vector3 eye = center + offset;
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            eye,
            center,
            Vector3.UnitY);
        float near = MathF.Max(
            0.0001f,
            MathF.Min(radius * 0.01f, (distance - radius) * 0.5f));
        float far = MathF.Max(near + 1f, distance + radius * 2f);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfView,
            aspect,
            near,
            far);
        return view * projection;
    }
}
