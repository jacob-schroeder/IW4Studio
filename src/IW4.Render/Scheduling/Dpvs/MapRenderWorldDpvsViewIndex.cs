namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// PS3 GfxWorldDpvsStatic view-array indices selected by the Event 0x0D
/// handler at default_mp.elf 0x00350EF8.
/// </summary>
public enum MapRenderWorldDpvsViewIndex
{
    Camera = 0,

    // Sun-shadow partition semantics follow IW3/IW4 naming. The PS3 view
    // indices own independent bitset destinations.
    SunShadowPartition0 = 1,
    SunShadowPartition1 = 2
}
