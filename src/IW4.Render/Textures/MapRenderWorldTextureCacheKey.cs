using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

/// <summary>
/// Build-local identity for textures whose content is owned by a map world's
/// runtime resource slots. It deliberately cannot be used by generic texture
/// consumers.
/// </summary>
internal readonly record struct MapRenderWorldTextureCacheKey(
    MapRenderWorldTextureCacheKeyKind Kind,
    GfxImageAsset Image,
    byte SamplerState,
    MapRenderWorldRuntimeTextureIdentity WorldIdentity,
    TextureSamplerShape CapturedShape,
    string? ContentSha256)
{
    internal static MapRenderWorldTextureCacheKey WorldRuntimeCube(
        GfxImageAsset image,
        byte samplerState,
        MapRenderWorldRuntimeTextureIdentity identity) => new(
            MapRenderWorldTextureCacheKeyKind.WorldRuntimeCube,
            image,
            samplerState,
            identity,
            TextureSamplerShape.Cube,
            null);

    internal static MapRenderWorldTextureCacheKey CapturedWorldTexture(
        GfxImageAsset image,
        byte samplerState,
        MapRenderWorldRuntimeTextureIdentity identity,
        TextureSamplerShape shape,
        string contentSha256) => new(
            MapRenderWorldTextureCacheKeyKind.CapturedWorldTexture,
            image,
            samplerState,
            identity,
            shape,
            contentSha256);
}

internal enum MapRenderWorldTextureCacheKeyKind : byte
{
    WorldRuntimeCube = 1,
    CapturedWorldTexture = 2
}
