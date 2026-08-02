using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding.Batching;

internal sealed class InstancedTexturedBatchBuilder(
    int lodIndex,
    MapRenderMaterialPass pass,
    MapRenderTexture texture,
    IReadOnlyList<MapRenderColorLayer> colorLayers,
    IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
    MapRenderShaderExecutionContract shaderExecution,
    MapRenderUvRoute uvRoute,
    MapRenderState state,
    MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
    MapRenderShaderExecutionContract? depthPrepassShaderExecution,
    int unresolvedCodeSamplerCount,
    List<float> vertices,
    List<float> rsxVertexInputs,
    List<uint> indices,
    MapRenderBounds localBounds,
    int editorDrawGroupId,
    bool isExactTechniqueVariant,
    byte sceneLightIndex)
{
    public int LodIndex { get; } = lodIndex;
    public MapRenderMaterialPass Pass { get; } = pass;
    public MapRenderTexture Texture { get; } = texture;
    public IReadOnlyList<MapRenderColorLayer> ColorLayers { get; } = colorLayers;
    public IReadOnlyList<MapRenderMaterialSamplerBinding> MaterialSamplers { get; } = materialSamplers;
    public MapRenderShaderExecutionContract ShaderExecution { get; } = shaderExecution;
    public MapRenderUvRoute UvRoute { get; } = uvRoute;
    public MapRenderState State { get; } = state;
    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; } =
        editorDepthPrepass;
    public MapRenderShaderExecutionContract? DepthPrepassShaderExecution
    {
        get;
    } = depthPrepassShaderExecution;
    public int UnresolvedCodeSamplerCount { get; } = unresolvedCodeSamplerCount;
    public List<float> Vertices { get; } = vertices;
    public List<float> RsxVertexInputs { get; } = rsxVertexInputs;
    public List<uint> Indices { get; } = indices;
    public MapRenderBounds LocalBounds { get; } = localBounds;
    public List<MapRenderStaticModelInstance> Instances { get; } = [];
    public List<MapRenderStaticModelInstance> PreparedInstances { get; } = [];
    public int PreparedSourceOrdinal { get; set; } = -1;
    public int EditorDrawGroupId { get; } = editorDrawGroupId;
    public bool IsExactTechniqueVariant { get; } =
        isExactTechniqueVariant;
    public byte SceneLightIndex { get; } = sceneLightIndex;
}
