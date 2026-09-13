using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed class CameraViewport : OpenGlControlBase
{
    private EditorSession? _session;
    private readonly CameraNavigation _navigation = new();
    private readonly SceneRenderer _renderer = new();
    private CameraTransformGesture? _transform;
    private IPointer? _dragPointer;
    private MouseButton _dragButton;
    private Point _lastPointer;
    private bool _panning;
    private bool _previewLighting = true;

    public CameraViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        _renderer.StatusChanged += (_, _) => RendererStatusChanged?.Invoke(this, EventArgs.Empty);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => FinishGesture(cancel: true);
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _dragPointer is not null)
            {
                FinishGesture(cancel: true);
                InteractionStatusChanged?.Invoke("Gesture cancelled.");
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                FinishGesture();
                FrameSelection();
                e.Handled = true;
            }
        };
        SizeChanged += (_, _) => { FinishGesture(cancel: true); RequestNextFrameRendering(); };
        DetachedFromVisualTree += (_, _) => FinishGesture(cancel: true);
    }

    internal EditorSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            FinishGesture(cancel: true);
            _session = value;
            RefreshScene();
        }
    }

    internal bool PreviewLighting
    {
        get => _previewLighting;
        set { _previewLighting = value; RequestNextFrameRendering(); }
    }
    internal Func<string, string?>? ResolveTexturePath { get; set; }
    internal string? RendererError => _renderer.Error;
    internal event EventHandler? RendererStatusChanged;
    internal event Action<string>? InteractionStatusChanged;

    internal void RefreshScene()
    {
        if (_transform is { IsCurrent: false }) FinishGesture(cancel: true);
        _renderer.RefreshScene();
        RequestNextFrameRendering();
    }

    internal void ReloadTextures()
    {
        _renderer.ReloadTextures();
        RequestNextFrameRendering();
    }

    internal void FrameAll()
    {
        FinishGesture();
        if (_session is { } session) _navigation.FrameAll(session.Document, Aspect);
        RequestNextFrameRendering();
    }

    internal void FrameSelection()
    {
        FinishGesture();
        if (_session is { } session) _navigation.FrameSelection(session.Document, session.Selection, Aspect);
        RequestNextFrameRendering();
    }

    internal void FinishGesture(bool cancel = false)
    {
        var transform = _transform;
        var pointer = _dragPointer;
        _transform = null;
        _dragPointer = null;
        // Clear ownership before releasing capture or refreshing the editor: either
        // operation can synchronously reenter this control.
        pointer?.Capture(null);
        transform?.Complete(cancel);
    }

    private float Aspect => (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height));

    protected override void OnOpenGlInit(GlInterface gl) => _renderer.Initialize(gl);
    protected override void OnOpenGlDeinit(GlInterface gl) => _renderer.ReleaseResources();
    protected override void OnOpenGlLost() => _renderer.ContextLost();

    protected override void OnOpenGlRender(GlInterface glInterface, int framebuffer)
    {
        if (_session is not { } session) return;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
        _renderer.Render(size, framebuffer, _navigation.ViewProjection((float)size.Width / size.Height),
            session.Document, session.Selection, session.TransformMode, session.Tool, ResolveTexturePath, PreviewLighting);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is not { } session) return;
        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        var properties = e.GetCurrentPoint(this).Properties;
        Point point = e.GetPosition(this);
        if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
        {
            FinishGesture(cancel: true);
            _dragPointer = e.Pointer;
            _lastPointer = point;
            _panning = properties.IsMiddleButtonPressed;
            _dragButton = _panning ? MouseButton.Middle : MouseButton.Right;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed || _dragPointer is not null) return;
        try
        {
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            object? vertex = session.Tool == EditorTool.Vertex
                ? CameraPicking.PickVertex(session.Selection, _navigation, point, Bounds.Size) : null;
            int axis = !additive && vertex is null && (session.Tool is EditorTool.Select or EditorTool.Vertex) && session.CanTransformSelection &&
                session.SelectionBounds is { } bounds
                ? CameraPicking.PickGizmo(bounds, session.TransformMode, _navigation, point, Bounds.Size) : 0;
            if (axis != 0)
            {
                _transform = new CameraTransformGesture(session, _navigation, axis, point, Bounds.Size);
                _dragPointer = e.Pointer;
                _dragButton = MouseButton.Left;
                e.Pointer.Capture(this);
                InteractionStatusChanged?.Invoke("Drag the handle to transform. Escape cancels.");
            }
            else
            {
                object? picked = vertex ?? CameraPicking.Pick(session.Document, _navigation, point, Bounds.Size, session.Tool);
                session.Select(picked, additive, toggle: additive);
                if (session.Tool == EditorTool.Vertex && picked is not null)
                    InteractionStatusChanged?.Invoke(picked is BrushVertexSelection or TerrainVertexSelection
                        ? "Vertex selected. Drag a transform handle; Shift-click adds vertices."
                        : "Click a vertex handle to select it; Shift-click adds vertices.");
            }
        }
        catch (Exception exception) when (IsEditError(exception))
        {
            FinishGesture(cancel: true);
            InteractionStatusChanged?.Invoke(exception.Message);
        }
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer)) return;
        Point position = e.GetPosition(this);
        if (_transform is { } transform)
            UpdateTransform(transform, position);
        else
        {
            Avalonia.Vector delta = position - _lastPointer;
            _lastPointer = position;
            if (_panning) _navigation.Pan((float)delta.X, (float)delta.Y, (float)Math.Max(1, Bounds.Height));
            else _navigation.Orbit((float)delta.X, (float)delta.Y);
            RequestNextFrameRendering();
        }
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer) || e.InitialPressMouseButton != _dragButton) return;
        if (_transform is { } transform) UpdateTransform(transform, e.GetPosition(this));
        FinishGesture();
        e.Handled = true;
    }

    private void UpdateTransform(CameraTransformGesture transform, Point position)
    {
        try
        {
            if (transform.Update(position, Bounds.Size) is { } status) InteractionStatusChanged?.Invoke(status);
        }
        catch (Exception exception) when (IsEditError(exception))
        {
            FinishGesture(cancel: true);
            InteractionStatusChanged?.Invoke(exception.Message);
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_transform is not null) { e.Handled = true; return; }
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(delta)) return;
        _navigation.Zoom((float)delta);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private static bool IsEditError(Exception exception) => exception is ArgumentException or FormatException or InvalidOperationException;
}
