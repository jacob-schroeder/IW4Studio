namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsCameraOnlyVisibilityFailureKind
{
    CameraTraversalFailed = 0,
    StaticCullFailed = 1,
    CameraSkyCullFailed = 2
}

public sealed record MapRenderWorldDpvsCameraOnlyVisibilityFailure(
    MapRenderWorldDpvsCameraOnlyVisibilityFailureKind Kind,
    string Detail,
    MapRenderWorldDpvsStaticCullFailureKind? StaticCullFailure = null,
    MapRenderWorldDpvsCameraSkyCullFailureKind? CameraSkyCullFailure = null,
    MapRenderWorldDpvsCameraTraversalFailureKind? CameraTraversalFailure = null);
