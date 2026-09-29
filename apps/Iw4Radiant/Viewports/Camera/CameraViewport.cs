using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using System.Numerics;
using IW4.Render.EditorPreview;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport : OpenGlControlBase, ICustomHitTest
{
    private EditorSession? _session;
    private readonly CameraNavigation _navigation = new();
    private readonly CameraFlyMovement _flyMovement;
    private readonly SceneRenderer _renderer = new();
    private int _lastFxPlaybackState;
    private bool _lastMapFxFinished;
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
    internal bool PhysicsContextMenuOpen => PhysicsPlacementActive && _objectMenu?.IsOpen == true;
    private bool _previewLighting = true;
    private FilmPreview _filmPreview = FilmPreview.Neutral;
    private FogPreview _fogPreview = FogPreview.Disabled;
    private bool _flyMode;
    private bool _foliagePaintingEnabled, _paintingFoliage, _foliageChanged;
    private bool _foliagePrefabsNeedRefresh;
    private MapDocument? _foliageSurfaceDocument;
    private Vector3? _lastFoliageStamp;
    private readonly List<(MapEntity Entity, XModelSource Model)> _foliagePreview = [];

    public CameraViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        _flyMovement = new CameraFlyMovement(this, _navigation);
        _walkMovement = new CameraWalkMovement(this, _navigation);
        _placementTimer.Tick += OnPhysicsPlacementTick;
        _shatterTimer.Tick += OnGlassShatterTick;
        _renderer.StatusChanged += (_, _) => RendererStatusChanged?.Invoke(this, EventArgs.Empty);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerExited += (_, _) => { if (!_paintingFoliage) FoliageBrushChanged?.Invoke(null); };
        PointerCaptureLost += (_, _) => { if (_dragPointer is not null) FinishGesture(cancel: true); };
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (_, e) => HandleNavigationKeyDown(e);
        KeyUp += (_, e) => { if (WalkMode) _walkMovement.KeyUp(e); else _flyMovement.KeyUp(e); };
        GotFocus += (_, _) => _walkMovement.Start();
        LostFocus += (_, _) => FinishGesture(cancel: true);
        SizeChanged += (_, _) => { FinishGesture(cancel: true); RequestNextFrameRendering(); };
        DetachedFromVisualTree += (_, _) => { StopPhysicsPlacement(); StopGlassShatter(); StopWalk(); FinishGesture(cancel: true); _objectMenu?.Close(); };
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnAssetDragOver);
        DragDrop.AddDropHandler(this, OnAssetDrop);
    }

    internal EditorSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            StopPhysicsPlacement();
            StopGlassShatter();
            StopWalk();
            FinishGesture(cancel: true);
            _objectMenu?.Close();
            if (_session is not null) _session.PointEntityPreviewChanged -= OnPointEntityPreviewChanged;
            _renderer.CancelDestructiblePreparation();
            _session = value;
            if (_session is not null) _session.PointEntityPreviewChanged += OnPointEntityPreviewChanged;
            RefreshScene();
        }
    }

    private void OnPointEntityPreviewChanged(bool modelsChanged)
    {
        StopPhysicsPlacement();
        StopGlassShatter();
        StopWalk();
        if (_session is { } session && session.DeferPreviewLighting && session.TransformMode == TransformMode.Move &&
            session.Selection.Count > 0 && session.Selection.Items.All(item => item is MapEntity entity &&
                (entity.ClassName == "fx_origin" || XModelGeometry.IsModel(entity))))
        {
            _renderer.PreviewPointEntityMove();
            RequestNextFrameRendering();
        }
        else if (modelsChanged || _transform is not null) RefreshScene();
    }

    internal bool PreviewLighting
    {
        get => _previewLighting;
        set { _previewLighting = value; RequestNextFrameRendering(); }
    }
    internal CompiledBspPreview? CompiledPreview
    {
        get => _renderer.CompiledPreview;
        set
        {
            StopPhysicsPlacement();
            StopGlassShatter();
            StopWalk();
            FinishGesture(cancel: true);
            _objectMenu?.Close();
            _renderer.CompiledPreview = value;
            RefreshScene();
            if (value is not null) FrameAll();
        }
    }
    internal FilmPreview FilmAdjustment
    {
        get => _filmPreview;
        set { _filmPreview = value; RequestNextFrameRendering(); }
    }
    internal FogPreview FogAdjustment
    {
        get => _fogPreview;
        set { _fogPreview = value; RequestNextFrameRendering(); }
    }
    internal bool FlyMode
    {
        get => _flyMode;
        set
        {
            if (_flyMode == value) return;
            StopWalk();
            FinishGesture(cancel: true);
            _flyMode = value;
            NavigationModeChanged?.Invoke();
        }
    }
    internal Func<string, MaterialSource?>? ResolveMaterial { get; set; }
    internal Func<bool>? CanAcceptAssetDrop { get; set; }
    internal IReadOnlyList<FoliagePaletteModel> FoliageModels { get; set; } = [];
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
    internal bool HasRenderingError => _renderer.HasRenderingError;
    internal bool HasActiveFxPreview => _renderer.HasActiveFxPreview;
    internal bool HasActiveMapFxPreview => _renderer.HasActiveMapFxPreview;
    internal bool IsFxPreviewPaused => _renderer.IsFxPreviewPaused;
    internal bool IsFxPreviewFinished => _renderer.IsFxPreviewFinished;
    internal bool IsFxPreviewLooping => _renderer.IsFxPreviewLooping;
    internal bool IsFxPreviewRepeating => _renderer.IsFxPreviewRepeating;
    internal bool IsMapFxPreviewPaused => _renderer.IsMapFxPreviewPaused;
    internal bool IsMapFxPreviewFinished => _renderer.IsMapFxPreviewFinished;
    internal string? FxPreviewNotice => _renderer.FxPreviewNotice;
    internal (Vector3 Min, Vector3 Max)? FxPreviewBounds => _renderer.FxPreviewBounds;
    internal string? MapFxPreviewNotice => _renderer.MapFxPreviewNotice;
    internal Vector3 Eye => _navigation.Eye;
    internal Vector3 Right => _navigation.Right;
    internal string? SetMapFxPreview(string? sourceDirectory,
        IReadOnlyList<(MapEntity Owner, string Name, Vector3 Origin, Matrix4x4 Orientation)> emitters)
    {
        bool wasActive = HasActiveMapFxPreview;
        string? previousNotice = MapFxPreviewNotice;
        string? notice = _renderer.SetMapFxPreview(sourceDirectory, emitters);
        NotifyMapFxPreviewStatusChanged(wasActive, previousNotice);
        RequestNextFrameRendering();
        return notice;
    }
    internal string? StartFxPreview(string sourceDirectory, string assetName, Vector3 origin, Matrix4x4 orientation)
    {
        bool wasActive = HasActiveFxPreview;
        string? previousNotice = FxPreviewNotice;
        string? notice = _renderer.SetFxPreview(sourceDirectory, assetName, origin, orientation);
        NotifyFxPreviewStatusChanged(wasActive, previousNotice);
        RequestNextFrameRendering();
        return notice;
    }
    internal string? StartFxPreview(FxSpritePreview prototype, Vector3 origin, Matrix4x4 orientation,
        bool blend = false)
    {
        bool wasActive = HasActiveFxPreview;
        string? previousNotice = FxPreviewNotice;
        string? notice = _renderer.SetFxPreview(prototype, origin, orientation, blend);
        NotifyFxPreviewStatusChanged(wasActive, previousNotice);
        RequestNextFrameRendering();
        return notice;
    }
    internal void StopFxPreview()
    {
        bool wasActive = HasActiveFxPreview;
        string? previousNotice = FxPreviewNotice;
        _renderer.StopFxPreview();
        NotifyFxPreviewStatusChanged(wasActive, previousNotice);
        RequestNextFrameRendering();
    }
    internal void SetFxPreviewPaused(bool paused)
    {
        _renderer.SetFxPreviewPaused(paused);
        RequestNextFrameRendering();
    }
    internal void RestartFxPreview()
    {
        _renderer.RestartFxPreview();
        RequestNextFrameRendering();
    }
    internal void SetFxPreviewRepeat(bool repeat)
    {
        _renderer.SetFxPreviewRepeat(repeat);
        RequestNextFrameRendering();
    }
    internal void SetMapFxPreviewPaused(bool paused)
    {
        _renderer.SetMapFxPreviewPaused(paused);
        RequestNextFrameRendering();
    }
    internal void RestartMapFxPreview()
    {
        _renderer.RestartMapFxPreview();
        RequestNextFrameRendering();
    }
    internal void UpdateMapFxPreviewTransforms()
    {
        _renderer.UpdateMapFxPreviewTransforms();
        RequestNextFrameRendering();
    }
    private void NotifyFxPreviewStatusChanged(bool wasActive, string? previousNotice)
    {
        int state = (IsFxPreviewPaused ? 1 : 0) | (IsFxPreviewFinished ? 2 : 0) |
            (IsFxPreviewLooping ? 4 : 0) | (IsFxPreviewRepeating ? 8 : 0);
        bool playbackChanged = state != _lastFxPlaybackState;
        _lastFxPlaybackState = state;
        if (wasActive != HasActiveFxPreview ||
            playbackChanged ||
            !string.Equals(previousNotice, FxPreviewNotice, StringComparison.Ordinal))
            FxPreviewStatusChanged?.Invoke();
    }
    private void NotifyMapFxPreviewStatusChanged(bool wasActive, string? previousNotice)
    {
        bool finished = IsMapFxPreviewFinished;
        bool playbackChanged = finished != _lastMapFxFinished;
        _lastMapFxFinished = finished;
        if (wasActive != HasActiveMapFxPreview ||
            playbackChanged ||
            !string.Equals(previousNotice, MapFxPreviewNotice, StringComparison.Ordinal))
            MapFxPreviewStatusChanged?.Invoke();
    }
    internal event EventHandler? RendererStatusChanged;
    internal event Action? FxPreviewStatusChanged;
    internal event Action<MapEntity?>? DestructiblePreviewRequested;
    internal void RequestDestructiblePreview(MapEntity? entity) => DestructiblePreviewRequested?.Invoke(entity);
    internal event Action? MapFxPreviewStatusChanged;
    internal event Action<string>? InteractionStatusChanged;
    internal event Action? NavigationModeChanged;
    internal event Action? NavigationChanged;
    internal event Action<BrushKind>? BrushKindRequested;
    internal event Action? CreateModelPlayerClipRequested;
    internal event Action<IReadOnlyList<Point>?>? FoliageBrushChanged;
    internal bool HasPointerGesture => _dragPointer is not null;
    internal LeakPath? LeakPath
    {
        get => _renderer.LeakPath;
        set { _renderer.LeakPath = value; RefreshScene(); }
    }
    internal int LeakPointIndex
    {
        set { _renderer.LeakPointIndex = value; RefreshScene(); }
    }
    internal void FramePoint(Vector3 point)
    {
        StopWalk();
        FinishGesture();
        _navigation.FrameBounds((point, point), Aspect);
        NavigationChanged?.Invoke();
        RequestNextFrameRendering();
    }
    internal void FrameBounds(Vector3 min, Vector3 max)
    {
        StopWalk();
        FinishGesture();
        _navigation.FrameBounds((min, max), Aspect);
        NavigationChanged?.Invoke();
        RequestNextFrameRendering();
    }
    internal bool CanFlyMove => !WalkMode && (FlyMode || _dragPointer is not null && _dragButton == MouseButton.Right);

    internal bool HandleNavigationKeyDown(KeyEventArgs e)
    {
        if (!IsFocused || !IsEffectivelyVisible || !IsEffectivelyEnabled) return false;
        if (WalkMode) return HandleWalkKeyDown(e);
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

    internal void FlyMovementApplied()
    {
        _navigationMoved = true;
        NavigationChanged?.Invoke();
    }

    internal void RefreshScene()
    {
        // A walk world is a source snapshot. Never continue against stale collision.
        StopPhysicsPlacement();
        StopGlassShatter();
        StopWalk();
        if (_transform is { IsCurrent: false }) FinishGesture(cancel: true);
        _renderer.RefreshScene();
        RequestNextFrameRendering();
    }

    internal void RefreshDestructibleAppearance()
    {
        if (_session is { } session) _renderer.RefreshDestructibleAppearance(session.Scene);
        RequestNextFrameRendering();
    }

    internal void PrepareFxPreviewMaterials(IReadOnlyList<string> materials)
    {
        _renderer.PrepareFxPreviewMaterials(materials);
        RequestNextFrameRendering();
    }

    internal void ClearDestructibleRenderingCache()
    {
        _session?.Scene.ClearPreparedDestructibleModels();
        _renderer.CancelDestructiblePreparation();
        RequestNextFrameRendering();
    }

    internal async Task<string?> PrepareDestructibleRenderingAsync(MapEntity entity,
        DestructiblePreviewSettings settings, IReadOnlyList<string> fxMaterials, CancellationToken cancellationToken = default)
    {
        EditorSession session = _session ?? throw new InvalidOperationException("Camera has no map session.");
        if (!session.Document.Entities.Contains(entity))
            throw new InvalidOperationException("The destructible is no longer in the active map.");
        DestructiblePreset preset = DestructiblePresets.Find(entity.Properties) ??
            throw new InvalidDataException("The destructible preset is unavailable.");
        Func<string, XModelSource?>? resolveModel = session.Scene.ResolveModel;
        string modelName = entity.Properties["model"];
        XModelSource[] stageModels = await Task.Run(() =>
        {
            return preset.Preview.Stages.Select(stage =>
            {
                string name = stage.ModelName ?? modelName;
                XModelSource model = resolveModel?.Invoke(name) ??
                    throw new InvalidDataException($"The {name} model is unavailable.");
                _ = model.Document;
                return model;
            }).ToArray();
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(session, _session) || !session.Document.Entities.Contains(entity))
            throw new OperationCanceledException("The active map changed during destructible preparation.");
        var variants = session.Scene.GetPreparedDestructibleModels(entity, settings.PartStates);
        if (variants is null)
            variants = await Task.Run(() =>
            {
                var prepared = new Dictionary<int, XModelSource>();
                IReadOnlyList<DestructiblePreviewPart>? parts = preset.Preview.Parts;
                XModelSource? intact = parts is null ? null :
                    DestructibleModelPreview.Create(stageModels[0], parts, settings.PartStates);
                for (int stage = 0; stage < stageModels.Length; stage++)
                    prepared[stage] = intact is not null && preset.Preview.Stages[stage].ModelName is null
                        ? intact : stageModels[stage];
                return prepared;
            }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(session, _session) || !session.Document.Entities.Contains(entity))
            throw new OperationCanceledException("The active map changed during destructible preparation.");
        session.Scene.SetPreparedDestructibleModels(entity, settings.PartStates, variants);
        XModelSource[] models = variants.Values.Distinct<XModelSource>(ReferenceEqualityComparer.Instance).ToArray();
        XModelSource[] missingMeshes = _renderer.DestructibleModelsNeedingMesh(models);
        var meshData = await Task.Run(() =>
        {
            var data = new Dictionary<XModelSource, SceneRenderer.PreparedPreviewModelMesh>(ReferenceEqualityComparer.Instance);
            foreach (XModelSource model in missingMeshes)
                data.Add(model, SceneRenderer.BuildDestructibleMesh(model));
            return data;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(session, _session) || !session.Document.Entities.Contains(entity))
            throw new OperationCanceledException("The active map changed during destructible preparation.");
        string[] missingFx = fxMaterials.Where(name => ResolveMaterial?.Invoke(name) is not
                { TechniqueSet: { Length: > 0 }, ImagePath: { } path } || !File.Exists(path))
            .Distinct(StringComparer.Ordinal).ToArray();
        string? notice = missingFx.Length == 0 ? null : "FX materials unavailable: " + string.Join(", ", missingFx);
        string[] materials = models.SelectMany(model => model.Document.Materials
                .Select(material => material.Name)).Concat(fxMaterials.Except(missingFx, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        Task<string?> preparation = _renderer.PrepareDestructibleRendering(models, meshData, materials, notice);
        RequestNextFrameRendering();
        try { return await preparation.WaitAsync(cancellationToken); }
        finally
        {
            if (!preparation.IsCompletedSuccessfully) _renderer.CancelDestructiblePreparation(preparation);
        }
    }

    internal void ReloadTextures()
    {
        StopPhysicsPlacement();
        StopGlassShatter();
        StopWalk();
        _renderer.ReloadTextures();
        RequestNextFrameRendering();
    }

    internal void FrameAll()
    {
        StopWalk();
        FinishGesture();
        if (CompiledPreview is { } preview) _navigation.FrameBounds(preview.Bounds, Aspect);
        else if (_session is { } session)
        {
            var bounds = session.Scene.VisibleBounds;
            if (PhysicsPlacementSelectionBounds() is { } placed)
                bounds = bounds is { } world
                    ? (Vector3.Min(world.Min, placed.Min), Vector3.Max(world.Max, placed.Max)) : placed;
            _navigation.FrameBounds(bounds, Aspect);
        }
        NavigationChanged?.Invoke();
        RequestNextFrameRendering();
    }

    internal void FrameSelection()
    {
        StopWalk();
        FinishGesture();
        if (_session is { } session) _navigation.FrameBounds(
            PhysicsPlacementSelectionBounds() ?? session.SelectionBounds ?? session.Scene.VisibleBounds, Aspect);
        NavigationChanged?.Invoke();
        RequestNextFrameRendering();
    }

    internal void FinishGesture(bool cancel = false)
    {
        _flyMovement.Stop();
        _walkMovement.Stop();
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
        _foliagePrefabsNeedRefresh = false;
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
            InteractionStatusChanged?.Invoke(cancel ? "Painter stroke cancelled." :
                foliageChanged ? "Painter stroke completed." : "No assets were placed.");
        }
    }

    private float Aspect => (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height));

    // OpenGL is presented through a composition surface, which supplies no control hit area.
    bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnOpenGlInit(GlInterface gl) => _renderer.Initialize(gl);
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        StopPhysicsPlacement();
        StopGlassShatter();
        StopWalk();
        FinishGesture(cancel: true);
        _renderer.ReleaseResources();
    }
    protected override void OnOpenGlLost()
    {
        StopPhysicsPlacement();
        StopGlassShatter();
        StopWalk();
        FinishGesture(cancel: true);
        _renderer.ContextLost();
    }

    protected override void OnOpenGlRender(GlInterface glInterface, int framebuffer)
    {
        if (_session is not { } session) return;
        bool fxWasActive = HasActiveFxPreview, mapFxWasActive = HasActiveMapFxPreview;
        bool fxWasPlaying = _renderer.HasPlayingFxPreview || _renderer.HasPlayingMapFxPreview;
        string? previousFxNotice = FxPreviewNotice, previousMapFxNotice = MapFxPreviewNotice;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));
        _renderer.Render(size, framebuffer, _navigation.ViewProjection((float)size.Width / size.Height), _navigation.Eye,
            session, ResolveMaterial, CompiledPreview is null && PreviewLighting, FilmAdjustment, FogAdjustment,
            WalkMode && ShowWalkPlayer, _walkPlayerSeconds,
            _navigation.Forward, _walkPlayerMotionAmount, _walkPlayerRunning);
        NotifyFxPreviewStatusChanged(fxWasActive, previousFxNotice);
        NotifyMapFxPreviewStatusChanged(mapFxWasActive, previousMapFxNotice);
        // Render one final frame when an effect finishes so its last particles disappear.
        if (_renderer.HasPendingTextures || _renderer.HasVisibleAnimatedWater || fxWasPlaying ||
            _renderer.HasPlayingFxPreview || _renderer.HasPlayingMapFxPreview)
            RequestNextFrameRendering();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is not { } session) return;
        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        var properties = e.GetCurrentPoint(this).Properties;
        Point point = e.GetPosition(this);
        if (PhysicsPlacementActive && properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            e.Handled = true;
            return;
        }
        if (WalkMode)
        {
            _walkMovement.Start();
            if (properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
            {
                e.Handled = true;
                return;
            }
        }
        if (properties.PointerUpdateKind is PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed)
        {
            FinishPointerGesture(cancel: true);
            _objectMenu?.Close();
            _dragPointer = e.Pointer;
            _lastPointer = _pressPoint = point;
            _navigationMoved = false;
            bool middle = properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed;
            _panning = !WalkMode && (middle || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _dragButton = middle ? MouseButton.Middle : MouseButton.Right;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (CompiledPreview is not null || !properties.IsLeftButtonPressed || _dragPointer is not null) return;
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
            else if (additive || session.Tool == EditorTool.Face)
            {
                object? picked = vertex ?? CameraPicking.Pick(session, _navigation, point, Bounds.Size, session.Tool);
                if (session.Tool == EditorTool.Select)
                {
                    _dragPointer = e.Pointer;
                    _dragButton = MouseButton.Left;
                    e.Pointer.Capture(this);
                    PaintSelection(session, picked);
                }
                else if (picked is not null) session.Select(picked, additive: additive, toggle: additive);
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
            if (!WalkMode) UpdateFoliageBrush(position);
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
                else if (FlyMode || WalkMode) _navigation.Look((float)delta.X, (float)delta.Y);
                else _navigation.Orbit((float)delta.X, (float)delta.Y);
                NavigationChanged?.Invoke();
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
            bool showMenu = !WalkMode && _dragButton == MouseButton.Right &&
                !_navigationMoved && fromPress.SquaredLength < 16;
            if (_paintingFoliage) ContinueFoliageStroke(point);
            else if (_transform is { } transform) UpdateTransform(transform, point);
            else if (_dragButton == MouseButton.Left) UpdateSelectionPaint(point);
            FinishPointerGesture();
            if (showMenu && CompiledPreview is null && _session is { } session)
            {
                bool placementActive = PhysicsPlacementActive;
                IReadOnlyList<(object Item, string Label)> hits = placementActive ? [] :
                    CameraPicking.PickAll(session, _navigation, point, Bounds.Size, EditorTool.Select);
                BrushFaceSelection? face = placementActive ? null :
                    CameraPicking.PickAll(session, _navigation, point, Bounds.Size, EditorTool.Face)
                        .FirstOrDefault().Item as BrushFaceSelection;
                _objectMenu = CameraObjectMenu.Open(this, session,
                    hits, face, point,
                    kind => BrushKindRequested?.Invoke(kind),
                    () => CreateModelPlayerClipRequested?.Invoke(),
                    message => InteractionStatusChanged?.Invoke(message));
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
        if (WalkMode) { e.Handled = true; return; }
        if (_transform is not null) { e.Handled = true; return; }
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(delta) || delta == 0) return;
        if (_dragPointer is not null) _navigationMoved = true;
        if (FlyMode) _navigation.MoveLocal(0, (float)delta * 32, 0);
        else _navigation.Zoom((float)delta);
        NavigationChanged?.Invoke();
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void BeginFoliageStroke(EditorSession session, IPointer pointer, Point point)
    {
        if (FoliageModels.Count == 0)
        {
            InteractionStatusChanged?.Invoke("Add a model or prefab to the brush first.");
            return;
        }
        if (!FoliageModels.Any(item => item.IsAvailable && item.Weight is > 0))
        {
            InteractionStatusChanged?.Invoke(FoliageModels.Any(item => item.Weight is > 0)
                ? "Choose an available brush asset. Unavailable entries are skipped."
                : "Set a positive brush weight before painting.");
            return;
        }
        _foliageSurfaceDocument = session.Scene.Document;
        if (!TryMapHit(point, includeModels: false, out Vector3 hit, out Vector3 normal))
        {
            _foliageSurfaceDocument = null;
            InteractionStatusChanged?.Invoke("Point at an existing map surface to paint.");
            return;
        }
        session.BeginEdit();
        _paintingFoliage = true;
        _foliageChanged = false;
        _foliagePrefabsNeedRefresh = false;
        _lastFoliageStamp = null;
        _dragPointer = pointer;
        _dragButton = MouseButton.Left;
        _pressPoint = _lastPointer = point;
        pointer.Capture(this);
        if (StampFoliage(session, hit, normal)) RefreshFoliageStroke(session);
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
            if (StampFoliage(session, hit, normal)) RefreshFoliageStroke(session);
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
        if (changed) RefreshFoliageStroke(session);
    }

    private void RefreshFoliageStroke(EditorSession session)
    {
        if (_foliagePrefabsNeedRefresh)
        {
            _foliagePrefabsNeedRefresh = false;
            session.Refresh();
        }
        else RequestNextFrameRendering();
    }

    private bool StampFoliage(EditorSession session, Vector3 center, Vector3 normal)
    {
        if (_foliageSurfaceDocument is not { } surfaces || FoliageModels.Count == 0) return false;
        double totalWeight = FoliageModels.Sum(item => item.IsAvailable && item.Weight is > 0
            ? (double)item.Weight.Value : 0);
        if (totalWeight <= 0) return false;
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
            FoliagePaletteModel? item = PickFoliageModel(totalWeight);
            if (item is null) continue;
            float itemMinimum = item.UsesCustomPlacement ? item.MinimumScale : FoliageMinimumScale;
            float itemMaximum = item.UsesCustomPlacement ? item.MaximumScale : FoliageMaximumScale;
            float minimum = Math.Max(0.01f, Math.Min(itemMinimum, itemMaximum));
            float maximum = Math.Max(minimum, Math.Max(itemMinimum, itemMaximum));
            float scale = minimum + Random.Shared.NextSingle() * (maximum - minimum);
            bool randomYaw = item.UsesCustomPlacement ? item.RandomYaw : FoliageRandomYaw;
            float yaw = randomYaw ? Random.Shared.NextSingle() * 360 : item.UsesCustomPlacement ? item.FixedYaw : 0;
            bool alignToSurface = item.UsesCustomPlacement ? item.AlignToSurface : FoliageAlignSurface;
            Vector3 position = hit;
            if (item.UsesCustomPlacement && item.SurfaceOffset != 0)
            {
                Vector3 offsetNormal = hitNormal.LengthSquared() > 0.000001f
                    ? Vector3.Normalize(hitNormal) : Vector3.UnitZ;
                position += offsetNormal * item.SurfaceOffset;
            }
            if (item.IsPrefab)
            {
                try
                {
                    session.Prefabs.AddPainted(session, item.Name, position,
                        alignToSurface ? hitNormal : null, yaw, scale);
                    _foliagePrefabsNeedRefresh = true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or
                                                   ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
                {
                    InteractionStatusChanged?.Invoke($"Could not paint prefab {Path.GetFileNameWithoutExtension(item.Name)}: {exception.Message}");
                    continue;
                }
            }
            else if (item.Model is { } model)
            {
                MapEntity entity = XModelEditing.Add(session, model, position,
                    alignToSurface ? hitNormal : null, yaw, scale);
                _foliagePreview.Add((entity, model));
            }
            else continue;
            added = true;
        }
        _lastFoliageStamp = center;
        if (!added) return false;
        _foliageChanged = true;
        _renderer.SetFoliagePreview(_foliagePreview);
        return true;
    }

    private FoliagePaletteModel? PickFoliageModel(double totalWeight)
    {
        double choice = Random.Shared.NextDouble() * totalWeight;
        FoliagePaletteModel? last = null;
        foreach (FoliagePaletteModel item in FoliageModels)
        {
            if (!item.IsAvailable || item.Weight is not > 0) continue;
            last = item;
            choice -= (double)item.Weight.Value;
            if (choice < 0) return item;
        }
        return last;
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

    private void OnAssetDragOver(object? sender, DragEventArgs e)
    {
        if (WalkMode || PhysicsPlacementActive || GlassShatterActive || CompiledPreview is not null) { e.DragEffects = DragDropEffects.None; e.Handled = true; return; }
        try
        {
            bool supported = XModelDrag.TryRead(e.DataTransfer, out _, out _) ||
                FxSoundDrag.TryRead(e.DataTransfer, out _, out _) || DestructibleDrag.Read(e.DataTransfer) is not null;
            e.DragEffects = CanAcceptAssetDrop?.Invoke() != false && supported &&
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

    private void OnAssetDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (WalkMode || PhysicsPlacementActive || GlassShatterActive || CompiledPreview is not null) return;
        try
        {
            if (CanAcceptAssetDrop?.Invoke() == false || _session is not { } session) return;
            DestructiblePreset? destructible = DestructibleDrag.Read(e.DataTransfer);
            bool modelDrop = XModelDrag.TryRead(e.DataTransfer, out string name, out bool align);
            bool isSound = false;
            if (destructible is null && !modelDrop && !FxSoundDrag.TryRead(e.DataTransfer, out name, out isSound)) return;
            XModelSource? model = modelDrop ? session.Scene.ResolveModel?.Invoke(name) : null;
            if (modelDrop && model is null ||
                !TryMapHit(e.GetPosition(this), includeModels: true, out Vector3 hit, out Vector3 normal))
            {
                InteractionStatusChanged?.Invoke("Drop the asset onto an existing visible map surface.");
                return;
            }
            FinishGesture(cancel: true);
            if (session.HasPlacement) session.CancelPlacement();
            if (destructible is not null) DestructiblePresets.Place(session, destructible, hit);
            else if (model is not null) XModelEditing.Place(session, model, hit, align ? normal : null);
            else GameplayEntityEditing.PlaceFxSound(session, name, isSound, hit);
            e.DragEffects = DragDropEffects.Copy;
            InteractionStatusChanged?.Invoke($"Placed {destructible?.Name ?? name}.");
        }
        catch (Exception exception) when (IsEditError(exception))
        { InteractionStatusChanged?.Invoke(exception.Message); }
    }

    private static bool IsEditError(Exception exception) => exception is ArgumentException or FormatException or InvalidOperationException or IOException;
}
