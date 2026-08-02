using IW4.Assets.Assets.Image;

namespace IW4.Runtime.Assets.GfxMap;

/// <summary>
/// Desired primary/secondary GfxImage identities for one
/// R_UpdateFrameLightmapTextures call. Null means authored per-lightmap rows.
/// </summary>
public sealed class GfxWorldLightmapTextureOverrideSelection
{
    public GfxWorldLightmapTextureOverrideSelection(
        GfxImageAsset? primary,
        GfxImageAsset? secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public GfxImageAsset? Primary { get; }

    public GfxImageAsset? Secondary { get; }

    public static GfxWorldLightmapTextureOverrideSelection FromNativeMode(
        int rawMode,
        bool unchangedPrimaryWhiteGate,
        GfxImageAsset blackImage,
        GfxImageAsset whiteImage,
        GfxImageAsset grayImage)
    {
        ArgumentNullException.ThrowIfNull(blackImage);
        ArgumentNullException.ThrowIfNull(whiteImage);
        ArgumentNullException.ThrowIfNull(grayImage);

        return rawMode switch
        {
            (int)GfxWorldLightMapMode.Black => new(blackImage, blackImage),
            (int)GfxWorldLightMapMode.Unchanged =>
                new(unchangedPrimaryWhiteGate ? whiteImage : null, null),
            (int)GfxWorldLightMapMode.White => new(whiteImage, whiteImage),
            _ => new(grayImage, grayImage)
        };
    }
}
