using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Clear;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Strict PS3 normal-camera DPVS composition. Camera view zero is produced by
/// portal traversal; views one and two are produced from one exact
/// R_SetupSunShadowMaps frame. Missing upstream state fails closed.
/// </summary>
public sealed class MapRenderWorldDpvsNormalCameraVisibilityProvider :
    IMapRenderWorldDpvsNormalCameraVisibilityProvider
{
    private readonly IMapRenderWorldDpvsSunShadowFrameProvider
        _sunShadowFrameProvider;
    private readonly MapRenderWorldDpvsPortalTraversalSettings
        _portalTraversalSettings;
    private readonly object _workingSetGate = new();
    private MapRenderWorldDpvsWorkingSet? _workingSet;

    public MapRenderWorldDpvsNormalCameraVisibilityProvider(
        string producerIdentity,
        IMapRenderWorldDpvsSunShadowFrameProvider sunShadowFrameProvider,
        MapRenderWorldDpvsPortalTraversalSettings? portalTraversalSettings =
            null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        ArgumentNullException.ThrowIfNull(sunShadowFrameProvider);

        ProducerIdentity = producerIdentity;
        _sunShadowFrameProvider = sunShadowFrameProvider;
        _portalTraversalSettings = portalTraversalSettings ??
            MapRenderWorldDpvsPortalTraversalSettings.Ps3Default;
    }

    public string ProducerIdentity { get; }

    public long SourceRevision => _sunShadowFrameProvider.SourceRevision;

    public MapRenderWorldDpvsVisibilityBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(farPlane);
        lock (_workingSetGate)
        {
            if (_workingSet is null ||
                !ReferenceEquals(_workingSet.Topology.World, world))
            {
                _workingSet = new(world);
            }
            return BuildCore(
                world,
                camera,
                framebufferExtent,
                farPlane,
                _workingSet);
        }
    }

    private MapRenderWorldDpvsVisibilityBuildResult BuildCore(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane,
        MapRenderWorldDpvsWorkingSet workingSet)
    {
        MapRenderWorldDpvsCameraTraversalBuildResult cameraResult =
            MapRenderWorldDpvsCameraTraversalProducer.Build(
                world,
                camera,
                framebufferExtent,
                farPlane,
                _portalTraversalSettings,
                workingSet);
        if (!cameraResult.IsSuccess)
        {
            MapRenderWorldDpvsCameraTraversalFailure failure =
                cameraResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind.CameraTraversalFailed,
                failure.Detail,
                MapRenderWorldDpvsViewIndex.Camera,
                CameraTraversalFailure: failure.Kind));
        }

        string frameProducerIdentity =
            _sunShadowFrameProvider.ProducerIdentity;
        long frameSourceRevision = _sunShadowFrameProvider.SourceRevision;
        if (string.IsNullOrWhiteSpace(frameProducerIdentity) ||
            frameSourceRevision < 0)
        {
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowFrameProviderContractViolated,
                $"producer={frameProducerIdentity ?? "<null>"};revision={frameSourceRevision}"));
        }

        MapRenderWorldDpvsSunShadowFrameBuildResult? frameResult =
            _sunShadowFrameProvider.Build(
                world,
                cameraResult.Traversal!.CameraFrame);
        if (frameResult is null)
        {
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowFrameProviderContractViolated,
                $"Sun-shadow provider '{frameProducerIdentity}' returned no typed result."));
        }
        if (!string.Equals(
                frameProducerIdentity,
                _sunShadowFrameProvider.ProducerIdentity,
                StringComparison.Ordinal) ||
            frameSourceRevision != _sunShadowFrameProvider.SourceRevision)
        {
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowFrameProviderContractViolated,
                "Sun-shadow frame provider identity or source revision changed during Build."));
        }
        if (!frameResult.IsSuccess)
        {
            MapRenderWorldDpvsSunShadowFrameFailure failure =
                frameResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowFrameBuildFailed,
                failure.Detail,
                failure.ViewIndex,
                SunShadowFrameFailure: failure.Kind));
        }

        MapRenderWorldDpvsSunShadowFrame frame = frameResult.Frame!;
        if (!string.Equals(
                frame.ProducerIdentity,
                frameProducerIdentity,
                StringComparison.Ordinal) ||
            frame.SourceRevision != frameSourceRevision)
        {
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowFrameProviderContractViolated,
                $"provider={frameProducerIdentity}@{frameSourceRevision};frame={frame.ProducerIdentity}@{frame.SourceRevision}"));
        }

        MapRenderWorldDpvsSunShadowTraversalBuildResult shadowResult =
            MapRenderWorldDpvsSunShadowTraversalProducer.Build(
                world,
                frame,
                workingSet);
        if (!shadowResult.IsSuccess)
        {
            MapRenderWorldDpvsSunShadowTraversalFailure failure =
                shadowResult.Failure!;
            return Failed(new(
                MapRenderWorldDpvsVisibilityFailureKind
                    .SunShadowTraversalFailed,
                failure.Detail,
                failure.ViewIndex,
                SunShadowTraversalFailure: failure.Kind));
        }

        return MapRenderWorldDpvsVisibilityProducer.Build(
            world,
            camera,
            cameraResult.Traversal,
            shadowResult.Traversal!,
            frame.Projection,
            workingSet);
    }

    private static MapRenderWorldDpvsVisibilityBuildResult Failed(
        MapRenderWorldDpvsVisibilityFailure failure) => new(
            null,
            null,
            [],
            [failure]);
}
