namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsNormalCameraFrameFailure(
    MapRenderWorldDpvsNormalCameraFrameFailureKind Kind,
    string Detail,
    int? PlaneIndex = null);
