using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.EditorPreview;
using IW4.Render.OpenGl;
using IW4.Render.Picking;
using IW4.Render.Resources;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.Rendering;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Embedded authoring viewport for the map editor. Avalonia owns the OpenGL
/// context and presentation framebuffer; the IW4 renderer owns only its
/// scene resources and receives the current host framebuffer per frame.
/// </summary>
public sealed class WorldViewportControl :
    OpenGlControlBase,
    ICustomHitTest,
    IDisposable
{
    public static readonly StyledProperty<string> StatusHeadingProperty =
        AvaloniaProperty.Register<WorldViewportControl, string>(
            nameof(StatusHeading),
            "WORLD VIEWPORT NOT ATTACHED");

    public static readonly StyledProperty<string> StatusMessageProperty =
        AvaloniaProperty.Register<WorldViewportControl, string>(
            nameof(StatusMessage),
            "No shared map-render scene was supplied.");

    public static readonly StyledProperty<IWorldViewportTranslationTool?>
        TranslationToolProperty =
            AvaloniaProperty.Register<
                WorldViewportControl,
                IWorldViewportTranslationTool?>(nameof(TranslationTool));

    public static readonly StyledProperty<bool>
        IsTranslationModeActiveProperty =
            AvaloniaProperty.Register<WorldViewportControl, bool>(
                nameof(IsTranslationModeActive));

    public static readonly StyledProperty<bool>
        IsCollisionOverlayVisibleProperty =
            AvaloniaProperty.Register<WorldViewportControl, bool>(
                nameof(IsCollisionOverlayVisible));

    public static readonly StyledProperty<bool>
        IsCollisionIsolateActiveProperty =
            AvaloniaProperty.Register<WorldViewportControl, bool>(
                nameof(IsCollisionIsolateActive));

    public static readonly StyledProperty<bool>
        IsCollisionPickingActiveProperty =
            AvaloniaProperty.Register<WorldViewportControl, bool>(
                nameof(IsCollisionPickingActive));

    public static readonly StyledProperty<bool>
        IsCollisionWorkspaceActiveProperty =
            AvaloniaProperty.Register<WorldViewportControl, bool>(
                nameof(IsCollisionWorkspaceActive));

    private readonly HashSet<Key> _pressedKeys = [];
    private Task<RenderViewSceneBuildResult>? _sceneWarmup;
    private WorldViewportSceneAuthority? _sceneAuthority;
    private RenderViewSceneBuildResult? _buildResult;
    private MapRenderScene? _scene;
    private RenderSceneSnapshot? _sceneSnapshot;
    private MapEditorLivePreviewBridge? _bridge;
    private readonly MapRenderLiveSceneProjectionMailbox
        _projectionMailbox = new();
    private GL? _gl;
    private SilkOpenGlMapRenderer? _renderer;
    private MapRenderCamera _camera;
    private Vector2 _lastPointerPosition;
    private MapRenderSurfaceExtents? _surfaceExtents;
    private IPointer? _mouseLookPointer;
    private IPointer? _translationPointer;
    private Vector2 _translationPressPosition;
    private MapVector3 _translationStartOrigin;
    private MapBounds? _translationStartBounds;
    private Key? _heldTranslationConstraintKey;
    private WorldViewportTranslationConstraint _translationConstraint =
        WorldViewportTranslationConstraint.ViewPlane;
    private long _translationDraftVersion;
    private long _appliedTranslationDraftVersion = -1;
    private long _lastFrameTimestamp;
    private bool _isMouseLooking;
    private bool _isTranslating;
    private bool _rendererLoaded;
    private bool _renderFailed;
    private bool _disposed;

    public WorldViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
        LostFocus += WorldViewportControl_LostFocus;
        PointerCaptureLost += WorldViewportControl_PointerCaptureLost;
    }

    public string StatusHeading
    {
        get => GetValue(StatusHeadingProperty);
        private set => SetCurrentValue(StatusHeadingProperty, value);
    }

    public string StatusMessage
    {
        get => GetValue(StatusMessageProperty);
        private set => SetCurrentValue(StatusMessageProperty, value);
    }

    public IWorldViewportTranslationTool? TranslationTool
    {
        get => GetValue(TranslationToolProperty);
        set => SetValue(TranslationToolProperty, value);
    }

    public bool IsTranslationModeActive
    {
        get => GetValue(IsTranslationModeActiveProperty);
        set => SetValue(IsTranslationModeActiveProperty, value);
    }

    public bool IsCollisionOverlayVisible
    {
        get => GetValue(IsCollisionOverlayVisibleProperty);
        set => SetValue(IsCollisionOverlayVisibleProperty, value);
    }

    public bool IsCollisionIsolateActive
    {
        get => GetValue(IsCollisionIsolateActiveProperty);
        set => SetValue(IsCollisionIsolateActiveProperty, value);
    }

    public bool IsCollisionPickingActive
    {
        get => GetValue(IsCollisionPickingActiveProperty);
        set => SetValue(IsCollisionPickingActiveProperty, value);
    }

    public bool IsCollisionWorkspaceActive
    {
        get => GetValue(IsCollisionWorkspaceActiveProperty);
        set => SetValue(IsCollisionWorkspaceActiveProperty, value);
    }

    /// <summary>
    /// The OpenGL image is hosted as a compositor child and does not create an
    /// Avalonia draw list of its own. Declare the control bounds as the input
    /// surface so pointer focus, mouse look, and source picking reach the
    /// viewport.
    /// </summary>
    public bool HitTest(Point point) =>
        point.X >= 0d &&
        point.X < Bounds.Width &&
        point.Y >= 0d &&
        point.Y < Bounds.Height;

    /// <summary>
    /// Attaches the one scene-build task already owned by the Studio workspace.
    /// This control never reparses the fastfile or rebuilds map assets.
    /// </summary>
    internal void Attach(
        Task<RenderViewSceneBuildResult> sceneWarmup,
        MapEditorLivePreviewBridge bridge,
        WorldViewportSceneAuthority sceneAuthority)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sceneWarmup);
        ArgumentNullException.ThrowIfNull(bridge);
        if (_sceneWarmup is not null)
        {
            throw new InvalidOperationException(
                "A WorldViewport can attach to exactly one workspace scene.");
        }

        _sceneWarmup = sceneWarmup;
        _sceneAuthority = sceneAuthority;
        _bridge = bridge;
        bridge.ProjectionChanged += Bridge_ProjectionChanged;
        bridge.SelectionChanged += Bridge_SelectionChanged;
        _projectionMailbox.Publish(bridge.CurrentProjection);
        SetStatus(
            "PREPARING WORLD VIEWPORT",
            "Reusing the Studio map-render scene; no assets are being rebuilt.");
        _ = ObserveSceneWarmupAsync(sceneWarmup);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        if (_disposed)
            return;

        try
        {
            _renderFailed = false;
            _lastFrameTimestamp = 0;
            _gl = GL.GetApi(name => gl.GetProcAddress(name));
            SetStatus(
                "OPENGL READY",
                _buildResult is null
                    ? "Waiting for the shared Studio scene build."
                    : "Loading the immutable map scene into the authoring viewport.");
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            _renderFailed = true;
            SetStatus(
                "WORLD VIEWPORT UNAVAILABLE",
                $"Avalonia OpenGL initialization failed: {exception.Message}");
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_disposed || _gl is not { } silkGl)
            return;

        if (_renderFailed)
        {
            ClearHostFramebuffer(silkGl, framebuffer);
            return;
        }

        try
        {
            uint hostFramebuffer = unchecked((uint)framebuffer);
            if (!TryEnsureRendererLoaded(hostFramebuffer))
            {
                ClearHostFramebuffer(silkGl, framebuffer);
                RequestFrame();
                return;
            }

            SilkOpenGlMapRenderer renderer = _renderer!;
            renderer.SetHostFramebuffer(hostFramebuffer);
            MapRenderSurfaceExtents extents = MeasureSurfaceExtents();
            if (_surfaceExtents != extents)
            {
                renderer.SetHostFramebuffer(hostFramebuffer);
                renderer.Resize(extents);
                _surfaceExtents = extents;
            }

            long now = Stopwatch.GetTimestamp();
            double elapsedSeconds = _lastFrameTimestamp == 0
                ? 0d
                : Stopwatch.GetElapsedTime(
                    _lastFrameTimestamp,
                    now).TotalSeconds;
            _lastFrameTimestamp = now;
            _camera = WorldViewportCameraController.Update(
                _camera,
                CaptureNavigationInput(),
                elapsedSeconds);

            ApplyPendingProjection(renderer);
            ApplyPendingTranslationDraft(renderer);
            ApplySelectionOutline(renderer);
            ApplyCollisionDisplay(renderer);
            renderer.SetHostFramebuffer(hostFramebuffer);
            renderer.Render(_camera);
            renderer.RecordPresentedFrame();
            RequestFrame();
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            _renderFailed = true;
            SetStatus(
                "WORLD VIEWPORT STOPPED",
                $"Rendering failed closed: {exception.Message}");
            ClearHostFramebuffer(silkGl, framebuffer);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        try
        {
            _renderer?.Dispose();
        }
        finally
        {
            _renderer = null;
            _rendererLoaded = false;
            _gl?.Dispose();
            _gl = null;
            _surfaceExtents = null;
            _lastFrameTimestamp = 0;
            _appliedTranslationDraftVersion = -1;
            base.OnOpenGlDeinit(gl);
        }
    }

    protected override void OnOpenGlLost()
    {
        // The lost context itself is unusable, so relinquish its resources
        // without driver deletion calls. Avalonia may immediately provide a
        // replacement context through OnOpenGlInit; the immutable CPU scene
        // and current semantic projection remain available for that rebuild.
        Exception? abandonFailure = null;
        try
        {
            _renderer?.AbandonContext();
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            abandonFailure = exception;
        }
        finally
        {
            _renderer = null;
            _rendererLoaded = false;
            _gl = null;
            _surfaceExtents = null;
            _renderFailed = true;
            _lastFrameTimestamp = 0;
            _appliedTranslationDraftVersion = -1;
            SetStatus(
                "WORLD VIEWPORT CONTEXT LOST",
                abandonFailure is null
                    ? "The invalid OpenGL context was abandoned safely. Waiting for Avalonia to initialize a replacement; compiled assets were not changed."
                    : $"The invalid OpenGL context was abandoned. Managed cleanup reported: {abandonFailure.Message} Waiting for Avalonia to initialize a replacement; compiled assets were not changed.");
            base.OnOpenGlLost();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_disposed)
            return;

        if (e.Key == Key.F && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            FrameCurrentSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape &&
            TranslationTool is { HasDraftChanges: true } cancelTool)
        {
            StopTranslation(_translationPointer);
            cancelTool.CancelChanges();
            SetReadyStatus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            TranslationTool is { HasDraftChanges: true } applyTool)
        {
            StopTranslation(_translationPointer);
            applyTool.ApplyChanges();
            SetReadyStatus();
            e.Handled = true;
            return;
        }

        if (IsTranslationModeActive &&
            TryResolveTranslationConstraint(
                e.Key,
                out WorldViewportTranslationConstraint constraint))
        {
            _heldTranslationConstraintKey = e.Key;
            _translationConstraint = constraint;
            if (_isTranslating)
            {
                UpdateTranslationCandidate(
                    _lastPointerPosition,
                    gridSnap: false);
            }
            e.Handled = true;
            return;
        }

        if (IsNavigationKey(e.Key))
        {
            _pressedKeys.Add(e.Key);
            e.Handled = true;
            RequestFrame();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_heldTranslationConstraintKey == e.Key &&
            TryResolveTranslationConstraint(e.Key, out _))
        {
            _heldTranslationConstraintKey = null;
            _translationConstraint =
                WorldViewportTranslationConstraint.ViewPlane;
            if (_isTranslating)
            {
                UpdateTranslationCandidate(
                    _lastPointerPosition,
                    gridSnap: false);
            }
            e.Handled = true;
            return;
        }
        if (_pressedKeys.Remove(e.Key))
        {
            e.Handled = true;
            RequestFrame();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_disposed)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        PointerPoint point = e.GetCurrentPoint(this);
        _lastPointerPosition = ToVector2(point.Position);
        if (point.Properties.PointerUpdateKind ==
            PointerUpdateKind.RightButtonPressed)
        {
            _isMouseLooking = true;
            _mouseLookPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.PointerUpdateKind ==
            PointerUpdateKind.LeftButtonPressed)
        {
            if (IsTranslationModeActive &&
                TranslationTool is { CanManipulate: true } translationTool)
            {
                _isTranslating = true;
                _translationPointer = e.Pointer;
                _translationPressPosition = _lastPointerPosition;
                _translationStartOrigin = translationTool.DraftOrigin;
                _translationStartBounds = translationTool.Bounds;
                _translationConstraint =
                    _heldTranslationConstraintKey is { } constraintKey &&
                    TryResolveTranslationConstraint(
                        constraintKey,
                        out WorldViewportTranslationConstraint heldConstraint)
                        ? heldConstraint
                        : WorldViewportTranslationConstraint.ViewPlane;
                translationTool.BeginManipulation();
                e.Pointer.Capture(this);
                SetStatus(
                    "MOVE DRAFT",
                    "Drag in the camera plane · hold X, Y, or Z to constrain · Ctrl snaps to 1 unit · Enter applies · Esc cancels.");
                e.Handled = true;
                return;
            }

            PublishPick(_lastPointerPosition);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Vector2 position = ToVector2(e.GetPosition(this));
        if (_isTranslating)
        {
            _lastPointerPosition = position;
            UpdateTranslationCandidate(
                position,
                e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
            return;
        }
        if (!_isMouseLooking)
        {
            _lastPointerPosition = position;
            return;
        }

        Vector2 delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        _camera = WorldViewportCameraController.ApplyMouseLook(
            _camera,
            delta);
        RequestFrame();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            StopMouseLook(e.Pointer);
            e.Handled = true;
        }
        else if (e.InitialPressMouseButton == MouseButton.Left &&
                 _isTranslating)
        {
            StopTranslation(e.Pointer);
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pressedKeys.Clear();
        _heldTranslationConstraintKey = null;
        StopTranslation(_translationPointer);
        StopMouseLook(_mouseLookPointer);
        if (TranslationTool is { } translationTool)
            translationTool.DraftChanged -= TranslationTool_DraftChanged;
        if (_bridge is not null)
        {
            _bridge.ProjectionChanged -= Bridge_ProjectionChanged;
            _bridge.SelectionChanged -= Bridge_SelectionChanged;
            _bridge = null;
        }
        _projectionMailbox.Clear();
        LostFocus -= WorldViewportControl_LostFocus;
        PointerCaptureLost -= WorldViewportControl_PointerCaptureLost;
    }

    private async Task ObserveSceneWarmupAsync(
        Task<RenderViewSceneBuildResult> sceneWarmup)
    {
        try
        {
            RenderViewSceneBuildResult result =
                await sceneWarmup.ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(
                () => AcceptSceneBuildResult(result));
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            if (_disposed)
                return;

            SetStatus(
                "WORLD VIEWPORT UNAVAILABLE",
                $"The shared Studio scene build failed: {exception.Message}");
        }
    }

    private void AcceptSceneBuildResult(
        RenderViewSceneBuildResult result)
    {
        if (_disposed)
            return;

        _buildResult = result;
        if (!result.IsRenderable)
        {
            SetStatus(
                "WORLD VIEWPORT UNAVAILABLE",
                result.NonRenderableReason ??
                "The loaded fastfile has no renderable map assets.");
            return;
        }
        if (_sceneAuthority is not { } authority ||
            _bridge is not { } bridge)
        {
            SetStatus(
                "WORLD VIEWPORT AUTHORITY REJECTED",
                "No authoritative map identity and semantic bridge were supplied.");
            return;
        }
        if (!authority.TryValidate(
                result,
                bridge,
                out string failure))
        {
            SetStatus(
                "WORLD VIEWPORT AUTHORITY REJECTED",
                failure);
            return;
        }

        _scene = result.Scene!;
        _sceneSnapshot = result.SceneSnapshot!;
        _camera = MapRenderCamera.CreateForBounds(_scene.CameraBounds);
        SetStatus(
            "WORLD SCENE READY",
            _gl is null
                ? "Waiting for an Avalonia OpenGL context."
                : "Loading immutable GPU resources.");
        RequestFrame();
    }

    private bool TryEnsureRendererLoaded(uint hostFramebuffer)
    {
        if (_rendererLoaded)
            return true;
        if (_scene is not { } scene ||
            _sceneSnapshot is not { } snapshot ||
            _gl is not { } gl)
        {
            return false;
        }

        SetStatus(
            "LOADING WORLD VIEWPORT",
            "Creating renderer resources from the prepared scene snapshot.");
        var renderer = new SilkOpenGlMapRenderer(gl)
        {
            EditorPreviewFogRenderingEnabled = true,
            ShowTexturedGeometry = true
        };
        try
        {
            MapRenderSurfaceExtents extents = MeasureSurfaceExtents();
            float aspect = extents.SceneTarget.Width /
                (float)extents.SceneTarget.Height;
            renderer.SetHostFramebuffer(hostFramebuffer);
            renderer.Load(scene, snapshot, _camera, aspect);
            ApplyCollisionDisplay(renderer);
            renderer.SetHostFramebuffer(hostFramebuffer);
            renderer.Resize(extents);
            _surfaceExtents = extents;
            _renderer = renderer;
            _rendererLoaded = true;
            if (_bridge is { } bridge)
                _projectionMailbox.Publish(bridge.CurrentProjection);
            ApplyPendingProjection(renderer);
            SetReadyStatus();
            return true;
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
    }

    private void ApplyPendingProjection(
        SilkOpenGlMapRenderer renderer)
    {
        MapRenderLiveSceneProjection? projection =
            _projectionMailbox.Take();
        if (projection is null)
            return;

        try
        {
            renderer.ApplyLiveSceneProjection(projection);
            SetReadyStatus();
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            SetStatus(
                "EDITOR PROJECTION REJECTED",
                $"{exception.Message} Baseline compiled geometry remains active.");
        }
    }

    private void ApplyPendingTranslationDraft(
        SilkOpenGlMapRenderer renderer)
    {
        if (_appliedTranslationDraftVersion == _translationDraftVersion)
            return;

        IWorldViewportTranslationTool? tool = TranslationTool;
        if (tool is
            {
                HasDraftChanges: true,
                RenderStaticModelSourceOrdinal: { } sourceOrdinal
            })
        {
            MapVector3 origin = tool.DraftOrigin;
            renderer.ApplyTransientStaticModelTranslation(
                sourceOrdinal,
                new Vector3(origin.X, origin.Y, origin.Z));
        }
        else
        {
            renderer.ClearTransientStaticModelTranslation();
        }

        _appliedTranslationDraftVersion = _translationDraftVersion;
    }

    private void ApplySelectionOutline(
        SilkOpenGlMapRenderer renderer)
    {
        MapRenderEditorSelectionOutline? outline = null;
        if (_bridge?.CurrentSelection is { } selection &&
            _bridge.CurrentSemanticSnapshot.TryGetObject(
                selection,
                out var selected) &&
            selected is not null &&
            WorldViewportSelectionOutlineProjection.TryCreate(
                selection,
                selected.Bounds,
                TranslationTool,
                out MapRenderEditorSelectionOutline projected))
        {
            outline = projected;
        }

        renderer.SetEditorSelectionOutline(outline);
    }

    private void ApplyCollisionDisplay(
        SilkOpenGlMapRenderer renderer)
    {
        WorldViewportCollisionDisplaySettings settings =
            WorldViewportCollisionDisplayPolicy.Resolve(
                IsCollisionOverlayVisible,
                IsCollisionIsolateActive);
        renderer.ShowWireframe = settings.ShowCollisionWireframe;
        renderer.ShowTexturedGeometry = settings.ShowTexturedGeometry;
        renderer.ShowSky = settings.ShowSky;
    }

    private void PublishPick(Vector2 position)
    {
        if (_scene is not { } scene ||
            Bounds.Width <= 0 ||
            Bounds.Height <= 0 ||
            _bridge is not IMapEditorLivePreviewPickSink pickSink)
        {
            return;
        }

        // This is intentionally source-semantic picking over the immutable
        // scene. It does not claim current-frame DPVS, LOD, material
        // executability, or editor-visibility filtering.
        Vector2 viewportSize = new(
            (float)Bounds.Width,
            (float)Bounds.Height);
        MapRenderPickHit pick;
        WorldViewportPickingDomain pickingDomain =
            WorldViewportCollisionDisplayPolicy.ResolvePickingDomain(
                IsCollisionWorkspaceActive,
                IsCollisionPickingActive,
                IsCollisionOverlayVisible,
                IsCollisionIsolateActive);
        bool hit;
        if (pickingDomain == WorldViewportPickingDomain.Render)
        {
            hit = MapRenderPicker.TryPick(
                scene,
                _camera,
                position,
                viewportSize,
                includeUntexturedGeometry: true,
                includeCollision: false,
                out pick);
        }
        else if (
            pickingDomain ==
                WorldViewportPickingDomain.Collision)
        {
            hit = MapRenderPicker.TryPickCollision(
                scene,
                _camera,
                position,
                viewportSize,
                out pick);
        }
        else
        {
            pick = default;
            hit = false;
        }
        pickSink.PublishPick(hit ? pick : null);
    }

    private void FrameCurrentSelection()
    {
        if (_bridge?.CurrentSelection is not { } selection ||
            !_bridge.CurrentSemanticSnapshot.TryGetObject(
                selection,
                out var selected) ||
            selected is null ||
            !WorldViewportSelectionOutlineProjection.TryResolveBounds(
                selection,
                selected.Bounds,
                TranslationTool,
                out MapBounds effectiveBounds) ||
            !WorldViewportCameraController.TryFrameBounds(
                _camera,
                effectiveBounds,
                out MapRenderCamera framed))
        {
            SetStatus(
                "FOCUS UNAVAILABLE",
                "The current semantic selection has no finite imported bounds.");
            return;
        }

        _camera = framed;
        SetReadyStatus();
        RequestFrame();
    }

    private WorldViewportNavigationInput CaptureNavigationInput()
    {
        if (_isTranslating)
            return WorldViewportNavigationInput.None;

        WorldViewportNavigationInput input =
            WorldViewportNavigationInput.None;
        AddIfPressed(Key.W, WorldViewportNavigationInput.Forward);
        AddIfPressed(Key.S, WorldViewportNavigationInput.Backward);
        AddIfPressed(Key.A, WorldViewportNavigationInput.Left);
        AddIfPressed(Key.D, WorldViewportNavigationInput.Right);
        AddIfPressed(Key.Up, WorldViewportNavigationInput.Up);
        AddIfPressed(Key.Down, WorldViewportNavigationInput.Down);
        AddIfPressed(Key.Left, WorldViewportNavigationInput.YawLeft);
        AddIfPressed(Key.Right, WorldViewportNavigationInput.YawRight);
        if (_pressedKeys.Contains(Key.LeftShift) ||
            _pressedKeys.Contains(Key.RightShift))
        {
            input |= WorldViewportNavigationInput.Fast;
        }
        return input;

        void AddIfPressed(
            Key key,
            WorldViewportNavigationInput value)
        {
            if (_pressedKeys.Contains(key))
                input |= value;
        }
    }

    private MapRenderSurfaceExtents MeasureSurfaceExtents()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        return WorldViewportSurfaceExtentPolicy.Measure(
            Bounds.Width,
            Bounds.Height,
            scaling);
    }

    private void Bridge_ProjectionChanged(
        object? sender,
        MapEditorLivePreviewChangedEventArgs e)
    {
        if (_disposed)
            return;

        if (!ReferenceEquals(sender, _bridge))
            return;

        _projectionMailbox.Publish(e.Projection);
        RequestFrame();
    }

    private void Bridge_SelectionChanged(
        object? sender,
        MapEditorLivePreviewSelectionChangedEventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _bridge))
            return;

        RequestFrame();
    }

    private void SetReadyStatus() =>
        SetStatus(
            "AUTHORING VIEWPORT",
            IsTranslationModeActive
                ? "Move tool · left-drag previews without history · X/Y/Z constrain · Ctrl snaps · Enter applies · Esc cancels."
                : "Select tool · left-click selects · right-drag looks · WASD moves · arrows elevate/turn · Shift accelerates · F frames selection.");

    private void SetStatus(string heading, string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SetStatusOnUiThread(heading, message);
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_disposed)
                    SetStatusOnUiThread(heading, message);
            });
    }

    private void SetStatusOnUiThread(string heading, string message)
    {
        StatusHeading = heading;
        StatusMessage = message;
    }

    private void RequestFrame()
    {
        if (_disposed)
            return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            RequestNextFrameRendering();
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_disposed)
                    RequestNextFrameRendering();
            },
            DispatcherPriority.Render);
    }

    private static void ClearHostFramebuffer(GL gl, int framebuffer)
    {
        gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            unchecked((uint)framebuffer));
        gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.ClearColor(0.025f, 0.03f, 0.04f, 1f);
        gl.ClearDepth(1d);
        gl.Clear(
            (uint)(ClearBufferMask.ColorBufferBit |
                   ClearBufferMask.DepthBufferBit));
    }

    private static bool IsNavigationKey(Key key) =>
        key is
            Key.W or Key.S or Key.A or Key.D or
            Key.Up or Key.Down or Key.Left or Key.Right or
            Key.LeftShift or Key.RightShift;

    private static bool TryResolveTranslationConstraint(
        Key key,
        out WorldViewportTranslationConstraint constraint)
    {
        constraint = key switch
        {
            Key.X => WorldViewportTranslationConstraint.GameX,
            Key.Y => WorldViewportTranslationConstraint.GameY,
            Key.Z => WorldViewportTranslationConstraint.GameZ,
            _ => WorldViewportTranslationConstraint.ViewPlane
        };
        return key is Key.X or Key.Y or Key.Z;
    }

    private static Vector2 ToVector2(Point point) =>
        new((float)point.X, (float)point.Y);

    private void WorldViewportControl_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        _pressedKeys.Clear();
        _heldTranslationConstraintKey = null;
        StopTranslation(_translationPointer);
        StopMouseLook(_mouseLookPointer);
    }

    private void WorldViewportControl_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _isMouseLooking = false;
        _mouseLookPointer = null;
        if (_isTranslating)
            StopTranslation(_translationPointer);
    }

    private void StopMouseLook(IPointer? pointer)
    {
        _isMouseLooking = false;
        _mouseLookPointer = null;
        pointer?.Capture(null);
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TranslationToolProperty)
        {
            if (change.OldValue is IWorldViewportTranslationTool oldTool)
                oldTool.DraftChanged -= TranslationTool_DraftChanged;
            if (change.NewValue is IWorldViewportTranslationTool newTool)
                newTool.DraftChanged += TranslationTool_DraftChanged;
            StopTranslation(_translationPointer);
            MarkTranslationDraftDirty();
        }
        else if (change.Property == IsTranslationModeActiveProperty)
        {
            if (!IsTranslationModeActive)
            {
                _heldTranslationConstraintKey = null;
                StopTranslation(_translationPointer);
            }
            SetReadyStatus();
        }
    }

    private void TranslationTool_DraftChanged(
        object? sender,
        EventArgs e)
    {
        if (!ReferenceEquals(sender, TranslationTool))
            return;

        MarkTranslationDraftDirty();
    }

    private void MarkTranslationDraftDirty()
    {
        _translationDraftVersion =
            checked(_translationDraftVersion + 1);
        RequestFrame();
    }

    private void UpdateTranslationCandidate(
        Vector2 pointerPosition,
        bool gridSnap)
    {
        if (!_isTranslating ||
            TranslationTool is not { CanManipulate: true } tool)
        {
            return;
        }

        MapVector3 candidate =
            WorldViewportTranslationManipulator.ResolveCandidate(
                _translationStartOrigin,
                _translationStartBounds,
                _camera,
                new Vector2(
                    (float)Bounds.Width,
                    (float)Bounds.Height),
                pointerPosition - _translationPressPosition,
                _translationConstraint,
                gridSnap ? 1f : null);
        tool.UpdateDraftOrigin(candidate);
        RequestFrame();
    }

    private void StopTranslation(IPointer? pointer)
    {
        if (!_isTranslating)
            return;

        _isTranslating = false;
        _translationPointer = null;
        TranslationTool?.EndManipulation();
        pointer?.Capture(null);
        SetReadyStatus();
    }
}
