namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Typed ownership for an Event 0x0D command set. Camera portal commands and
/// secondary sun-frustum commands have different producers and must never be
/// substituted for one another merely because both contain cell/plane rows.
/// </summary>
public enum MapRenderWorldDpvsCommandOrigin
{
    CameraPortalTraversal = 0,
    SunShadowFrustumTraversal = 1
}
