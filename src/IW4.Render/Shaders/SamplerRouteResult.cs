namespace IW4.Render.Shaders;

internal readonly record struct SamplerRouteResult(
    bool Success,
    byte Source,
    RsxVertexOutputDependencyAnalysis VertexAnalysis,
    IReadOnlyList<PixelTextureOp> MatchingPixelTextureOps);
