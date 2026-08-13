using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

/// <summary>
/// Allocation-free identity for render texture decoding. Canonical image
/// reference identity prevents two same-named assets with different payloads
/// from colliding, while sampler and mip fields cover every generic output
/// variant.
/// </summary>
internal readonly record struct RenderTextureCacheKey(
    RenderTextureCacheKeyKind Kind,
    GfxImageAsset Image,
    byte SamplerState,
    bool IncludeAuthoredMipChain)
{
    internal static RenderTextureCacheKey TwoDimensionalImage(
        GfxImageAsset image,
        byte samplerState,
        bool includeAuthoredMipChain) =>
        new(
            RenderTextureCacheKeyKind.TwoDimensionalImage,
            image,
            samplerState,
            includeAuthoredMipChain);

    internal static RenderTextureCacheKey SkyCube(
        GfxImageAsset image,
        byte samplerState) => new(
            RenderTextureCacheKeyKind.SkyCube,
            image,
            samplerState,
            IncludeAuthoredMipChain: true);
}

internal enum RenderTextureCacheKeyKind : byte
{
    TwoDimensionalImage = 0,
    SkyCube = 1
}
