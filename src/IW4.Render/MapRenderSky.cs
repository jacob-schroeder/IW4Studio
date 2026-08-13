using IW4.Render.Textures;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render;

public sealed record MapRenderSky(
    int? WorldSkyIndex,
    MapRenderSkySource Source,
    IReadOnlyList<int> SkyStartSurfPositions,
    IReadOnlyList<int> SurfaceIndices,
    Texture Texture,
    float[] Vertices,
    uint[] Indices)
{
    /// <summary>
    /// Exact selected wc_sky source pass retained for backend-specific
    /// execution. This remains absent when the surfaces in this submission do
    /// not resolve to one identical authored pass.
    /// </summary>
    internal MaterialPassIdentity? ShaderPass { get; init; }

    internal MaterialSamplerIdentity? ShaderPrimarySampler { get; init; }

    internal MaterialStreamSource ShaderTexCoordSource { get; init; }

    /// <summary>
    /// Exact translated RSX pair selected by <see cref="ShaderPass"/>. The
    /// compatibility sky path does not require this optional provenance.
    /// </summary>
    internal ShaderExecutionContract? ShaderExecution { get; init; }
}
