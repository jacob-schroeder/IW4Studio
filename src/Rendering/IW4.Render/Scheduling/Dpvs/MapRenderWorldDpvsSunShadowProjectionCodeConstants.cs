using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Immutable operational vectors produced with the PS3 sun-shadow
/// projection. Numeric source rows 0x1E and 0x1F retain their native values;
/// member names follow IW3/IW4 semantics.
/// </summary>
public sealed class MapRenderWorldDpvsSunShadowProjectionCodeConstants
{
    public MapRenderWorldDpvsSunShadowProjectionCodeConstants(
        Vector4 switchPartition,
        Vector4 shadowMapScale)
    {
        if (!IsFinite(switchPartition) || !IsFinite(shadowMapScale))
        {
            throw new ArgumentException(
                "Sun-shadow projection code constants must be finite.");
        }

        SwitchPartition = switchPartition;
        ShadowMapScale = shadowMapScale;
    }

    /// <summary>PS3 direct source row 0x1E.</summary>
    public Vector4 SwitchPartition { get; }

    /// <summary>PS3 direct source row 0x1F.</summary>
    public Vector4 ShadowMapScale { get; }

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
