namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsCameraTraversalFailureKind
{
    CameraFrameBuildFailed = 0,
    CameraCellResolutionFailed = 1,
    InvalidWorldCellStorage = 2,
    InvalidPortalStorage = 3,
    InvalidPortalGeometry = 4,
    InvalidPortalTargetCell = 5,
    PortalQueueCapacityExceeded = 6,
    PortalHullCapacityExceeded = 7,
    PortalClipCapacityExceeded = 8,
    PortalPlaneCapacityExceeded = 9,
    PortalWalkLimitReached = 10,
    SkipPvsDisablesCellCommands = 11,
    PortalTraversalCycle = 12
}
