using System.Numerics;
using IW4.Render.Lighting;
using IW4.Render.Execution.Fog;
using IW4.Render.Transforms;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Adapts editor-owned distance-haze controls into the exact active-state
/// fields consumed by the PS3 <c>R_SetFrameFog</c> row producer. This
/// policy supplies values; it does not claim that they were map-authored.
/// </summary>
public static class MapRenderEditorPreviewActiveFogAdapter
{
    public const float DefaultSunFogBeginFadeAngleDegrees = 45f;
    public const float DefaultSunFogEndFadeAngleDegrees = 90f;
    public const float DefaultSunFogScale = 0.25f;

    private const float MinimumEndpointTransmission = 1f / 255f;

    public static MapRenderActiveFogState Create(
        MapRenderEditorPreviewAtmospherePlan atmosphere,
        MapRenderEditorPreviewLightingPlan? lighting)
    {
        ArgumentNullException.ThrowIfNull(atmosphere);
        if (!atmosphere.IsEnabled)
        {
            throw new ArgumentException(
                "An active Live Preview fog state requires an enabled atmosphere plan.",
                nameof(atmosphere));
        }

        float range = atmosphere.EndDistance - atmosphere.StartDistance;
        float endpointTransmission = MathF.Max(
            1f - atmosphere.MaxOpacity,
            MinimumEndpointTransmission);
        float density = atmosphere.MaxOpacity == 0f
            ? 0f
            : -MathF.Log(endpointTransmission) / range;
        MapRenderBgra8Color fogColor = ToBgra8(atmosphere.FogColor);

        Vector3 sunFogDirection = lighting?.HasDirectionalSun == true
            ? RenderCoordinateConverter.RenderToGameUnitDirection(
                lighting.DirectionalSunDirection)
            : Vector3.UnitZ;
        var sunFog = new MapRenderActiveSunFogState(
            enabled: true,
            color: fogColor,
            direction: sunFogDirection,
            DefaultSunFogBeginFadeAngleDegrees,
            DefaultSunFogEndFadeAngleDegrees,
            DefaultSunFogScale);

        return new MapRenderActiveFogState(
            startTime: 0,
            finishTime: 0,
            fogColor,
            atmosphere.StartDistance,
            density,
            atmosphere.MaxOpacity,
            sunFog);
    }

    private static MapRenderBgra8Color ToBgra8(Vector3 color) =>
        new(
            ToByte(color.Z),
            ToByte(color.Y),
            ToByte(color.X),
            byte.MaxValue);

    private static byte ToByte(float value) =>
        checked((byte)Math.Clamp(
            (int)MathF.Round(value * byte.MaxValue),
            byte.MinValue,
            byte.MaxValue));
}
