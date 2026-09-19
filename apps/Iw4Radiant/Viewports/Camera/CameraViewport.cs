using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed class CameraViewport : OpenGlControlBase, ICustomHitTest
{
    private EditorSession? _session;
    private readonly CameraNavigation _navigation = new();
    private readonly CameraFlyMovement _flyMovement;
    private readonly SceneRenderer _renderer = new();
    private CameraTransformGesture? _transform;
    private IPointer? _dragPointer;
    private MouseButton _dragButton;
    private Point _lastPointer;
    private Point _pressPoint;
    private bool _panning;
    private bool _navigationMoved;
    private readonly HashSet<object> _painted = new(ReferenceEqualityComparer.Instance);
    private bool? _paintSelecting;
    private ContextMenu? _objectMenu;
    private bool _previewLighting = true;
    private bool _flyMode;

    public CameraViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        _flyMovement = new CameraFlyMovement(this, _navigation);
        _renderer.StatusChanged += (_, _) => RendererStatusChanged?.Invoke(this, EventArgs.Empty);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => { if (_dragPointer is not null) FinishGesture(cancel: true); };
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (_, e) => HandleNavigationKeyDown(e);
        KeyUp += (_, e) => _flyMovement.KeyUp(e);
        LostFocus += (_, _) => FinishGesture(cancel: true);
        SizeChanged += (_, _) => { FinishGesture(cancel: true); RequestNextFrameRendering(); };
        DetachedFromVisualTree += (_, _) => { FinishGesture(cancel: true); _objectMenu?.Close(); };
    }

    internal EditorSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            FinishGesture(cancel: true);
            _objectMenu?.Close();
            _session = value;
            RefreshScene();
        }
    }

    internal bool PreviewLighting
    {
        get => _previewLighting;
        set { _previewLighting = value; RequestNextFrameRendering(); }
    }
    internal bool FlyMode
    {
        get => _flyMode;
        set
        {
            if (_flyMode == value) return;
            FinishGesture(cancel: true);
            _flyMode = value;
            NavigationModeChanged?.Invoke();
        }
    }
    internal Func<string, MaterialSource?>? ResolveMaterial { get; set; }
    internal string? RendererError => _renderer.Error;
    internal event EventHandler? RendererStatusChanged;
    internal event Action<string>? InteractionStatusChanged;
    internal event Action? NavigationModeChanged;
    internal event Action<BrushKind>? BrushKindRequested;
    internal bool HasPointerGesture => _dragPointer is not null;
    internal bool CanFlyMove => FlyMode || _dragPointer is not null && _dragButton == MouseButton.Right;

    internal bool HandleNavigationKeyDown(KeyEventArgs e)
    {
        if (!IsFocused || !IsEffectivelyVisible || !IsEffectivelyEnabled) return false;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) != 0)
        {
            _flyMovement.Stop();
            return false;
        }
        if (e.Key == Key.Escape && (_dragPointer is not null || FlyMode))
        {
            FinishGesture(cancel: true);
            FlyMode = false;
            InteractionStatusChanged?.Invoke("Camera orbit mode. Right-drag to orbit; Shift+right-drag to pan; hold right mouse and use WASD to move.");
            e.Handled = true;
            return true;
        }
        return CanFlyMove && _session is not null && _flyMovement.KeyDown(e, canMove: _transform is null);
    }

    internal void FlyMovementApplied() => _navigationMoved = true;

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
        if (_session is { } session) _navigation.FrameBounds(session.Scene.VisibleBounds, Aspect);
        RequestNextFrameRendering();
    }

    internal void FrameSelection()
    {
        FinishGesture();
        if (_session is { } session) _navigation.FrameBounds(session.SelectionBounds ?? session.Scene.VisibleBounds, Aspect);
        RequestNextFrameRendering();
    }

    internal void FinishGesture(bool cancel = false)
    {
        _flyMovement.Stop();
        FinishPointerGesture(cancel);
    }

    private void FinishPointerGesture(bool cancel = false)
    {
        if (!FlyMode) _flyMovement.Stop();
        var transform = _transform;
        var pointer = _dragPointer;
        _transform = null;
        _dragPointer = null;
        _painted.Clear();
        _paintSelecting = null;
        // Clear ownership before releasing capture or refreshing the editor: either
        // operation can synchronously reenter this control.
        pointer?.Capture(null);
        transform?.Complete(cancel);
    }

    private float Aspect => (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height));

    // OpenGL is presented through a composition surface, which supplies no control hit area.
    bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnOpenGlInit(GlInterface gl) => _renderer.Initialize(gl);
    protected override void OnOpenGlDeinit(GlInterface gl) => _renderer.ReleaseResources();
    protected override void OnOpenGlLost() => _renderer.ContextLost();

    protected override void OnOpenGlRender(GlInterface glInterface, int framebuffer)
    {
        if (_session is not { } session) return;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
        _renderer.Render(size, framebuffer, _navigation.ViewProjection((float)size.Width / size.Height), _navigation.Eye,
            session, ResolveMaterial, PreviewLighting);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is not { } session) return;
        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        var properties = e.GetCurrentPoint(this).Properties;
        Point point = e.GetPosition(this);
        if (properties.PointerUpdateKind is PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed)
        {
            FinishPointerGesture(cancel: true);
            _objectMenu?.Close();
            _dragPointer = e.Pointer;
            _lastPointer = _pressPoint = point;
            _navigationMoved = false;
            bool middle = properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed;
            _panning = middle || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _dragButton = middle ? MouseButton.Middle : MouseButton.Right;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed || _dragPointer is not null) return;
        _flyMovement.Stop();
        try
        {
            if (session.HasPlacement)
            {
                var (origin, direction) = _navigation.PickRay((float)(point.X / Math.Max(1, Bounds.Width) * 2 - 1),
                    (float)(1 - point.Y / Math.Max(1, Bounds.Height) * 2), Aspect);
                if (SurfaceRaycast.TryHit(session.Scene.Document, origin, direction,
                        name => session.Scene.ResolveModel?.Invoke(name), ResolveMaterial, null,
                        out Vector3 hit, out Vector3 normal) && CameraPicking.InCubicClip(session, hit, origin))
                {
                    string? label = session.PlacementLabel;
                    session.Place(hit, normal, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    InteractionStatusChanged?.Invoke($"Placed {label}." + (session.HasPlacement ? " Click to place another; Esc cancels." : ""));
                }
                else InteractionStatusChanged?.Invoke("Click an existing surface to place here, or click a grid view to use the grid plane.");
                e.Handled = true;
                return;
            }
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            object? vertex = session.Tool == EditorTool.Vertex
                ? CameraPicking.PickVertex(session, _navigation, point, Bounds.Size) : null;
            int axis = !additive && vertex is null && (session.Tool is EditorTool.Select or EditorTool.Vertex) && session.CanTransformSelection &&
                session.SelectionBounds is { } bounds
                ? CameraPicking.PickGizmo(session, bounds, session.TransformMode, _navigation, point, Bounds.Size) : 0;
            if (axis != 0)
            {
                _transform = new CameraTransformGesture(session, _navigation, axis, point, Bounds.Size);
                _dragPointer = e.Pointer;
                _dragButton = MouseButton.Left;
                e.Pointer.Capture(this);
                InteractionStatusChanged?.Invoke("Drag the handle to transform. Escape cancels.");
            }
            else if (additive)
            {
                object? picked = vertex ?? CameraPicking.Pick(session, _navigation, point, Bounds.Size, session.Tool);
                if (session.Tool == EditorTool.Select)
                {
                    _dragPointer = e.Pointer;
                    _dragButton = MouseButton.Left;
                    e.Pointer.Capture(this);
                    PaintSelection(session, picked);
                }
                else if (picked is not null) session.Select(picked, additive: true, toggle: true);
                if (session.Tool == EditorTool.Vertex && picked is not null)
                    InteractionStatusChanged?.Invoke(picked is BrushVertexSelection or TerrainVertexSelection
                        ? "Drag a transform handle; Shift-click toggles vertices."
                        : "Shift-click a vertex handle to toggle its selection.");
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
        else if (_dragButton == MouseButton.Left)
            UpdateSelectionPaint(position);
        else
        {
            Avalonia.Vector fromPress = position - _pressPoint;
            if (!_navigationMoved && fromPress.SquaredLength < 16) return;
            _navigationMoved = true;
            Avalonia.Vector delta = position - _lastPointer;
            _lastPointer = position;
            if (_panning) _navigation.Pan((float)delta.X, (float)delta.Y, (float)Math.Max(1, Bounds.Height));
            else if (FlyMode) _navigation.Look((float)delta.X, (float)delta.Y);
            else _navigation.Orbit((float)delta.X, (float)delta.Y);
            RequestNextFrameRendering();
        }
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer) || e.InitialPressMouseButton != _dragButton) return;
        Point point = e.GetPosition(this);
        Avalonia.Vector fromPress = point - _pressPoint;
        bool showMenu = _dragButton == MouseButton.Right && !_navigationMoved && fromPress.SquaredLength < 16;
        if (_transform is { } transform) UpdateTransform(transform, point);
        else if (_dragButton == MouseButton.Left) UpdateSelectionPaint(point);
        FinishPointerGesture();
        if (showMenu && _session is { } session)
        {
            try
            {
                _objectMenu = CameraObjectMenu.Open(this, session,
                    CameraPicking.PickAll(session, _navigation, point, Bounds.Size, EditorTool.Select),
                    kind => BrushKindRequested?.Invoke(kind));
            }
            catch (Exception exception) when (IsEditError(exception))
            {
                InteractionStatusChanged?.Invoke(exception.Message);
            }
        }
        e.Handled = true;
    }

    private void PaintSelection(EditorSession session, object? picked)
    {
        if (picked is null || !_painted.Add(picked)) return;
        bool selected = session.Selection.Contains(picked);
        _paintSelecting ??= !selected;
        if (selected != _paintSelecting.Value) session.Select(picked, additive: true, toggle: true);
    }

    private void UpdateSelectionPaint(Point point)
    {
        if (_session is not { Tool: EditorTool.Select } session) return;
        try
        {
            PaintSelection(session, CameraPicking.Pick(session, _navigation, point, Bounds.Size, session.Tool));
        }
        catch (Exception exception) when (IsEditError(exception))
        {
            FinishGesture(cancel: true);
            InteractionStatusChanged?.Invoke(exception.Message);
        }
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
        if (!double.IsFinite(delta) || delta == 0) return;
        if (_dragPointer is not null) _navigationMoved = true;
        if (FlyMode) _navigation.MoveLocal(0, (float)delta * 32, 0);
        else _navigation.Zoom((float)delta);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private static bool IsEditError(Exception exception) => exception is ArgumentException or FormatException or InvalidOperationException or IOException;
}
