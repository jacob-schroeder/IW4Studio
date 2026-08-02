namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsCameraCellFailureKind
{
    InvalidCameraOrigin = 0,
    InvalidCellCount,
    PlaneCardinalityMismatch,
    NodeCardinalityMismatch,
    MissingRootNode,
    InvalidPlaneIndex,
    InvalidPlane,
    InvalidChildOffset,
    TraversalCycle,
    InvalidLeafCell
}
