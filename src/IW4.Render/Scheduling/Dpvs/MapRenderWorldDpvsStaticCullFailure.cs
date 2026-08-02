namespace IW4.Render.Scheduling.Dpvs;

public sealed record MapRenderWorldDpvsStaticCullFailure(
    MapRenderWorldDpvsStaticCullFailureKind Kind,
    string Detail,
    int? CellIndex = null,
    int? TreeIndex = null,
    int? ElementIndex = null);
