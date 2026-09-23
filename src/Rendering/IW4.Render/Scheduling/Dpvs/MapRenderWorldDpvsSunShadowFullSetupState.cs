using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Immutable renderer inputs consumed by the normal PS3
/// <c>rg.sunShadowFull == 1</c> path. Values are setup state, never captured
/// clip-plane output.
/// </summary>
public sealed class MapRenderWorldDpvsSunShadowFullSetupState
{
    public MapRenderWorldDpvsSunShadowFullSetupState(
        Vector3 sunShadowLightDirection,
        Vector3 sunShadowCenter,
        int shadowMapResolution,
        int shadowMapTileCount,
        float sunShadowMapScale,
        int sunShadowSize,
        float sunSampleSizeNear,
        MapRenderWorldDpvsSunShadowSwitchPartitionZBranch
            switchPartitionZBranch)
    {
        SunShadowLightDirection = sunShadowLightDirection;
        SunShadowCenter = sunShadowCenter;
        ShadowMapResolution = shadowMapResolution;
        ShadowMapTileCount = shadowMapTileCount;
        SunShadowMapScale = sunShadowMapScale;
        SunShadowSize = sunShadowSize;
        SunSampleSizeNear = sunSampleSizeNear;
        SwitchPartitionZBranch = switchPartitionZBranch;
    }

    public Vector3 SunShadowLightDirection { get; }

    public Vector3 SunShadowCenter { get; }

    public int ShadowMapResolution { get; }

    public int ShadowMapTileCount { get; }

    public float SunShadowMapScale { get; }

    public int SunShadowSize { get; }

    public float SunSampleSizeNear { get; }

    /// <summary>
    /// Exact zero/nonzero outcome of the PS3 projection writer's tested
    /// three-float vector. Its semantic native owner remains open.
    /// </summary>
    public MapRenderWorldDpvsSunShadowSwitchPartitionZBranch
        SwitchPartitionZBranch { get; }

    /// <summary>
    /// Exact normal viewer profile from PS3 R_RenderScene and dvar
    /// registration: selected-sun fallback direction (0,0,1), zero center,
    /// 1024 pixels, two tiles, scale one, useful size 1024, and near sample
    /// size 0.25.
    /// </summary>
    public static MapRenderWorldDpvsSunShadowFullSetupState
        CreateSelectedSunAbsentViewerProfile() =>
        CreateViewerProfile(Vector3.UnitZ);

    /// <summary>
    /// Creates the normal-viewer full-profile metrics with the active
    /// authored sun direction supplied explicitly in native game axes. The
    /// direction must already be normalized by the selected-light producer;
    /// this boundary does not silently normalize or replace it.
    /// </summary>
    public static MapRenderWorldDpvsSunShadowFullSetupState
        CreateViewerProfile(Vector3 normalizedAuthoredSunDirection)
    {
        float lengthSquared = normalizedAuthoredSunDirection.LengthSquared();
        if (!float.IsFinite(normalizedAuthoredSunDirection.X) ||
            !float.IsFinite(normalizedAuthoredSunDirection.Y) ||
            !float.IsFinite(normalizedAuthoredSunDirection.Z) ||
            !float.IsFinite(lengthSquared) ||
            MathF.Abs(lengthSquared - 1f) > 0.001f)
        {
            throw new ArgumentException(
                "The authored sun-shadow direction must be finite and normalized.",
                nameof(normalizedAuthoredSunDirection));
        }

        return new(
            normalizedAuthoredSunDirection,
            Vector3.Zero,
            shadowMapResolution: 1024,
            shadowMapTileCount: 2,
            sunShadowMapScale: 1f,
            sunShadowSize: 1024,
            sunSampleSizeNear: 0.25f,
            MapRenderWorldDpvsSunShadowSwitchPartitionZBranch
                .TestedVectorNonZero);
    }
}
