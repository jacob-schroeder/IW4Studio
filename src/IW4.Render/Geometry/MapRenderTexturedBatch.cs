using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.Geometry;

public sealed record MapRenderTexturedBatch(
    MapRenderMaterialPass Pass,
    MapRenderTexture Texture,
    MapRenderTexture? LightmapTexture,
    IReadOnlyList<MapRenderColorLayer> ColorLayers,
    IReadOnlyList<MapRenderMaterialSamplerBinding> MaterialSamplers,
    MapRenderShaderExecutionContract ShaderExecution,
    string ShaderExecutionStatus,
    MapRenderUvRoute UvRoute,
    MapRenderState State,
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

    public MapRenderShaderExecutionContract? DepthPrepassShaderExecution
    {
        get;
        init;
    }
}
