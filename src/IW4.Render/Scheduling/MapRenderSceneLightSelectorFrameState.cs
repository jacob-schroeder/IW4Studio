using IW4.Render.Scheduling.Shadows;

namespace IW4.Render.Scheduling;

/// <summary>
/// One current-frame scene-light column snapshot. Its LSB-first allocation
/// words feed the +3 draw-method column transition and are deliberately
/// independent from the MSB-first three-view DPVS surface-page bitsets.
/// </summary>
public sealed class MapRenderSceneLightSelectorFrameState
{
    internal MapRenderSceneLightSelectorFrameState(
        long revision,
        MapRenderSceneLightSelectorState selectors,
        MapRenderSunShadowAtlasReadyState? sunShadowAtlasReady)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(selectors);
        if (sunShadowAtlasReady is not null &&
            sunShadowAtlasReady.Revision != revision)
        {
            throw new ArgumentException(
                "Sun-shadow atlas readiness must belong to the selector frame revision.",
                nameof(sunShadowAtlasReady));
        }

        Revision = revision;
        Selectors = selectors;
        SunShadowAtlasReady = sunShadowAtlasReady;
    }

    public long Revision { get; }

    public MapRenderSceneLightSelectorState Selectors { get; }

    /// <summary>
    /// Non-null only when the directional-sun +3 bits in this snapshot were
    /// authorized after both atlas partitions completed.
    /// </summary>
    public MapRenderSunShadowAtlasReadyState? SunShadowAtlasReady { get; }
}
