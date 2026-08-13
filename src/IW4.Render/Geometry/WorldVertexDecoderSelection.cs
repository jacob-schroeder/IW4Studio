using IW4.Render.Materials;

namespace IW4.Render.Geometry;

internal readonly record struct WorldVertexDecoderSelection(
    WorldVertexDecoder? Decoder,
    UvRoute UvRoute);

internal readonly record struct WorldVertexDecoderCacheKey(
    WorldVertexLayoutSelection Layout,
    byte TexCoordSource,
    bool TexCoordSourceIsEngineRouted);

internal delegate WorldVertexDecoderSelection WorldVertexDecoderResolver(
    WorldVertexLayoutSelection layout,
    byte texCoordSource,
    bool texCoordSourceIsEngineRouted);
