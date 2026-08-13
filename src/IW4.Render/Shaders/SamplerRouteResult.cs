using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

internal readonly record struct SamplerRouteResult(
    bool Success,
    MaterialStreamSource Source,
    RsxVertexOutputDependencyAnalysis VertexAnalysis,
    IReadOnlyList<PixelTextureOp> MatchingPixelTextureOps);
