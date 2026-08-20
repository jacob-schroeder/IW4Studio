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
        _renderStates = new MetalRenderStateCache(surface.Device);
        _resources = new MetalResourceCache(
            surface.Device,
            surface.CommandQueue);
        _auxiliaryResources = new MetalResourceCache(
            surface.Device,
            surface.CommandQueue);
        _commandBuffers = new MetalCommandBufferRing(surface.CommandQueue);
        _gpuPassTimer = new MetalGpuPassTimer(surface.Device);
        _surfaceExtents = MapRenderSurfaceExtents.Unified(
            Math.Max(1, surface.DrawablePixelWidth),
            Math.Max(1, surface.DrawablePixelHeight));
        _targets.Resize(
            _surfaceExtents.SceneTarget.Width,
            _surfaceExtents.SceneTarget.Height);
    }

    public bool EditorPreviewFogRenderingEnabled { get; set; } = true;

    public bool ShowTexturedGeometry { get; set; } = true;

    public bool ShowDiagnosticGeometry { get; set; }

    /// <summary>
    /// Shows the collision-wireframe overlay. Metal exposes fixed one-pixel
    /// native lines, so the legacy OpenGL 1.25-pixel hint is bounded to one
    /// pixel until the overlay is lowered to expanded line geometry.
    /// </summary>
    public bool ShowWireframe { get; set; }

    public MapRenderFrameTelemetrySnapshot FrameTelemetry =>
        _telemetry.CreateSnapshot();

    public void Load(MapRenderScene scene) =>
        Load(scene, sceneSnapshot: null);

    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot) =>
        LoadCore(scene, sceneSnapshot);

    public void Load(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot,
        RenderCamera initialCamera,
        float initialAspectRatio)
    {
        if (!(initialAspectRatio > 0f) || !float.IsFinite(initialAspectRatio))
            throw new ArgumentOutOfRangeException(nameof(initialAspectRatio));
        _ = initialCamera.Forward;
        LoadCore(scene, sceneSnapshot);
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

        SynchronizeGpu();
        _surfaceExtents = extents;
        _targets.Resize(extents.SceneTarget.Width, extents.SceneTarget.Height);
        _presentation?.Resize(
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

            EncodeShadowPasses(commandBuffer, camera);

            MapRenderNormalCameraClearColorResult clearColor =
                CreateClearColor(camera);
            if (!clearColor.RequestsColorClear)
            {
                throw new InvalidOperationException(
                    "The Metal normal-camera target requires its exact color clear.");
            }

            using MTLRenderPassDescriptor scenePass =
                _targets.CreateScenePass(
                    clearColor.Red,
                    clearColor.Green,
                    clearColor.Blue,
                    clearColor.Alpha);
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
                EncodeNormalCamera(sceneEncoder, camera);
            }
            finally
            {
                sceneEncoder.EndEncoding();
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
                using MTLRenderPassDescriptor presentationPass =
                    MetalFrameTargets.CreatePresentationPass(drawableTexture);
                _gpuPassTimer.AttachPass(
                    presentationPass,
                    commandSlot,
                    MapRenderGpuPhase.Presentation);
                MTLRenderCommandEncoder presentationEncoder =
                    commandBuffer.RenderCommandEncoder(presentationPass);
                if (presentationEncoder.NativePtr == 0)
                    throw new InvalidOperationException("Metal could not begin presentation.");
                try
                {
                    presentationEncoder.SetViewport(new MTLViewport
                    {
                        originX = 0,
                        originY = 0,
                        width = drawableTexture.Width,
                        height = drawableTexture.Height,
                        znear = 0,
                        zfar = 1
                    });
                    presentationEncoder.SetScissorRect(new MTLScissorRect
                    {
                        x = 0,
                        y = 0,
                        width = drawableTexture.Width,
                        height = drawableTexture.Height
                    });
                    _renderStates.ResetEncoderInheritance();
                    _presentation.Encode(
                        presentationEncoder,
                        _targets.ResolvedColor,
                        _renderStates);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.DrawCalls);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.LogicalDrawCommands);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.Triangles,
                        2);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.Passes);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.PostPasses);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.PostLogicalDrawCommands);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.ProgramChanges);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.RenderStateChanges);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.BufferChanges,
                        5);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.UniformUpdates,
                        3);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.TextureChanges);
                    _telemetry.AddCounter(
                        MapRenderFrameCounter.SamplerChanges);
                    _telemetry.AddGpuPhaseWork(
                        MapRenderGpuPhase.Presentation,
                        drawCalls: 1,
                        triangles: 2);
                }
                finally
                {
                    presentationEncoder.EndEncoding();
                }
                commandBuffer.PresentDrawable(ownedDrawable);
                commandBuffer.Commit();
                CommitShadowPasses();
                submitted = true;
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
        _ = requestedCamera;
        return _loaded && _lastCompletedCpuFrameIndex >= 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _commandBuffers.Dispose();
        _gpuPassTimer.Dispose();
        DeleteSceneResources();
        _auxiliaryResources.Dispose();
        _resources.Dispose();
        _renderStates.Dispose();
        _targets.Dispose();
    }

    private void LoadCore(
        MapRenderScene scene,
        RenderSceneSnapshot? sceneSnapshot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scene);
        if (sceneSnapshot is not null &&
            !string.Equals(sceneSnapshot.Name, scene.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The prebuilt render snapshot belongs to another map scene.",
                nameof(sceneSnapshot));
        }

        SynchronizeGpu();
        DeleteSceneResources();
        // Callers may omit the complete normal-camera inventory. Materialize
        // the full neutral snapshot only when the supplied snapshot lacks the
        // production draw contract.
        sceneSnapshot ??= RenderSceneSnapshotBuilder.Create(scene);
        if (sceneSnapshot.NormalCameraDraws.PreparedPasses.IsEmpty &&
            (scene.TexturedBatches.Count != 0 ||
             scene.StaticModelLodTexturedBatches.Count != 0 ||
             scene.InstancedTexturedBatches.Count != 0))
        {
            sceneSnapshot = RenderSceneSnapshotBuilder.Create(
                scene,
                sceneSnapshot.Revision);
        }

        _sceneSnapshot = sceneSnapshot;
        _drawOrder = sceneSnapshot.NormalCameraDraws.CreateFrameOrderWorkspace();
        _editorPreviewAtmosphere = scene.EditorPreviewAtmosphere ??
            MapRenderEditorPreviewAtmospherePlanner.Create(scene.Bounds);
        CreateSceneResources(scene, sceneSnapshot);
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
        RenderSceneSnapshot snapshot)
    {
        try
        {
            _resources.Load(snapshot.NormalCameraDraws.Resources);
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
        _sceneSnapshot = null;
        _drawOrder = null;
        _editorPreviewAtmosphere = null;
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
            _surfaceExtents.HostFramebuffer.Width,
            _surfaceExtents.HostFramebuffer.Height);
        try
        {
            _ = _renderStates.GetOrCreate(replacement.RenderState);
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

    private void EncodeNormalCamera(
        MTLRenderCommandEncoder encoder,
        RenderCamera camera)
    {
        ResetNormalCameraFrameState();
        ResetNormalCameraVisibilityFrameState();
        ResetStaticModelLightingFrameState();
        if (ShowTexturedGeometry)
        {
            PrepareNormalCameraVisibility(camera);
            PrepareStaticModelLighting();
        }
        EncodeNormalCameraAuxiliaryPrelude(encoder, camera);
        if (ShowTexturedGeometry)
            EncodeNormalCameraDepthPrepass(encoder, camera);
        if (ShowTexturedGeometry)
            EncodeNormalCameraDraws(encoder, camera);
        EncodeNormalCameraOverlays(encoder, camera);
    }

    partial void CreateNormalCameraResources(
        MapRenderScene scene,
        RenderSceneSnapshot snapshot);

    partial void DeleteNormalCameraResources();

    partial void EncodeNormalCameraDraws(
        MTLRenderCommandEncoder encoder,
        RenderCamera camera);

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
        MTLRenderCommandEncoder encoder,
        RenderCamera camera);

    partial void EncodeNormalCameraOverlays(
        MTLRenderCommandEncoder encoder,
        RenderCamera camera);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
