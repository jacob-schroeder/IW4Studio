using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.Geometry;

/// <summary>
/// Structural form of <see cref="MapRenderTexture.BindingIdentity"/> used by
/// hot dictionaries without repeatedly formatting the identity string.
/// </summary>
internal readonly record struct MapRenderTextureBindingKey(
    string Name,
    MapRenderTextureTarget Target,
    string Format,
    int Width,
    int Height,
    byte SamplerState,
    int MipCount,
    int CubeFaceCount,
    int RgbaByteCount,
    uint FilterPayload,
    uint WrapPayload,
    uint EnablePayload,
    uint CachePayload)
{
    internal static MapRenderTextureBindingKey Create(MapRenderTexture texture) => new(
        texture.Name,
        texture.Target,
        texture.Format,
        texture.Width,
        texture.Height,
        texture.SamplerState,
        texture.MipLevels.Count,
        texture.CubeFaces?.Count ?? 0,
        texture.RgbaBytes.Length,
        texture.DecodedSamplerState.RsxTexFilterPayload,
        texture.DecodedSamplerState.RsxTexWrapPayload,
        texture.DecodedSamplerState.RsxTexEnablePayload,
        texture.DecodedSamplerState.RsxSamplerCachePayload);
}

/// <summary>
/// Structural form of <see cref="MapRenderUvRoute.BatchKey"/>. Float bit
/// patterns are retained so construction is culture-free and allocation-free.
/// </summary>
internal readonly record struct MapRenderUvRouteBatchKey(
    string WorldVertexFormat,
    byte TexCoordSource,
    byte StreamIndex,
    int Stride,
    int Offset,
    byte FormatByte0,
    byte FormatByte1,
    MapRenderUvBaseMode BaseMode,
    int ComponentA,
    int ComponentB,
    int ScaleUBits,
    int ScaleVBits,
    int AddUBits,
    int AddVBits)
{
    internal static MapRenderUvRouteBatchKey Create(MapRenderUvRoute route) => new(
        route.WorldVertexFormat,
        route.TexCoordSource,
        route.StreamIndex,
        route.Stride,
        route.Offset,
        route.FormatByte0,
        route.FormatByte1,
        route.BaseMode,
        route.ComponentA,
        route.ComponentB,
        BitConverter.SingleToInt32Bits(route.ScaleU),
        BitConverter.SingleToInt32Bits(route.ScaleV),
        BitConverter.SingleToInt32Bits(route.AddU),
        BitConverter.SingleToInt32Bits(route.AddV));
}
