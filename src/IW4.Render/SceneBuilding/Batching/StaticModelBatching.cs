using IW4.Render.Techniques;
using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding.Batching;

internal sealed class InstancedTexturedBatchBuilder(
    int lodIndex,
    MaterialPassIdentity pass,
    MaterialSamplerIdentity primarySampler,
    Texture texture,
    IReadOnlyList<MaterialColorLayer> colorLayers,
    IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers,
    ShaderExecutionContract shaderExecution,
    UvRoute uvRoute,
    RenderState state,
    MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
    ShaderExecutionContract? depthPrepassShaderExecution,
    int unresolvedCodeSamplerCount,
    List<float> vertices,
    List<float> rsxVertexInputs,
    List<uint> indices,
    RenderBounds localBounds,
    int editorDrawGroupId,
    bool isExactTechniqueVariant,
    byte sceneLightIndex)
{
    public int LodIndex { get; } = lodIndex;
    public MaterialPassIdentity Pass { get; } = pass;
    public MaterialSamplerIdentity PrimarySampler { get; } = primarySampler;
    public Texture Texture { get; } = texture;
    public IReadOnlyList<MaterialColorLayer> ColorLayers { get; } = colorLayers;
    public IReadOnlyList<MapRenderWorldMaterialSamplerBinding> MaterialSamplers { get; } = materialSamplers;
    public ShaderExecutionContract ShaderExecution { get; } = shaderExecution;
    public UvRoute UvRoute { get; } = uvRoute;
    public RenderState State { get; } = state;
    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; } =
        editorDepthPrepass;
    public ShaderExecutionContract? DepthPrepassShaderExecution
    {
        get;
    } = depthPrepassShaderExecution;
    public int UnresolvedCodeSamplerCount { get; } = unresolvedCodeSamplerCount;
    public List<float> Vertices { get; } = vertices;
    public List<float> RsxVertexInputs { get; } = rsxVertexInputs;
    public List<uint> Indices { get; } = indices;
    public RenderBounds LocalBounds { get; } = localBounds;
    public List<MapRenderStaticModelInstance> Instances { get; } = [];
    public List<MapRenderStaticModelInstance> PreparedInstances { get; } = [];
    public int PreparedSourceOrdinal { get; set; } = -1;
    public int EditorDrawGroupId { get; } = editorDrawGroupId;
    public bool IsExactTechniqueVariant { get; } =
        isExactTechniqueVariant;
    public byte SceneLightIndex { get; } = sceneLightIndex;
}
