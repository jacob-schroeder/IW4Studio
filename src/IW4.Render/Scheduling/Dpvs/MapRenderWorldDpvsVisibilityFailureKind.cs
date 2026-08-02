namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsVisibilityFailureKind
{
    CameraCellResolutionFailed = 0,
    CameraPortalCommandSetUnavailable,
    CameraSkyCullInputUnavailable,
    SunShadowPartition0CommandSetUnavailable,
    SunShadowPartition1CommandSetUnavailable,
    CommandSetRoleMismatch,
    CameraPortalStartCellMismatch,
    StaticCullFailed,
    CameraSkyCullFailed,
    CameraTraversalFailed,
    SunShadowFrameBuildFailed,
    SunShadowTraversalFailed,
    SunShadowFrameProviderContractViolated
}

