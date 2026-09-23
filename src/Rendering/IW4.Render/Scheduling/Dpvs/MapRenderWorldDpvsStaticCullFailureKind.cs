namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsStaticCullFailureKind
{
    InvalidWorldCardinality = 0,
    InvalidCellIndex,
    InvalidClipPlane,
    CellTreeCardinalityMismatch,
    AabbTreeCardinalityMismatch,
    InvalidAabbTreeBounds,
    InvalidAabbTreeTopology,
    InvalidStaticModelIndex,
    InvalidSurfaceRange,
    InvalidSurfaceIndex,
    InvalidStaticModelBounds,
    InvalidSurfaceBounds,
    ActivePlaneCapacityExceeded
}
