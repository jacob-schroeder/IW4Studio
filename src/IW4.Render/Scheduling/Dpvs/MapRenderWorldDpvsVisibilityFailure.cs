namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsVisibilityFailure(
    MapRenderWorldDpvsVisibilityFailureKind Kind,
    string Detail,
    MapRenderWorldDpvsViewIndex? ViewIndex = null,
    MapRenderWorldDpvsCameraCellFailureKind? CameraCellFailure = null,
    MapRenderWorldDpvsStaticCullFailureKind? StaticCullFailure = null,
    MapRenderWorldDpvsCameraSkyCullFailureKind? CameraSkyCullFailure = null,
    MapRenderWorldDpvsCameraTraversalFailureKind? CameraTraversalFailure = null,
    MapRenderWorldDpvsSunShadowFrameFailureKind? SunShadowFrameFailure = null,
    MapRenderWorldDpvsSunShadowTraversalFailureKind?
        SunShadowTraversalFailure = null);

