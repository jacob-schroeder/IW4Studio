using IW4.Render.Scheduling.Shadows;

namespace IW4.Render.Scheduling;

using IW4.Assets.Assets.ComWorld;

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
        MapRenderSunShadowAtlasReadyState? sunShadowAtlasReady,
        MapRenderSpotShadowAtlasReadyState? spotShadowAtlasReady = null,
        bool isShadowAllocationPreflight = false)
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
        if (spotShadowAtlasReady is not null &&
            spotShadowAtlasReady.Revision != revision)
        {
            throw new ArgumentException(
                "Spot-shadow atlas readiness must belong to the selector frame revision.",
                nameof(spotShadowAtlasReady));
        }

        for (int lightIndex = 0;
             lightIndex < selectors.SceneLightCount;
             lightIndex++)
        {
            if (!selectors.IsAlternateVariantAllocated(lightIndex) ||
                isShadowAllocationPreflight)
            {
                continue;
            }

            bool isDirectional =
                selectors.VariantSelectorByLight[lightIndex] ==
                (byte)GfxLightType.Directional;
            bool hasReadyOwner = isDirectional
                ? sunShadowAtlasReady is not null
                : spotShadowAtlasReady?.TryGetEntry(
                    lightIndex,
                    out _) == true;
            if (!hasReadyOwner)
            {
                throw new ArgumentException(
                    $"Allocated scene-light selector {lightIndex} has no matching same-revision shadow publication.",
                    nameof(selectors));
            }
        }

        Revision = revision;
        Selectors = selectors;
        SunShadowAtlasReady = sunShadowAtlasReady;
        SpotShadowAtlasReady = spotShadowAtlasReady;
        IsShadowAllocationPreflight = isShadowAllocationPreflight;
    }

    public long Revision { get; }

    public MapRenderSceneLightSelectorState Selectors { get; }

    /// <summary>
    /// Non-null only when the directional-sun +3 bits in this snapshot were
    /// authorized after both atlas partitions completed.
    /// </summary>
    public MapRenderSunShadowAtlasReadyState? SunShadowAtlasReady { get; }

    /// <summary>
    /// Non-null only when every selected local-spot +3 bit is backed by a
    /// completed entry in the normal spot-shadow atlas for this revision.
    /// </summary>
    public MapRenderSpotShadowAtlasReadyState? SpotShadowAtlasReady { get; }

    internal bool IsShadowAllocationPreflight { get; }
}
