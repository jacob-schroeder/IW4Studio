using IW4.Render.Execution.Fog;

namespace IW4.Render.Scheduling.Clear;

/// <summary>
/// Pure implementation of PS3 <c>R_GetClearColor</c>.
/// </summary>
public static class MapRenderNormalCameraClearColorProducer
{
    private const int BlinkSelectionBit = 0x200;
    private const float ByteToFloat = 1.0f / byte.MaxValue;

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
}
