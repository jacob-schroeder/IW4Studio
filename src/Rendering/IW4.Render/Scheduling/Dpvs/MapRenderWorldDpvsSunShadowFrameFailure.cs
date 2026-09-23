namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsSunShadowFrameFailure(
    MapRenderWorldDpvsSunShadowFrameFailureKind Kind,
    string Detail,
    MapRenderWorldDpvsViewIndex? ViewIndex = null,
    int? PlaneIndex = null);

