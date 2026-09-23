namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsCameraCellFailure(
    MapRenderWorldDpvsCameraCellFailureKind Kind,
    string Detail,
    int? NodeOffset = null,
    int? PlaneIndex = null);
