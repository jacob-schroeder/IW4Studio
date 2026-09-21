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
    private bool _foliagePaintingEnabled, _paintingFoliage, _foliageChanged;
    private MapDocument? _foliageSurfaceDocument;
    private Vector3? _lastFoliageStamp;
    private readonly List<(MapEntity Entity, XModelSource Model)> _foliagePreview = [];

    public CameraViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        _flyMovement = new CameraFlyMovement(this, _navigation);
        _renderer.StatusChanged += (_, _) => RendererStatusChanged?.Invoke(this, EventArgs.Empty);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) => { if (!_paintingFoliage) FoliageBrushChanged?.Invoke(null); };
        PointerCaptureLost += (_, _) => { if (_dragPointer is not null) FinishGesture(cancel: true); };
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (_, e) => HandleNavigationKeyDown(e);
        KeyUp += (_, e) => _flyMovement.KeyUp(e);
        LostFocus += (_, _) => FinishGesture(cancel: true);
        SizeChanged += (_, _) => { FinishGesture(cancel: true); RequestNextFrameRendering(); };
        DetachedFromVisualTree += (_, _) => { FinishGesture(cancel: true); _objectMenu?.Close(); };
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnModelDragOver);
        DragDrop.AddDropHandler(this, OnModelDrop);
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
    internal Func<bool>? CanAcceptModelDrop { get; set; }
    internal IReadOnlyList<XModelSource> FoliageModels { get; set; } = [];
    internal float FoliageRadius { get; set; } = 64;
    internal int FoliageDensity { get; set; } = 1;
    internal float FoliageSpacing { get; set; } = 32;
    internal float FoliageMinimumScale { get; set; } = 0.8f;
    internal float FoliageMaximumScale { get; set; } = 1.2f;
    internal bool FoliageRandomYaw { get; set; } = true;
    internal bool FoliageAlignSurface { get; set; } = true;
    internal bool FoliagePaintingEnabled
    {
        get => _foliagePaintingEnabled;
        set
        {
            if (_foliagePaintingEnabled == value) return;
            if (_paintingFoliage) FinishGesture(cancel: true);
            _foliagePaintingEnabled = value;
            if (!value) FoliageBrushChanged?.Invoke(null);
        }
    }
    internal string? RendererError => _renderer.Error;
    internal event EventHandler? RendererStatusChanged;
    internal event Action<string>? InteractionStatusChanged;
    internal event Action? NavigationModeChanged;
    internal event Action<BrushKind>? BrushKindRequested;
    internal event Action<IReadOnlyList<Point>?>? FoliageBrushChanged;
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
        bool paintingFoliage = _paintingFoliage;
        bool foliageChanged = _foliageChanged;
        _transform = null;
        _dragPointer = null;
        _paintingFoliage = _foliageChanged = false;
        _foliageSurfaceDocument = null;
        _lastFoliageStamp = null;
        _foliagePreview.Clear();
        _renderer.SetFoliagePreview(_foliagePreview);
        _painted.Clear();
        _paintSelecting = null;
        // Clear ownership before releasing capture or refreshing the editor: either
        // operation can synchronously reenter this control.
        pointer?.Capture(null);
        transform?.Complete(cancel);
        if (paintingFoliage && _session is { } session)
        {
            if (cancel) session.CancelEdit();
            else session.CompleteEdit(foliageChanged);
            InteractionStatusChanged?.Invoke(cancel ? "Foliage stroke cancelled." :
                foliageChanged ? "Foliage stroke completed." : "No foliage was placed.");
        }
    }

    private float Aspect => (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height));

    // OpenGL is presented through a composition surface, which supplies no control hit area.
    bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnOpenGlInit(GlInterface gl) => _renderer.Initialize(gl);
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        FinishGesture(cancel: true);
        _renderer.ReleaseResources();
    }
    protected override void OnOpenGlLost()
    {
        FinishGesture(cancel: true);
        _renderer.ContextLost();
    }

    protected override void OnOpenGlRender(GlInterface glInterface, int framebuffer)
    {
        if (_session is not { } session) return;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
        _renderer.Render(size, framebuffer, _navigation.ViewProjection((float)size.Width / size.Height), _navigation.Eye,
            session, ResolveMaterial, PreviewLighting);
        if (_renderer.HasAnimatedWater)
            RequestNextFrameRendering();
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
            if (FoliagePaintingEnabled && e.KeyModifiers == KeyModifiers.None)
            {
                BeginFoliageStroke(session, e.Pointer, point);
                e.Handled = true;
                return;
            }
            if (session.HasPlacement)
            {
                if (TryMapHit(point, includeModels: true, out Vector3 hit, out Vector3 normal))
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
        try
        {
            Point position = e.GetPosition(this);
            UpdateFoliageBrush(position);
            if (!ReferenceEquals(e.Pointer, _dragPointer)) return;
            if (_paintingFoliage)
                ContinueFoliageStroke(position);
            else if (_transform is { } transform)
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
        catch (Exception exception) when (IsEditError(exception))
        {
            FinishGesture(cancel: true);
            FoliageBrushChanged?.Invoke(null);
            InteractionStatusChanged?.Invoke(exception.Message);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer) || e.InitialPressMouseButton != _dragButton) return;
        try
        {
            Point point = e.GetPosition(this);
            Avalonia.Vector fromPress = point - _pressPoint;
            bool showMenu = _dragButton == MouseButton.Right && !_navigationMoved && fromPress.SquaredLength < 16;
            if (_paintingFoliage) ContinueFoliageStroke(point);
            else if (_transform is { } transform) UpdateTransform(transform, point);
            else if (_dragButton == MouseButton.Left) UpdateSelectionPaint(point);
            FinishPointerGesture();
            if (showMenu && _session is { } session)
            {
                _objectMenu = CameraObjectMenu.Open(this, session,
                    CameraPicking.PickAll(session, _navigation, point, Bounds.Size, EditorTool.Select),
                    kind => BrushKindRequested?.Invoke(kind));
            }
        }
        catch (Exception exception) when (IsEditError(exception))
        {
            FinishGesture(cancel: true);
            InteractionStatusChanged?.Invoke(exception.Message);
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

    private void BeginFoliageStroke(EditorSession session, IPointer pointer, Point point)
    {
        if (FoliageModels.Count == 0)
        {
            InteractionStatusChanged?.Invoke("Add one or more models to the foliage palette first.");
            return;
        }
        _foliageSurfaceDocument = session.Scene.Document;
        if (!TryMapHit(point, includeModels: false, out Vector3 hit, out Vector3 normal))
        {
            _foliageSurfaceDocument = null;
            InteractionStatusChanged?.Invoke("Point at an existing map surface to paint foliage.");
            return;
        }
        session.BeginEdit();
        _paintingFoliage = true;
        _foliageChanged = false;
        _lastFoliageStamp = null;
        _dragPointer = pointer;
        _dragButton = MouseButton.Left;
        _pressPoint = _lastPointer = point;
        pointer.Capture(this);
        if (StampFoliage(session, hit, normal)) RequestNextFrameRendering();
    }

    private void ContinueFoliageStroke(Point point)
    {
        if (!_paintingFoliage || _session is not { } session) return;
        if (!TryMapHit(point, includeModels: false, out Vector3 hit, out Vector3 normal))
        {
            _lastFoliageStamp = null;
            return;
        }
        if (_lastFoliageStamp is not { } previous)
        {
            if (StampFoliage(session, hit, normal)) RequestNextFrameRendering();
            return;
        }
        float spacing = Math.Max(1, FoliageSpacing);
        Vector3 delta = hit - previous;
        float distance = delta.Length();
        if (distance < spacing) return;
        Vector3 direction = delta / distance;
        int stamps = Math.Min(32, (int)(distance / spacing));
        bool changed = false;
        for (int index = 0; index < stamps; index++)
            changed |= StampFoliage(session, previous + direction * spacing * (index + 1), normal);
        if (stamps == 32 && distance >= spacing * 33) _lastFoliageStamp = hit;
        if (changed) RequestNextFrameRendering();
    }

    private bool StampFoliage(EditorSession session, Vector3 center, Vector3 normal)
    {
        if (_foliageSurfaceDocument is not { } surfaces || FoliageModels.Count == 0) return false;
        float radius = Math.Clamp(FoliageRadius, 1, 100000);
        int count = Math.Clamp(FoliageDensity, 1, 128);
        (Vector3 tangent, Vector3 bitangent) = SurfaceBasis(normal);
        bool added = false;
        for (int index = 0; index < count; index++)
        {
            float angle = Random.Shared.NextSingle() * MathF.Tau;
            float distance = MathF.Sqrt(Random.Shared.NextSingle()) * radius;
            Vector3 sample = center + tangent * (MathF.Cos(angle) * distance) + bitangent * (MathF.Sin(angle) * distance);
            Vector3 rayOrigin = sample + normal * 60;
            if (!SurfaceRaycast.TryHitSurfaces(surfaces, rayOrigin, -normal, ResolveMaterial,
                    out Vector3 hit, out Vector3 hitNormal) || Vector3.Distance(hit, sample) > 120) continue;
            XModelSource model = FoliageModels[Random.Shared.Next(FoliageModels.Count)];
            float minimum = Math.Max(0.01f, Math.Min(FoliageMinimumScale, FoliageMaximumScale));
            float maximum = Math.Max(minimum, Math.Max(FoliageMinimumScale, FoliageMaximumScale));
            float scale = minimum + Random.Shared.NextSingle() * (maximum - minimum);
            float yaw = FoliageRandomYaw ? Random.Shared.NextSingle() * 360 : 0;
            MapEntity entity = XModelEditing.Add(session, model, hit, FoliageAlignSurface ? hitNormal : null, yaw, scale);
            _foliagePreview.Add((entity, model));
            added = true;
        }
        _lastFoliageStamp = center;
        if (!added) return false;
        _foliageChanged = true;
        _renderer.SetFoliagePreview(_foliagePreview);
        return true;
    }

    private bool TryMapHit(Point point, bool includeModels, out Vector3 hit, out Vector3 normal)
    {
        hit = normal = default;
        if (_session is not { } session || !new Rect(Bounds.Size).Contains(point)) return false;
        MapDocument surfaces = includeModels ? session.Scene.Document : _foliageSurfaceDocument ?? session.Scene.Document;
        var (origin, direction) = _navigation.PickRay((float)(point.X / Math.Max(1, Bounds.Width) * 2 - 1),
            (float)(1 - point.Y / Math.Max(1, Bounds.Height) * 2), Aspect);
        bool found = includeModels
            ? SurfaceRaycast.TryHit(surfaces, origin, direction, name => session.Scene.ResolveModel?.Invoke(name),
                ResolveMaterial, null, out hit, out normal)
            : SurfaceRaycast.TryHitSurfaces(surfaces, origin, direction, ResolveMaterial, out hit, out normal);
        return found && CameraPicking.InCubicClip(session, hit, origin);
    }

    private void UpdateFoliageBrush(Point point)
    {
        if (!FoliagePaintingEnabled || !TryMapHit(point, includeModels: false, out Vector3 hit, out Vector3 normal))
        {
            FoliageBrushChanged?.Invoke(null);
            return;
        }
        (Vector3 tangent, Vector3 bitangent) = SurfaceBasis(normal);
        var points = new List<Point>(48);
        for (int index = 0; index < 48; index++)
        {
            float angle = index * (MathF.Tau / 48);
            Vector3 edge = hit + (tangent * MathF.Cos(angle) + bitangent * MathF.Sin(angle)) * FoliageRadius;
            if (!CameraPicking.Project(_navigation, edge, Bounds.Size, out Point screen, out _))
            {
                FoliageBrushChanged?.Invoke(null);
                return;
            }
            points.Add(screen);
        }
        FoliageBrushChanged?.Invoke(points);
    }

    private static (Vector3 Tangent, Vector3 Bitangent) SurfaceBasis(Vector3 normal)
    {
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(normal,
            MathF.Abs(Vector3.Dot(normal, Vector3.UnitZ)) < 0.95f ? Vector3.UnitZ : Vector3.UnitX));
        return (tangent, Vector3.Normalize(Vector3.Cross(normal, tangent)));
    }

    private void OnModelDragOver(object? sender, DragEventArgs e)
    {
        try
        {
            e.DragEffects = CanAcceptModelDrop?.Invoke() != false && XModelDrag.TryRead(e.DataTransfer, out _, out _) &&
                TryMapHit(e.GetPosition(this), includeModels: true, out _, out _) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        catch (Exception exception) when (IsEditError(exception))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            InteractionStatusChanged?.Invoke(exception.Message);
        }
    }

    private void OnModelDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        try
        {
            if (CanAcceptModelDrop?.Invoke() == false || _session is not { } session ||
                !XModelDrag.TryRead(e.DataTransfer, out string name, out bool align) ||
                session.Scene.ResolveModel?.Invoke(name) is not { } model ||
                !TryMapHit(e.GetPosition(this), includeModels: true, out Vector3 hit, out Vector3 normal))
            {
                InteractionStatusChanged?.Invoke("Drop the model onto an existing visible map surface.");
                return;
            }
            FinishGesture(cancel: true);
            if (session.HasPlacement) session.CancelPlacement();
            XModelEditing.Place(session, model, hit, align ? normal : null);
            e.DragEffects = DragDropEffects.Copy;
            InteractionStatusChanged?.Invoke($"Placed {name}.");
        }
        catch (Exception exception) when (IsEditError(exception))
        { InteractionStatusChanged?.Invoke(exception.Message); }
    }

    private static bool IsEditError(Exception exception) => exception is ArgumentException or FormatException or InvalidOperationException or IOException;
}
