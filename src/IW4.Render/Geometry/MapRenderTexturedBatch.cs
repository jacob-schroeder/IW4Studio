using IW4.Render.Techniques;
using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.Geometry;

public sealed record MapRenderTexturedBatch(
    MaterialPassIdentity Pass,
    MaterialSamplerIdentity PrimarySampler,
    Texture Texture,
    Texture? LightmapTexture,
    IReadOnlyList<MaterialColorLayer> ColorLayers,
    IReadOnlyList<MapRenderWorldMaterialSamplerBinding> MaterialSamplers,
    ShaderExecutionContract ShaderExecution,
    string ShaderExecutionStatus,
    UvRoute UvRoute,
    RenderState State,
    int UnresolvedCodeSamplerCount,
    IReadOnlyList<MapRenderPickRange> PickRanges,
    float[] Vertices,
    float[] RsxVertexInputs,
    uint[] Indices)
{
    /// <summary>
    /// Invocation-owned Event20 DrawGroup.SceneLightIndex. The value remains
    /// part of batch identity so two local lights can never share one set of
    /// direct constants merely because they selected the same material pass.
    /// </summary>
    public byte SceneLightIndex { get; init; }

    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; init; }

    public ShaderExecutionContract? DepthPrepassShaderExecution
    {
        get;
        init;
    }
}
