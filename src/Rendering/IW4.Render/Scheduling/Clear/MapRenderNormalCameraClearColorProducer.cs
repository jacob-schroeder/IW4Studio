using System.Numerics;
using IW4.Render.Execution.Fog;

namespace IW4.Render.Scheduling.Clear;

/// <summary>
/// Pure implementation of PS3 <c>R_GetClearColor</c>.
/// </summary>
public static class MapRenderNormalCameraClearColorProducer
{
    private const int BlinkSelectionBit = 0x200;
    private const float ByteToFloat = 1.0f / byte.MaxValue;

    /// <summary>
    /// Adapts the live-preview fallback and optional gated active-fog state
    /// into the same byte-quantized inputs consumed by PS3
    /// <c>R_GetClearColor</c>.
    /// </summary>
    internal static MapRenderNormalCameraClearColorResult
        ProduceEditorPreview(
        float farPlaneDistance,
        Vector3 fallbackClearColor,
        MapRenderActiveFogState? activeFog)
    {
        if (!float.IsFinite(farPlaneDistance))
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));
        if (!IsFiniteUnitColor(fallbackClearColor))
            throw new ArgumentOutOfRangeException(nameof(fallbackClearColor));

        MapRenderRgba8Color primary = ToRgba8(fallbackClearColor);
        MapRenderActiveFogState fog = activeFog ?? InactiveFog(primary);
        return Produce(
            new MapRenderNormalCameraClearColorInput(
                activeFog is null
                    ? MapRenderNormalCameraClearMode.Steady
                    : MapRenderNormalCameraClearMode.FogColor,
                developerEnabled: false,
                systemMilliseconds: 0,
                primary,
                primary,
                new MapRenderNormalCameraFarPlaneState(
                    farPlaneDistance,
                    farPlaneDistance),
                fog));
    }

    public static MapRenderNormalCameraClearColorResult Produce(
        MapRenderNormalCameraClearColorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        float farPlaneDistance = input.FarPlane.EffectiveDistance;
        if (input.Fog.Density != 0.0f &&
            (farPlaneDistance != 0.0f ||
             input.Mode == MapRenderNormalCameraClearMode.FogColor))
        {
            return FromFog(input.Fog.Color);
        }

        if (input.Mode == MapRenderNormalCameraClearMode.Never ||
            (input.Mode == MapRenderNormalCameraClearMode.DeveloperOnlyBlink &&
             !input.DeveloperEnabled))
        {
            return new MapRenderNormalCameraClearColorResult(
                requestsColorClear: false,
                MapRenderNormalCameraClearColorSource.Disabled,
                0.0f,
                0.0f,
                0.0f,
                0.0f);
        }

        if (input.Mode == MapRenderNormalCameraClearMode.Steady ||
            (input.SystemMilliseconds & BlinkSelectionBit) == 0)
        {
            return FromDvar(
                input.PrimaryColor,
                MapRenderNormalCameraClearColorSource.PrimaryDvar);
        }

        return FromDvar(
            input.SecondaryColor,
            MapRenderNormalCameraClearColorSource.SecondaryDvar);
    }

    private static MapRenderNormalCameraClearColorResult FromFog(
        MapRenderBgra8Color color) =>
        new(
            requestsColorClear: true,
            MapRenderNormalCameraClearColorSource.Fog,
            color.Red * ByteToFloat,
            color.Green * ByteToFloat,
            color.Blue * ByteToFloat,
            1.0f);

    private static MapRenderNormalCameraClearColorResult FromDvar(
        MapRenderRgba8Color color,
        MapRenderNormalCameraClearColorSource source) =>
        new(
            requestsColorClear: true,
            source,
            color.Red * ByteToFloat,
            color.Green * ByteToFloat,
            color.Blue * ByteToFloat,
            1.0f);

    private static MapRenderActiveFogState InactiveFog(
        MapRenderRgba8Color color)
    {
        var bgra = new MapRenderBgra8Color(
            color.Blue,
            color.Green,
            color.Red,
            color.Alpha);
        return new MapRenderActiveFogState(
            startTime: 0,
            finishTime: 0,
            bgra,
            fogStart: 0f,
            density: 0f,
            fogMaxOpacity: 0f,
            new MapRenderActiveSunFogState(
                enabled: false,
                bgra,
                Vector3.UnitZ,
                beginFadeAngleDegrees: 0f,
                endFadeAngleDegrees: 0f,
                scale: 0f));
    }

    private static MapRenderRgba8Color ToRgba8(Vector3 color) => new(
        ToByte(color.X),
        ToByte(color.Y),
        ToByte(color.Z),
        byte.MaxValue);

    private static byte ToByte(float value) => checked((byte)Math.Clamp(
        (int)MathF.Round(value * byte.MaxValue),
        byte.MinValue,
        byte.MaxValue));

    private static bool IsFiniteUnitColor(Vector3 color) =>
        float.IsFinite(color.X) && color.X is >= 0f and <= 1f &&
        float.IsFinite(color.Y) && color.Y is >= 0f and <= 1f &&
        float.IsFinite(color.Z) && color.Z is >= 0f and <= 1f;
}
