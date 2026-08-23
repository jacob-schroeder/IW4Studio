using System.Numerics;
using IW4.Render.OpenGl.Programs;
using IW4.Render.EditorPreview;
using IW4.Render.OpenGl.Targets;
using IW4.Render.OpenGl.Sky;
using IW4.Render.OpenGl.Diagnostics;
using IW4.Render.OpenGl.FloatZ;
using IW4.Render.OpenGl.Wireframe;
using IW4.Render.Execution;
using IW4.Render.Preview;
using IW4.Render.Resources;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Execution.Fog;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Presentation;

/// <summary>
/// Reusable render-thread owner for EditorPreview target 2, target 4, the
/// translated fullscreen programs, and their begin/present lifecycle.
/// </summary>
internal sealed class EditorPresentationSession : IDisposable
{
    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow _state;
    private readonly MapRenderWorldSceneSource _source;
    private readonly RenderSceneSnapshot _sceneSnapshot;
    private readonly MapRenderOpenGlProgramCache _programs;
    private readonly MapRenderEditorPreviewEffectivePostState?
        _effectivePost;
    private readonly
        SilkMapRenderOpenGlNormalCameraColorTargetResourceAllocator
        _colorAllocator;
    private readonly
        SilkMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator
        _depthAllocator;
    private readonly OpenGlExecutor _executor;
    private readonly SilkMapRenderOpenGlNormalCameraDefaultPresenter
        _presenter;
    private SilkMapRenderOpenGlNormalCameraFloatZBackend?
        _floatZ;
    private readonly int _ownerThreadId;
    private MapRenderOpenGlNormalCameraTargetSet? _targets;
    private EditorPresentationFrame?
        _activeFrame;
    private MapRenderSurfaceExtents? _extents;
    private long _nextFrameRevision;
    private bool _disposed;

