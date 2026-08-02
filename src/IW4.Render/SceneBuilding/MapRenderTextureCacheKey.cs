using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Allocation-free identity for scene texture decoding. Canonical image
/// reference identity prevents two same-named assets with different payloads
/// from colliding, while sampler/mip/runtime fields cover every output variant.
/// </summary>
internal readonly record struct MapRenderTextureCacheKey(
    MapRenderTextureCacheKeyKind Kind,
    GfxImageAsset Image,
    byte SamplerState,
    bool IncludeAuthoredMipChain,
    MapRenderWorldRuntimeTextureIdentity? WorldIdentity,
    MapRenderSelectedPassSamplerShape CapturedShape,
    string? ContentSha256)
{
    internal static MapRenderTextureCacheKey Standard(
        MaterialTextureDef texture,
        GfxImageAsset image,
        bool includeAuthoredMipChain) =>
        FromMaterialTexture(
            MapRenderTextureCacheKeyKind.Standard,
            texture,
            image,
            includeAuthoredMipChain,
            null,
            MapRenderSelectedPassSamplerShape.Unknown,
            null);

    internal static MapRenderTextureCacheKey RuntimeCube(
        MaterialTextureDef texture,
        GfxImageAsset image,
        MapRenderWorldRuntimeTextureIdentity identity) =>
        FromMaterialTexture(
            MapRenderTextureCacheKeyKind.RuntimeCube,
            texture,
            image,
            false,
            identity,
            MapRenderSelectedPassSamplerShape.Cube,
            null);

    internal static MapRenderTextureCacheKey CapturedRuntimeTexture(
        MaterialTextureDef texture,
        GfxImageAsset image,
        MapRenderWorldRuntimeTextureIdentity identity,
        MapRenderSelectedPassSamplerShape shape,
        string contentSha256) =>
        FromMaterialTexture(
            MapRenderTextureCacheKeyKind.CapturedRuntimeTexture,
            texture,
            image,
            true,
            identity,
            shape,
            contentSha256);

    internal static MapRenderTextureCacheKey Sky(
        GfxImageAsset image,
        byte samplerState) => new(
            MapRenderTextureCacheKeyKind.Sky,
            image,
            samplerState,
            IncludeAuthoredMipChain: true,
            WorldIdentity: null,
            CapturedShape: MapRenderSelectedPassSamplerShape.Cube,
            ContentSha256: null);

    private static MapRenderTextureCacheKey FromMaterialTexture(
        MapRenderTextureCacheKeyKind kind,
        MaterialTextureDef texture,
        GfxImageAsset image,
        bool includeAuthoredMipChain,
        MapRenderWorldRuntimeTextureIdentity? worldIdentity,
        MapRenderSelectedPassSamplerShape capturedShape,
        string? contentSha256)
    {
        return new MapRenderTextureCacheKey(
            kind,
            image,
            texture.SamplerState,
            includeAuthoredMipChain,
            worldIdentity,
            capturedShape,
            contentSha256);
    }
}

internal enum MapRenderTextureCacheKeyKind : byte
{
    Standard = 0,
    RuntimeCube = 1,
    CapturedRuntimeTexture = 2,
    Sky = 3
}
