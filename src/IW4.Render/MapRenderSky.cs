using IW4.Render.Textures;
using IW4.Render.Execution;
using IW4.Render.Materials;

namespace IW4.Render;

public sealed record MapRenderSky(
    int? WorldSkyIndex,
    MapRenderSkySource Source,
    IReadOnlyList<int> SkyStartSurfPositions,
    IReadOnlyList<int> SurfaceIndices,
    MapRenderTexture Texture,
    float[] Vertices,
    uint[] Indices)
{
    /// <summary>
    /// Exact selected wc_sky source pass retained for backend-specific
    /// execution. This remains absent when the surfaces in this submission do
    /// not resolve to one identical authored pass.
    /// </summary>
    internal MapRenderMaterialPass? ShaderPass { get; init; }

    /// <summary>
    /// Exact translated RSX pair selected by <see cref="ShaderPass"/>. The
    /// compatibility sky path does not require this optional provenance.
    /// </summary>
    internal MapRenderShaderExecutionContract? ShaderExecution { get; init; }
}
