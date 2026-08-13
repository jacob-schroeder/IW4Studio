using IW4.Render.Materials;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Geometry;

internal readonly record struct WorldVertexDecoderSelection(
    WorldVertexDecoder? Decoder,
    UvRoute UvRoute);

internal readonly record struct WorldVertexDecoderCacheKey(
    WorldVertexLayoutSelection Layout,
    MaterialStreamSource TexCoordSource,
    bool TexCoordSourceIsEngineRouted);

internal delegate WorldVertexDecoderSelection WorldVertexDecoderResolver(
    WorldVertexLayoutSelection layout,
    MaterialStreamSource texCoordSource,
    bool texCoordSourceIsEngineRouted);
