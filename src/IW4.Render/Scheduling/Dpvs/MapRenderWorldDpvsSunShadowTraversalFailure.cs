namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsSunShadowTraversalFailure(
    MapRenderWorldDpvsSunShadowTraversalFailureKind Kind,
    string Detail,
    MapRenderWorldDpvsViewIndex? ViewIndex = null,
    int? CellIndex = null,
    int? PlaneIndex = null);

