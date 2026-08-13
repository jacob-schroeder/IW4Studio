using IW4.Assets.Assets.GfxMap;
using IW4.Render.Scheduling.Clear;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Operational current-view DPVS boundary for one normal-camera frame.
/// Implementations must execute the camera portal/sky path and both native
/// sun-shadow view traversals. Diagnostic captures and authored fastfile
/// visibility arrays are not operational implementations of this contract.
/// </summary>
public interface IMapRenderWorldDpvsNormalCameraVisibilityProvider
{
    string ProducerIdentity { get; }

    long SourceRevision { get; }

    MapRenderWorldDpvsVisibilityBuildResult Build(
        GfxWorldAsset world,
        RenderCamera camera,
        MapRenderNormalCameraFramebufferExtent framebufferExtent,
        MapRenderNormalCameraFarPlaneState farPlane);
}

