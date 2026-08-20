using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Metal.Native;
using IW4.Render.Metal.Pipelines;
using IW4.Render.Metal.Resources;
using IW4.Render.Metal.Targets;
using IW4.Render.Metal.Telemetry;
using IW4.Render.Resources;
using IW4.Render.Scheduling.Clear;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.SceneBuilding;

using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace IW4.Render.Metal;

/// <summary>
/// Native Apple Metal implementation of the IW4 live map renderer. The
/// renderer owns GPU resources and command submission; the desktop host owns
/// only the Cocoa window and its <see cref="MetalLayerHost"/>.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MetalMapRenderer : IMapRenderer
{
    private static readonly Vector3 NeutralClearColor =
        new(0.42f, 0.49f, 0.52f);

    private readonly MetalLayerHost _surface;
    private readonly MetalDepthStencilFormatSelection _depthStencilFormat;
    private readonly MetalFrameTargets _targets;
    private readonly MetalRenderStateCache _renderStates;
    private readonly MetalResourceCache _resources;
    private readonly MetalResourceCache _auxiliaryResources;
    private readonly MetalCommandBufferRing _commandBuffers;
    private readonly MetalGpuPassTimer _gpuPassTimer;
    private readonly MapRenderFrameTelemetry _telemetry = new();
    private RenderSceneSnapshot? _sceneSnapshot;
    private RenderNormalCameraDrawFrameOrderWorkspace? _drawOrder;
    private MetalNormalCameraPresentation? _presentation;
    private MapRenderEditorPreviewAtmospherePlan? _editorPreviewAtmosphere;
    private MapRenderSurfaceExtents _surfaceExtents;
    private long _frameIndex;
    private long _lastCompletedCpuFrameIndex = -1;
    private long _lastPresentedCpuFrameIndex = -1;
    private bool _loaded;
    private bool _disposed;

    public MetalMapRenderer(MetalLayerHost surface)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The native Metal renderer requires macOS.");
        }
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _depthStencilFormat = MetalDepthStencilFormatSelection.Select(
            surface.Device);
        _targets = new MetalFrameTargets(
            surface.Device,
            _depthStencilFormat);
        _renderStates = new MetalRenderStateCache(
            surface.Device,
            _depthStencilFormat);
        _resources = new MetalResourceCache(
            surface.Device,
            surface.CommandQueue,
            deferTextureResidencyByDefault: true);
        _auxiliaryResources = new MetalResourceCache(
            surface.Device,
            surface.CommandQueue);
        _commandBuffers = new MetalCommandBufferRing(surface.CommandQueue);
        _gpuPassTimer = new MetalGpuPassTimer(surface.Device);
        _captureReadbacks = new MetalCaptureReadbackRing(
            surface.Device,
            surface.CommandQueue);
        _surfaceExtents = MapRenderSurfaceExtents.Unified(
            Math.Max(1, surface.DrawablePixelWidth),
            Math.Max(1, surface.DrawablePixelHeight));
        _targets.Resize(
            _surfaceExtents.SceneTarget.Width,
            _surfaceExtents.SceneTarget.Height);
        ResizeCaptureHostOutput(
            _surfaceExtents.HostFramebuffer.Width,
            _surfaceExtents.HostFramebuffer.Height);
    }

    public bool EditorPreviewFogRenderingEnabled { get; set; } = true;

    public bool ShowTexturedGeometry { get; set; } = true;

    public bool ShowDiagnosticGeometry { get; set; }

    /// <summary>
    /// Shows the collision-wireframe overlay. Metal's native one-pixel lines
    /// match Apple's OpenGL 4.1 aliased-line capability clamp of 1..1; the
    /// shared 1.25-pixel semantic request therefore resolves to one physical
    /// pixel on both macOS backends.
    /// </summary>
    public bool ShowWireframe { get; set; }

    public MapRenderFrameTelemetrySnapshot FrameTelemetry =>
        _telemetry.CreateSnapshot();

    public void Load(MapRenderScene scene) =>
        Load(scene, sceneSnapshot: null);

    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot) =>
        LoadCore(
            scene,
            sceneSnapshot,
            initialCamera: null,
            initialAspectRatio: 0f);

    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot,
        RenderCamera initialCamera,
        float initialAspectRatio)
    {
        if (!(initialAspectRatio > 0f) || !float.IsFinite(initialAspectRatio))
            throw new ArgumentOutOfRangeException(nameof(initialAspectRatio));
        _ = initialCamera.Forward;
        LoadCore(
            scene,
            sceneSnapshot,
            initialCamera,
            initialAspectRatio);
    }

    public void Resize(int width, int height) =>
        Resize(MapRenderSurfaceExtents.Unified(width, height));

    public void Resize(MapRenderSurfaceExtents extents)
    {
        ThrowIfDisposed();
        if (!extents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(extents));
        if (extents == _surfaceExtents)
            return;

        InvalidateCaptureFrame();
        SynchronizeGpu();
        WaitForCaptureReadbacks();
        _surfaceExtents = extents;
        _targets.Resize(extents.SceneTarget.Width, extents.SceneTarget.Height);
        ResizeCaptureHostOutput(
            extents.HostFramebuffer.Width,
            extents.HostFramebuffer.Height);
        _presentation?.Resize(
            extents.SceneTarget.Width,
            extents.SceneTarget.Height,
            extents.HostFramebuffer.Width,
            extents.HostFramebuffer.Height);
    }

    public void Render(RenderCamera camera)
    {
        ThrowIfDisposed();
        if (!_loaded || _sceneSnapshot is null || _drawOrder is null ||
            _presentation is null)
            throw new InvalidOperationException("A map scene must be loaded before rendering.");
        if (!_targets.IsReady)
            return;

        InvalidateCaptureFrame();
        LastFramePlan = null;
        LastEditorPreviewPresentationResult = null;
        using var pool = new NSAutoreleasePool();
        long telemetryFrameIndex = _telemetry.BeginCpuFrame();
        MTLCommandBuffer commandBuffer = default;
        int commandSlot = -1;
        bool submitted = false;
        try
        {
            using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.FrameSetup))
            {
                DrainGpuTelemetry(telemetryFrameIndex);
                _telemetry.SetCounter(
                    MapRenderFrameCounter.SceneTargetWidth,
                    _surfaceExtents.SceneTarget.Width);
                _telemetry.SetCounter(
                    MapRenderFrameCounter.SceneTargetHeight,
                    _surfaceExtents.SceneTarget.Height);
                _telemetry.SetCounter(
                    MapRenderFrameCounter.HostFramebufferWidth,
                    _surfaceExtents.HostFramebuffer.Width);
                _telemetry.SetCounter(
                    MapRenderFrameCounter.HostFramebufferHeight,
                    _surfaceExtents.HostFramebuffer.Height);
                commandBuffer = _commandBuffers.Begin(telemetryFrameIndex);
                // Begin may have waited for and retired the slot being reused.
                // Collect that slot before its counter buffer is attached to
                // the new command buffer.
                DrainGpuTelemetry(telemetryFrameIndex);
                commandSlot = _commandBuffers.ResolveSlot(commandBuffer);
                _gpuPassTimer.BeginFrame(commandSlot, telemetryFrameIndex);
            }

            // One frame revision owns visibility, static lighting, shadows,
            // depth, FloatZ, and color. Shadow preparation publishes the
            // authoritative camera DPVS result before receiver admission, so
            // reset here and finalize the shared state after that publication.
            ResetNormalCameraFrameState();
            ResetNormalCameraVisibilityFrameState();
            ResetStaticModelLightingFrameState();
            ResetProcessedFloatZFrame();
            ResetNormalCameraTextureResidencyFrameState();

            EncodeShadowPasses(commandBuffer, camera);
            PrepareNormalCameraFrame(camera);
            bool requiresVisibleProcessedFloatZ =
                RequiresVisibleProcessedFloatZ(camera);

            MapRenderNormalCameraClearColorResult clearColor =
                CreateClearColor(camera);
            MetalNormalCameraFrameState plannedFrameState =
                PrepareNormalCameraFrameState(camera);
            LastFramePlan = RenderFramePlanner.CreateNormalCameraFrame(
                _frameIndex,
                _surfaceExtents,
                clearColor,
                _sceneSnapshot,
                camera,
                CreateFramePreviewSettings(
                    plannedFrameState.AnimationTimeSeconds));
            if (!clearColor.RequestsColorClear)
            {
                throw new InvalidOperationException(
                    "The Metal normal-camera target requires its exact color clear.");
            }

            MetalSceneRenderPassTimingSplit? sceneTimingSplit =
                _gpuPassTimer.RequiresSceneRenderPassIsolation()
                    ? new MetalSceneRenderPassTimingSplit(
                        commandBuffer,
                        _targets,
                        _renderStates,
                        _gpuPassTimer,
                        commandSlot,
                        _surfaceExtents)
                    : null;
            using MTLRenderPassDescriptor scenePass =
                _targets.CreateScenePass(
                    clearColor.Red,
                    clearColor.Green,
                    clearColor.Blue,
                    clearColor.Alpha,
                    preserveForFloatZ:
                        requiresVisibleProcessedFloatZ ||
                        sceneTimingSplit is not null);
            _gpuPassTimer.AttachPass(
                scenePass,
                commandSlot,
                MapRenderGpuPhase.SceneTarget);
            MTLRenderCommandEncoder sceneEncoder;
            using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.SceneTarget))
            {
                sceneEncoder =
                    commandBuffer.RenderCommandEncoder(scenePass);
                if (sceneEncoder.NativePtr == 0)
                    throw new InvalidOperationException("Metal could not begin the Scene pass.");
                sceneEncoder.SetViewport(new MTLViewport
                {
                    originX = 0,
                    originY = 0,
                    width = _surfaceExtents.SceneTarget.Width,
                    height = _surfaceExtents.SceneTarget.Height,
                    znear = 0,
                    zfar = 1
                });
                sceneEncoder.SetScissorRect(new MTLScissorRect
                {
                    x = 0,
                    y = 0,
                    width = checked((ulong)_surfaceExtents.SceneTarget.Width),
                    height = checked((ulong)_surfaceExtents.SceneTarget.Height)
                });
                _renderStates.ResetEncoderInheritance();
            }
            try
            {
                EncodeNormalCameraPreludeAndDepth(
                    ref sceneEncoder,
                    camera,
                    sceneTimingSplit);
                if (!requiresVisibleProcessedFloatZ)
                {
                    EncodeNormalCameraColorAndOverlays(
                        ref sceneEncoder,
                        camera,
                        sceneTimingSplit);
                    sceneTimingSplit?.Finish(ref sceneEncoder);
                }
            }
            finally
            {
                if (sceneEncoder.NativePtr != 0)
                    sceneEncoder.EndEncoding();
            }

            if (requiresVisibleProcessedFloatZ)
            {
                EncodeProcessedFloatZ(commandBuffer, camera);
                using MTLRenderPassDescriptor resumedScenePass =
                    _targets.CreateSceneResumePass(
                        resolveAtEnd: sceneTimingSplit is null);
                // SceneTarget may span both sides of the demand-gated FloatZ
                // encoder break. The sparse timer sums both disjoint target-2
                // intervals into one phase sample.
                _gpuPassTimer.AttachPass(
                    resumedScenePass,
                    commandSlot,
                    MapRenderGpuPhase.SceneTarget);
                MTLRenderCommandEncoder resumedSceneEncoder =
                    commandBuffer.RenderCommandEncoder(resumedScenePass);
                if (resumedSceneEncoder.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        "Metal could not resume the Scene pass after FloatZ.");
                }
                try
                {
                    resumedSceneEncoder.SetViewport(new MTLViewport
                    {
                        originX = 0,
                        originY = 0,
                        width = _surfaceExtents.SceneTarget.Width,
                        height = _surfaceExtents.SceneTarget.Height,
                        znear = 0,
                        zfar = 1
                    });
                    resumedSceneEncoder.SetScissorRect(new MTLScissorRect
                    {
                        x = 0,
                        y = 0,
                        width = checked((ulong)_surfaceExtents.SceneTarget.Width),
                        height = checked((ulong)_surfaceExtents.SceneTarget.Height)
                    });
                    _renderStates.ResetEncoderInheritance();
                    sceneTimingSplit?.ResumeAfterExternalEncoderBreak();
                    EncodeNormalCameraColorAndOverlays(
                        ref resumedSceneEncoder,
                        camera,
                        sceneTimingSplit);
                    sceneTimingSplit?.Finish(ref resumedSceneEncoder);
                }
                finally
                {
                    if (resumedSceneEncoder.NativePtr != 0)
                        resumedSceneEncoder.EndEncoding();
                }
            }

            // Acquire as late as possible so drawable ownership does not
            // throttle scene encoding or resource preparation.
            CAMetalDrawable? drawable;
            using (_telemetry.BeginCpuPhase(
                       MapRenderCpuPhase.SwapOrPresent))
            {
                drawable = _surface.AcquireDrawable();
            }
            if (drawable is null)
            {
                AbandonShadowPasses();
                _gpuPassTimer.Abandon(commandSlot, telemetryFrameIndex);
                _commandBuffers.Abandon(commandBuffer);
                commandBuffer = default;
                return;
            }
            using CAMetalDrawable ownedDrawable = drawable.Value;

            using (_telemetry.BeginCpuPhase(MapRenderCpuPhase.Presentation))
            {
                MTLTexture drawableTexture = ownedDrawable.Texture;
                MTLTexture hostOutput = CaptureHostOutput;
                MetalNormalCameraPresentationExecutionResult
                    presentation = _presentation.Encode(
                        commandBuffer,
                        _targets.ResolvedColor,
                        hostOutput,
                        _renderStates,
                        pass => _gpuPassTimer.AttachPass(
                            pass,
                            commandSlot,
                            MapRenderGpuPhase.Presentation));
                int postDrawCount = presentation.FullscreenDrawCount;
                long postTriangleCount = checked(postDrawCount * 2L);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.DrawCalls,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.LogicalDrawCommands,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.Triangles,
                    postTriangleCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.Passes,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.PostPasses,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.PostLogicalDrawCommands,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.ProgramChanges,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.RenderStateChanges,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.BufferChanges,
                    checked(postDrawCount * 5L));
                _telemetry.AddCounter(
                    MapRenderFrameCounter.UniformUpdates,
                    checked(postDrawCount * 3L));
                _telemetry.AddCounter(
                    MapRenderFrameCounter.TextureChanges,
                    postDrawCount);
                _telemetry.AddCounter(
                    MapRenderFrameCounter.SamplerChanges,
                    postDrawCount);
                _telemetry.AddGpuPhaseWork(
                    MapRenderGpuPhase.Presentation,
                    postDrawCount,
                    postTriangleCount);
                // Retain the canonical postprocessed pixels independently of
                // the disposable drawable. The ordered GPU copy feeds the
                // drawable with those exact bytes before this same command
                // buffer makes them eligible for presentation.
                EncodeCaptureHostHandoff(commandBuffer, drawableTexture);
                commandBuffer.PresentDrawable(ownedDrawable);
                commandBuffer.Commit();
                // Commit transfers command-buffer ownership to Metal. Mark
                // that boundary before publishing renderer-owned revisions so
                // a later publication failure never abandons committed work.
                submitted = true;
                CommitShadowPasses();
                PublishCaptureFrame(telemetryFrameIndex, commandBuffer);
                LastEditorPreviewPresentationResult = new(
                    LastFramePlan ?? throw new InvalidOperationException(
                        "Metal presentation completed without its frame plan."),
                    UsesFilmColorManipulation:
                        _presentation.UsesFilmColorManipulation,
                    UsesGlow: _presentation.UsesGlow,
                    ResolvedStoredSamplePair: true,
                    ExecutedFilmColorManipulation:
                        presentation.ExecutedFilmColorManipulation,
                    ExecutedTranslatedPostFx: true,
                    ExecutedGlow: presentation.ExecutedGlow,
                    GlowGaussianPassCount:
                        presentation.GlowGaussianPassCount,
                    FullscreenDrawCount:
                        presentation.FullscreenDrawCount,
                    WroteCurrentHostBackBuffer: true);
                _frameIndex++;
            }
        }
        catch
        {
            AbandonShadowPasses();
            if (!submitted && commandBuffer.NativePtr != 0)
            {
                _gpuPassTimer.Abandon(commandSlot, telemetryFrameIndex);
                _commandBuffers.Abandon(commandBuffer);
            }
            throw;
        }
        finally
        {
            MapRenderCpuFrameTiming completed = _telemetry.EndCpuFrame();
            if (submitted)
                _lastCompletedCpuFrameIndex = completed.FrameIndex;
        }
    }

    public void RecordPresentedFrame()
    {
        ThrowIfDisposed();
        long frameIndex = _lastCompletedCpuFrameIndex;
        if (frameIndex < 0 || frameIndex == _lastPresentedCpuFrameIndex)
            return;
        _telemetry.RecordPresentedFrame(frameIndex);
        _lastPresentedCpuFrameIndex = frameIndex;
    }

    public bool IsStartupWorkingSetSettled(RenderCamera requestedCamera)
    {
        ThrowIfDisposed();
        return _loaded &&
            _lastCompletedCpuFrameIndex >= 0 &&
            _lastPresentedCpuFrameIndex == _lastCompletedCpuFrameIndex &&
            IsNormalCameraTextureWorkingSetSettled(requestedCamera) &&
            IsProgressiveStaticWorkingSetSettled(requestedCamera);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        InvalidateCaptureFrame();
        // Retire every writer before releasing the retained host target, then
        // retire any later same-queue capture copies which read that target.
        try
        {
            _commandBuffers.Dispose();
        }
        finally
        {
            DisposeCaptureResources();
        }
        _gpuPassTimer.Dispose();
        DeleteSceneResources();
        _auxiliaryResources.Dispose();
        _resources.Dispose();
        _renderStates.Dispose();
        _targets.Dispose();
    }

    private void LoadCore(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot,
        RenderCamera? initialCamera,
        float initialAspectRatio)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scene);
        bool isolateWorldSurface = IsolatedWorldSurfaceIndex.HasValue;
        if (sceneSnapshot is not null &&
            !string.Equals(sceneSnapshot.Name, scene.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The prebuilt render snapshot belongs to another map scene.",
                nameof(sceneSnapshot));
        }
        if (isolateWorldSurface && sceneSnapshot is not null)
        {
            throw new ArgumentException(
                "An isolated world-surface render requires a snapshot without diagnostic geometry.",
                nameof(sceneSnapshot));
        }

        InvalidateCaptureFrame();
        SynchronizeGpu();
        WaitForCaptureReadbacks();
        DeleteSceneResources();
        _loadedIsolatedWorldSurfaceIndex = IsolatedWorldSurfaceIndex;
        // Callers may omit the complete normal-camera inventory. Materialize
        // the full neutral snapshot only when the supplied snapshot lacks the
        // production draw contract.
        sceneSnapshot ??= RenderSceneSnapshotBuilder.Create(
            scene,
            revision: 0,
            includeDiagnosticGeometry: !isolateWorldSurface);
        if (sceneSnapshot.NormalCameraDraws.PreparedPasses.IsEmpty &&
            (scene.TexturedBatches.Count != 0 ||
             scene.StaticModelLodTexturedBatches.Count != 0 ||
             scene.InstancedTexturedBatches.Count != 0))
        {
            sceneSnapshot = RenderSceneSnapshotBuilder.Create(
                scene,
                sceneSnapshot.Revision,
                includeDiagnosticGeometry: !isolateWorldSurface);
        }

        _sceneSnapshot = sceneSnapshot;
        _drawOrder = sceneSnapshot.NormalCameraDraws.CreateFrameOrderWorkspace();
        _editorPreviewAtmosphere = scene.EditorPreviewAtmosphere ??
            MapRenderEditorPreviewAtmospherePlanner.Create(scene.Bounds);
        // Match OpenGL's two load contracts. Without an initial view every
        // authorized resource is eager; with one, only its bounded working
        // set is resident. Isolation remains a world-only deferred view.
        bool usesProgressiveResourceResidency =
            initialCamera.HasValue && !isolateWorldSurface;
        bool usesVisibilityDrivenResourceResidency =
            usesProgressiveResourceResidency || isolateWorldSurface;
        if (usesVisibilityDrivenResourceResidency)
            ConfigureProgressiveStaticResourceOwnership(sceneSnapshot);
        ConfigureNormalCameraTextureResidency(
            usesVisibilityDrivenResourceResidency);
        CreateSceneResources(
            scene,
            sceneSnapshot,
            deferNormalCameraTextureResidency:
                usesVisibilityDrivenResourceResidency);
        InitializeProgressiveStaticAdmission(
            sceneSnapshot,
            usesProgressiveResourceResidency);
        if (usesProgressiveResourceResidency &&
            initialCamera is { } progressiveInitialCamera)
        {
            PrefetchInitialProgressiveStaticNeighborhood(
                progressiveInitialCamera,
                initialAspectRatio);
        }
        _frameIndex = 0;
        _lastCompletedCpuFrameIndex = -1;
        _lastPresentedCpuFrameIndex = -1;
        _loaded = true;
    }

    private void DrainGpuTelemetry(long currentCpuFrameIndex)
    {
        _commandBuffers.DrainCompleted();
        while (_commandBuffers.TryDequeueGpuTiming(
                   out long gpuFrameIndex,
                   out double milliseconds,
                   out bool hasFrameTiming,
                   out int slotIndex))
        {
            ulong nanoseconds = checked((ulong)Math.Round(
                Math.Max(0.0, milliseconds) * 1_000_000.0));
            int delay = checked((int)Math.Clamp(
                currentCpuFrameIndex - gpuFrameIndex,
                0,
                int.MaxValue));
            if (_gpuPassTimer.TryCollect(
                    slotIndex,
                    gpuFrameIndex,
                    delay,
                    out MapRenderOpenGlGpuPhaseTiming phaseTiming))
            {
                _telemetry.RecordGpuPhaseTiming(phaseTiming);
            }
            if (hasFrameTiming)
            {
                _telemetry.RecordGpuFrameTiming(
                    new MapRenderOpenGlGpuFrameTiming(
                        gpuFrameIndex,
                        nanoseconds,
                        delay));
            }
        }
    }

    private void SynchronizeGpu()
    {
        long nextCpuFrameIndex = Math.Max(0, _lastCompletedCpuFrameIndex + 1);
        DrainGpuTelemetry(nextCpuFrameIndex);
        _commandBuffers.WaitForIdle();
        DrainGpuTelemetry(nextCpuFrameIndex);
    }

    private void CreateSceneResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot,
        bool deferNormalCameraTextureResidency)
    {
        try
        {
            _resources.Load(
                snapshot.NormalCameraDraws.Resources,
                deferNormalCameraTextureResidency);
            _auxiliaryResources.Load(snapshot.Resources);
            CreateShadowResources(scene);
            CreateNormalCameraPresentation(scene);
            CreateNormalCameraResources(scene, snapshot);
            CreateDepthPrepassResources(scene, snapshot);
            CreateNormalCameraVisibilityResources(scene, snapshot);
            CreateStaticModelLightingResources(scene, snapshot);
            CreateAuxiliarySceneResources(scene, snapshot);
        }
        catch
        {
            DeleteAuxiliarySceneResources();
            DeleteStaticModelLightingResources();
            DeleteNormalCameraVisibilityResources();
            DeleteDepthPrepassResources();
            DeleteNormalCameraResources();
            DeleteNormalCameraPresentation();
            DeleteShadowResources();
            _auxiliaryResources.Clear();
            _resources.Clear();
            throw;
        }
    }

    private void DeleteSceneResources()
    {
        DeleteAuxiliarySceneResources();
        DeleteStaticModelLightingResources();
        DeleteNormalCameraVisibilityResources();
        DeleteDepthPrepassResources();
        DeleteNormalCameraResources();
        DeleteNormalCameraPresentation();
        DeleteShadowResources();
        _auxiliaryResources.Clear();
        _resources.Clear();
        ResetProgressiveStaticAdmission();
        ResetNormalCameraTextureResidency();
        ConfigureNormalCameraTextureResidency(
            visibilityDriven: false);
        _sceneSnapshot = null;
        _drawOrder = null;
        _editorPreviewAtmosphere = null;
        _loadedIsolatedWorldSurfaceIndex = null;
        LastFramePlan = null;
        LastEditorPreviewPresentationResult = null;
        _loaded = false;
    }

    private void CreateNormalCameraPresentation(MapRenderScene scene)
    {
        MapRenderWorldSceneSource source = scene.WorldSource ??
            throw new InvalidOperationException(
                "The native Metal presentation requires the map's canonical world-scene source.");
        var replacement = new MetalNormalCameraPresentation(
            _surface.Device,
            source,
            scene.EditorPreviewEffectivePost,
            _surfaceExtents.SceneTarget.Width,
            _surfaceExtents.SceneTarget.Height,
            _surfaceExtents.HostFramebuffer.Width,
            _surfaceExtents.HostFramebuffer.Height);
        try
        {
            replacement.PrepareRenderStates(_renderStates);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
        _presentation = replacement;
    }

    private void DeleteNormalCameraPresentation()
    {
        _presentation?.Dispose();
        _presentation = null;
    }

    private MapRenderNormalCameraClearColorResult CreateClearColor(
        RenderCamera camera)
    {
        MapRenderEditorPreviewAtmospherePlan? atmosphere =
            _editorPreviewAtmosphere;
        Vector3 fallback =
            EditorPreviewFogRenderingEnabled &&
            atmosphere?.IsEnabled == true
                ? atmosphere.FogColor
                : NeutralClearColor;
        return MapRenderNormalCameraClearColorProducer.ProduceEditorPreview(
            camera.FarPlane,
            fallback,
            EditorPreviewFogRenderingEnabled
                ? _normalCameraActiveFog
                : null);
    }

    private void PrepareNormalCameraFrame(RenderCamera camera)
    {
        PublishNormalCameraInventoryCounters();
        if (ShowTexturedGeometry)
        {
            PrepareNormalCameraVisibility(camera);
            PrepareStaticModelLighting();
            using (_telemetry.BeginCpuPhase(
                       MapRenderCpuPhase.StaticResourceAdmission))
            {
                PrepareProgressiveStaticAdmission(camera);
            }
        }
        else
        {
            SetProgressiveStaticAdmissionInactive(camera);
        }
        PrepareNormalCameraTextureResidency(camera);
    }

    private void EncodeNormalCameraPreludeAndDepth(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera,
        MetalSceneRenderPassTimingSplit? timingSplit)
    {
        EncodeNormalCameraAuxiliaryPrelude(
            ref encoder,
            camera,
            timingSplit);
        if (ShowTexturedGeometry && _orderedDepthPrepassGroups.Length != 0)
        {
            timingSplit?.Transition(
                ref encoder,
                MapRenderGpuPhase.DepthPrepass);
            EncodeNormalCameraDepthPrepass(encoder, camera);
        }
    }

    private void EncodeNormalCameraColorAndOverlays(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera,
        MetalSceneRenderPassTimingSplit? timingSplit)
    {
        if (ShowTexturedGeometry)
            EncodeNormalCameraDraws(ref encoder, camera, timingSplit);
        EncodeNormalCameraOverlays(ref encoder, camera, timingSplit);
    }

    partial void CreateNormalCameraResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot);

    partial void DeleteNormalCameraResources();

    partial void EncodeNormalCameraDraws(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera,
        MetalSceneRenderPassTimingSplit? timingSplit);

    partial void CreateDepthPrepassResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot);

    partial void DeleteDepthPrepassResources();

    partial void EncodeNormalCameraDepthPrepass(
        MTLRenderCommandEncoder encoder,
        RenderCamera camera);

    partial void CreateAuxiliarySceneResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot);

    partial void DeleteAuxiliarySceneResources();

    partial void EncodeNormalCameraAuxiliaryPrelude(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera,
        MetalSceneRenderPassTimingSplit? timingSplit);

    partial void EncodeNormalCameraOverlays(
        ref MTLRenderCommandEncoder encoder,
        RenderCamera camera,
        MetalSceneRenderPassTimingSplit? timingSplit);

    /// <summary>
    /// Sparse stage-boundary timing fallback for Apple devices that do not
    /// expose draw-boundary counters. It preserves target 2 between encoders
    /// and resolves it only after the normal-camera stream is complete.
    /// </summary>
    private sealed class MetalSceneRenderPassTimingSplit
    {
        private readonly MTLCommandBuffer _commandBuffer;
        private readonly MetalFrameTargets _targets;
        private readonly MetalRenderStateCache _renderStates;
        private readonly MetalGpuPassTimer _timer;
        private readonly int _commandSlot;
        private readonly MapRenderSurfaceExtents _extents;
        private bool _isolating;

        internal MetalSceneRenderPassTimingSplit(
            MTLCommandBuffer commandBuffer,
            MetalFrameTargets targets,
            MetalRenderStateCache renderStates,
            MetalGpuPassTimer timer,
            int commandSlot,
            MapRenderSurfaceExtents extents)
        {
            _commandBuffer = commandBuffer;
            _targets = targets;
            _renderStates = renderStates;
            _timer = timer;
            _commandSlot = commandSlot;
            _extents = extents;
        }

        /// <summary>
        /// Reopens target 2 only when crossing into or out of the one phase
        /// selected for this sparse frame. Repeated non-contiguous runs of a
        /// selected draw phase are independently isolated and summed by the
        /// timer, so authored draw order remains unchanged.
        /// </summary>
        internal bool Transition(
            ref MTLRenderCommandEncoder encoder,
            MapRenderGpuPhase phase)
        {
            bool shouldIsolate =
                _timer.RequiresSceneRenderPassIsolation(phase);
            if (shouldIsolate == _isolating)
                return false;

            encoder.EndEncoding();
            encoder = default;
            using MTLRenderPassDescriptor pass =
                _targets.CreateSceneResumePass(resolveAtEnd: false);
            if (shouldIsolate)
                _timer.AttachPass(pass, _commandSlot, phase);
            encoder = BeginEncoder(pass);
            _isolating = shouldIsolate;
            return true;
        }

        /// <summary>
        /// Performs the one final multisample resolve for an isolated frame.
        /// The empty terminal encoder is intentionally sparse-only and keeps
        /// every measured encoder store/load exact without resolving early.
        /// </summary>
        internal void Finish(ref MTLRenderCommandEncoder encoder)
        {
            encoder.EndEncoding();
            encoder = default;
            using MTLRenderPassDescriptor pass =
                _targets.CreateSceneResumePass(resolveAtEnd: true);
            MTLRenderCommandEncoder resolveEncoder = BeginEncoder(pass);
            resolveEncoder.EndEncoding();
            encoder = default;
            _isolating = false;
        }

        /// <summary>
        /// FloatZ owns its own natural command-encoder break. Its resumed
        /// target-2 encoder is not a timed scene-phase pass, even when the
        /// pre-FloatZ encoder happened to be isolated.
        /// </summary>
        internal void ResumeAfterExternalEncoderBreak() =>
            _isolating = false;

        private MTLRenderCommandEncoder BeginEncoder(
            MTLRenderPassDescriptor pass)
        {
            MTLRenderCommandEncoder encoder =
                _commandBuffer.RenderCommandEncoder(pass);
            if (encoder.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal could not resume the sparse scene timing pass.");
            }
            encoder.SetViewport(new MTLViewport
            {
                originX = 0,
                originY = 0,
                width = _extents.SceneTarget.Width,
                height = _extents.SceneTarget.Height,
                znear = 0,
                zfar = 1
            });
            encoder.SetScissorRect(new MTLScissorRect
            {
                x = 0,
                y = 0,
                width = checked((ulong)_extents.SceneTarget.Width),
                height = checked((ulong)_extents.SceneTarget.Height)
            });
            _renderStates.ResetEncoderInheritance();
            return encoder;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
