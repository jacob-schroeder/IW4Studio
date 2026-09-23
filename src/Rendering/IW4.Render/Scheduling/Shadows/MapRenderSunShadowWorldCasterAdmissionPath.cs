namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// PS3 world-caster scheduling branch represented by a catalog.
/// </summary>
public enum MapRenderSunShadowWorldCasterAdmissionPath
{
    /// <summary>
    /// Fast-worker partition visibility is MSB-first and the cached
    /// surfaceCastsSunShadow mask is LSB-first.
    /// </summary>
    FastWorkerCachedCasterMask = 0
}