    private EditorPresentationSession(
        GL gl,
        SilkOpenGlStateShadow state,
        MapRenderWorldSceneSource source,
        RenderSceneSnapshot sceneSnapshot,
        MapRenderOpenGlProgramCache programs,
        SilkMapRenderOpenGlNormalCameraColorTargetResourceAllocator
            colorAllocator,
        SilkMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator
            depthAllocator,
        OpenGlExecutor executor,
        SilkMapRenderOpenGlNormalCameraDefaultPresenter presenter,
        SilkMapRenderOpenGlNormalCameraFloatZBackend? floatZ,
        MapRenderEditorPreviewEffectivePostState? effectivePost)
    {
        _gl = gl;
        _state = state;
        _source = source;
        _sceneSnapshot = sceneSnapshot;
        _programs = programs;
        _colorAllocator = colorAllocator;
        _depthAllocator = depthAllocator;
        _executor = executor;
        _presenter = presenter;
        _floatZ = floatZ;
        _effectivePost = effectivePost;
        ContextIdentity = programs.ContextIdentity;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string ContextIdentity { get; }

    public MapRenderOpenGlNormalCameraTargetSet? Targets
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _targets;
        }
    }

    public static
        EditorPresentationSession Create(
            GL gl,
            string contextIdentity,
            string linkProfileIdentity,
            MapRenderWorldSceneSource source,
            RenderSceneSnapshot sceneSnapshot,
            MapRenderOpenGlShaderCompilationCounter compilationCounter,
            OpenGlSharedProgramCache.UsageLease
                sharedProgramUsage,
            MapRenderEditorPreviewEffectivePostState? effectivePost = null,
            SilkOpenGlStateShadow? stateShadow = null)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkProfileIdentity);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sceneSnapshot);
        ArgumentNullException.ThrowIfNull(compilationCounter);
        ArgumentNullException.ThrowIfNull(sharedProgramUsage);
        SilkOpenGlStateShadow state = stateShadow ??
            throw new ArgumentNullException(nameof(stateShadow));

        MapRenderOpenGlProgramCache? programs = null;
        SilkMapRenderOpenGlNormalCameraDefaultPresenter? presenter = null;
        SilkMapRenderOpenGlNormalCameraFloatZBackend? floatZ = null;
        try
        {
            programs = new MapRenderOpenGlProgramCache(
                new SilkMapRenderOpenGlProgramCompiler(
                    gl,
                    contextIdentity,
                    linkProfileIdentity,
                    sharedProgramUsage
                        .ProgramBinaryPersistenceEnabled),
                compilationCounter,
                sharedProgramUsage);
            var colorAllocator =
                new SilkMapRenderOpenGlNormalCameraColorTargetResourceAllocator(
                    gl,
                    contextIdentity);
            var depthAllocator =
                new
                    SilkMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator(
                        gl,
                        contextIdentity);
            var replayer =
                new MapRenderOpenGlNormalCameraSceneTargetReplayer(
                    gl,
                    contextIdentity,
                    state);
            var executor = new OpenGlExecutor(replayer);
            presenter =
                new SilkMapRenderOpenGlNormalCameraDefaultPresenter(
                    gl,
                    source,
                    programs,
                    contextIdentity,
                    effectivePost);
            if (SceneRequiresProcessedFloatZ(sceneSnapshot))
            {
                floatZ =
                    new SilkMapRenderOpenGlNormalCameraFloatZBackend(
                        gl,
                        state,
                        source,
                        programs,
                        presenter.ErrorMonitor);
            }
            return new
                EditorPresentationSession(
                    gl,
                    state,
                    source,
                    sceneSnapshot,
                    programs,
                    colorAllocator,
                    depthAllocator,
                    executor,
                    presenter,
                    floatZ,
                    effectivePost);
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            TryDispose(floatZ, failures);
            TryDispose(presenter, failures);
            TryDispose(programs, failures);
            throw new AggregateException(
                "Live Preview normal-camera presentation-session creation failed; every partial owner was disposed when possible.",
                failures);
        }
    }

    public void Resize(int displayWidth, int displayHeight)
    {
        Resize(MapRenderSurfaceExtents.Unified(
            displayWidth,
            displayHeight));
    }

    public void Resize(MapRenderSurfaceExtents extents)
    {
        EnsureUsableOnOwnerThread();
        if (!extents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(extents));
        MapRenderPixelExtent sceneExtent = extents.SceneTarget;
        if (_targets is { } current &&
            current.DisplayWidth == sceneExtent.Width &&
            current.DisplayHeight == sceneExtent.Height)
        {
            _extents = extents;
            _activeFrame = null;
            return;
        }

        MapRenderOpenGlNormalCameraTargetSet replacement =
            MapRenderOpenGlNormalCameraTargetSet.Create(
                _colorAllocator,
                _depthAllocator,
                sceneExtent.Width,
                sceneExtent.Height);
        if (!string.Equals(
                replacement.ContextIdentity,
                ContextIdentity,
                StringComparison.Ordinal) ||
            replacement.DisplayWidth != sceneExtent.Width ||
            replacement.DisplayHeight != sceneExtent.Height)
        {
            replacement.Dispose();
            throw new InvalidOperationException(
                "Live Preview target allocation returned a foreign context or extent.");
        }

        try
        {
            _presenter.ResizeSceneTarget(
                sceneExtent.Width,
                sceneExtent.Height);
            _floatZ?.Resize(
                sceneExtent.Width,
                sceneExtent.Height);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        MapRenderOpenGlNormalCameraTargetSet? previous = _targets;
        _targets = replacement;
        _extents = extents;
        _activeFrame = null;
        previous?.Dispose();
    }

    public EditorPresentationFrame
        BeginFrame(
            RenderCamera camera,
            Vector3 fallbackClearColor,
            MapRenderActiveFogState? activeFog,
            RenderPreviewSettings previewSettings)
    {
        EnsureUsableOnOwnerThread();
        if (_activeFrame is not null)
        {
            throw new InvalidOperationException(
                "Live Preview began another target-2 frame before presenting the active frame.");
        }
        MapRenderOpenGlNormalCameraTargetSet targets = _targets ??
            throw new InvalidOperationException(
                "Live Preview presentation targets have no framebuffer extent.");
        MapRenderSurfaceExtents extents = _extents ??
            throw new InvalidOperationException(
                "Live Preview presentation extents have not been configured.");
        if (_nextFrameRevision == long.MaxValue)
        {
            throw new InvalidOperationException(
                "Live Preview presentation frame revision is exhausted.");
        }

        MapRenderNormalCameraClearColorResult clearColor =
            MapRenderNormalCameraClearColorProducer.ProduceEditorPreview(
                camera.FarPlane,
                fallbackClearColor,
                activeFog);
        EditorPresentationFrame frame =
            EditorPresentationFramePlanner
                .Create(
                    targets,
                    _source,
                    _nextFrameRevision++,
                    clearColor,
                    extents.HostFramebuffer,
                    _sceneSnapshot,
                    camera,
                    previewSettings,
                    _effectivePost);
        _executor.ExecuteSceneTarget(frame.FramePlan, frame.SceneTarget);

        _activeFrame = frame;
        return frame;
    }

    public void ExecuteSky(
        EditorPresentationFrame frame,
        MapRenderOpenGlNormalCameraSkyResourceCatalog resources,
        IMapRenderOpenGlNormalCameraSkyReplayApi api)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(api);
        if (!ReferenceEquals(frame, _activeFrame))
        {
            throw new ArgumentException(
                "Sky execution requires the exact active presentation frame.",
                nameof(frame));
        }

        MapRenderOpenGlNormalCameraSkyPlan lowered =
            MapRenderOpenGlNormalCameraSkyPlanner.Lower(
                frame.FramePlan,
                resources);
        _executor.ExecuteSky(frame.FramePlan, lowered, api);
    }

    public void ExecuteDiagnostics(
        EditorPresentationFrame frame,
        MapRenderOpenGlNormalCameraDiagnosticResourceCatalog resources,
        Matrix4x4 preparedHostViewProjection,
        IMapRenderOpenGlNormalCameraDiagnosticReplayApi api)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(api);
        if (!ReferenceEquals(frame, _activeFrame))
        {
            throw new ArgumentException(
                "Diagnostic execution requires the exact active presentation frame.",
                nameof(frame));
        }

        MapRenderOpenGlNormalCameraDiagnosticPlan lowered =
            MapRenderOpenGlNormalCameraDiagnosticPlanner.Lower(
                frame.FramePlan,
                resources,
                preparedHostViewProjection);
        _executor.ExecuteDiagnostics(frame.FramePlan, lowered, api);
    }

    public void ExecuteWireframe(
        EditorPresentationFrame frame,
        MapRenderOpenGlWireframeResourceCatalog resources,
        Matrix4x4 preparedHostViewProjection,
        IMapRenderOpenGlWireframeReplayApi api)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(api);
        if (!ReferenceEquals(frame, _activeFrame))
        {
            throw new ArgumentException(
                "Wireframe execution requires the exact active presentation frame.",
                nameof(frame));
        }

        MapRenderOpenGlWireframePlan lowered =
            MapRenderOpenGlWireframePlanner.LowerNormalCamera(
                frame.FramePlan,
                resources,
                preparedHostViewProjection);
        _executor.ExecuteWireframe(frame.FramePlan, lowered, api);
    }

    public MapRenderOpenGlProcessedFloatZFrame ExecuteProcessedFloatZ(
        EditorPresentationFrame frame,
        float zNear)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(frame);
        if (!ReferenceEquals(frame, _activeFrame))
        {
            throw new ArgumentException(
                "FloatZ execution requires the exact active presentation frame.",
                nameof(frame));
        }

        _floatZ ??=
            new SilkMapRenderOpenGlNormalCameraFloatZBackend(
                _gl,
                _state,
                _source,
                _programs,
                _presenter.ErrorMonitor);
        _floatZ.Resize(
            frame.SceneTarget.Extent.LogicalWidth,
            frame.SceneTarget.Extent.LogicalHeight);
        return _floatZ.Execute(frame, zNear);
    }

    public MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult
        Present(
            EditorPresentationFrame frame,
            uint hostFramebuffer = 0)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(frame);
        if (!ReferenceEquals(frame, _activeFrame))
        {
            throw new ArgumentException(
                "Live Preview can present only its exact active target-2 frame.",
                nameof(frame));
        }

        try
        {
            return _presenter.Present(
                frame.Presentation,
                frame.HostFramebufferExtent,
                hostFramebuffer);
        }
        finally
        {
            _activeFrame = null;
        }
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        _disposed = true;
        _activeFrame = null;
        _extents = null;
        var failures = new List<Exception>();
        TryDispose(_floatZ, failures);
        TryDispose(_presenter, failures);
        TryDispose(_programs, failures);
        TryDispose(_targets, failures);
        _targets = null;
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "Live Preview normal-camera presentation-session disposal failed.",
                failures);
        }
    }

    private static bool SceneRequiresProcessedFloatZ(
        RenderSceneSnapshot sceneSnapshot) =>
        sceneSnapshot.NormalCameraDraws.PreparedPasses.Any(pass =>
            pass.ShaderProvenance.RuntimeSamplerRequirements.Any(
                requirement =>
                    requirement.ResourceKind ==
                        ShaderRuntimeSamplerResourceKind
                            .ProcessedFloatZ &&
                    requirement.Status ==
                        ShaderRuntimeSamplerRequirementStatus
                            .SameRevisionTextureRequired));

    private static void TryDispose(
        IDisposable? owner,
        ICollection<Exception> failures)
    {
        if (owner is null)
            return;
        try
        {
            owner.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private void EnsureUsableOnOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Live Preview presentation session may only be used and disposed on its owning render thread.");
        }
    }
}
