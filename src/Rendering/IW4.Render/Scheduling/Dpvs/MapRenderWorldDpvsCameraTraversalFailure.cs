namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsCameraTraversalFailure(
    MapRenderWorldDpvsCameraTraversalFailureKind Kind,
    string Detail,
    int? CellIndex = null,
    int? PortalIndex = null,
    MapRenderWorldDpvsNormalCameraFrameFailureKind? CameraFrameFailure = null,
    MapRenderWorldDpvsCameraCellFailureKind? CameraCellFailure = null);
