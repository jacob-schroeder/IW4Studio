namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsCameraSkyCullFailureKind
{
    InvalidWorldCardinality = 0,
    InvalidClipPlane,
    InvalidSkyCardinality,
    InvalidSortedSurfacePosition,
    InvalidSurfaceIndex,
    InvalidSurfaceBounds
}
