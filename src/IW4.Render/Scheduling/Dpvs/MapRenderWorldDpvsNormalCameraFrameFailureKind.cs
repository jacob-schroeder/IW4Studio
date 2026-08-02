namespace IW4.Render.Scheduling.Dpvs;

public enum MapRenderWorldDpvsNormalCameraFrameFailureKind
{
    InvalidCamera = 0,
    InvalidFramebufferAspectRatio = 1,
    InvalidFarPlaneState = 2,
    SingularViewProjection = 3,
    InvalidFrustumPlane = 4
}
