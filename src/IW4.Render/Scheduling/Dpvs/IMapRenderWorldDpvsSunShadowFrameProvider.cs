using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Operational boundary for the two partition plane records published by PS3
/// R_SetupSunShadowMaps. Implementations must derive current-frame planes from
/// native setup state; captures and synthesized empty/default planes do not
/// satisfy this contract.
/// </summary>
public interface IMapRenderWorldDpvsSunShadowFrameProvider
{
    string ProducerIdentity { get; }

    long SourceRevision { get; }

    MapRenderWorldDpvsSunShadowFrameBuildResult Build(
        GfxWorldAsset world,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame);
}

