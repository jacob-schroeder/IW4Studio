namespace IW4.Render.Scheduling;

/// <summary>
/// Result of the PS3 Event 0x0E world-surface classifier. This value is DPVS
/// page membership only; it carries no scene-light shadow-allocation state.
/// </summary>
public enum MapRenderWorldSurfacePageMembership
{
    Excluded = 0,
    PageZero = 1,
    PageOne = 2
}
