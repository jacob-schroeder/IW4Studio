using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling;

/// <summary>
/// Copies the selector-relevant header bytes from PS3 ComPrimaryLight rows to
/// GfxLight rows.
/// </summary>
public static class MapRenderComWorldLightSelectorAdapter
{
    public static MapRenderSceneLightSelectorAssetState Create(
        GfxWorldAsset gfxWorld,
        ComWorldAsset comWorld)
    {
        ArgumentNullException.ThrowIfNull(gfxWorld);
        ArgumentNullException.ThrowIfNull(comWorld);

        // Take the loop count from GfxWorld+0x20 while reading source rows
        // through ComWorld+0x0C.
        int primaryLightCount = gfxWorld.PrimaryLightCount;
        if (primaryLightCount < 0 ||
            primaryLightCount > MapRenderDrawMethodPageProducer.PageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gfxWorld),
                $"GfxWorld primary-light count {primaryLightCount} is outside the PS3 selector-page range 0..{MapRenderDrawMethodPageProducer.PageLength}.");
        }

        if (comWorld.PrimaryLightCount != primaryLightCount ||
            comWorld.PrimaryLights.Count != primaryLightCount)
        {
            throw new InvalidDataException(
                $"GfxWorld requests {primaryLightCount} primary lights, while ComWorld declares " +
                $"{comWorld.PrimaryLightCount} and materialized {comWorld.PrimaryLights.Count} rows.");
        }

        var baseColumns = new byte[primaryLightCount];
        var canUseShadowMap = new byte[primaryLightCount];
        for (int lightIndex = 0; lightIndex < primaryLightCount; lightIndex++)
        {
            ComPrimaryLight light = comWorld.PrimaryLights[lightIndex]
                ?? throw new InvalidDataException($"ComWorld primary light {lightIndex} is null.");

            baseColumns[lightIndex] = (byte)light.Type;
            canUseShadowMap[lightIndex] = light.CanUseShadowMapRaw;
        }

        return new MapRenderSceneLightSelectorAssetState(baseColumns, canUseShadowMap);
    }
}
