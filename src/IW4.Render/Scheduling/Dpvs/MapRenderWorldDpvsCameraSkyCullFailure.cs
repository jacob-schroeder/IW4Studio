namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsCameraSkyCullFailure(
    MapRenderWorldDpvsCameraSkyCullFailureKind Kind,
    string Detail,
    int? SkyIndex = null,
    int? ElementIndex = null);
