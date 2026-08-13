using IW4.Render.Techniques;
using IW4.Render.Execution;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding.Batching;

/// <summary>
/// Allocation-bounded structural replacement for the formatted world-batch
/// identity. The field set deliberately matches the previous string key so
/// grouping and first-seen dictionary order remain unchanged.
/// </summary>
internal sealed class WorldTexturedBatchKey :
    IEquatable<WorldTexturedBatchKey>
{
    private readonly IReadOnlyList<MaterialColorLayer> _colorLayers;
    private readonly IReadOnlyList<MapRenderWorldMaterialSamplerBinding>
        _materialSamplers;
    private readonly int _hashCode;

    internal WorldTexturedBatchKey(
        MaterialPassIdentity pass,
        MaterialSamplerIdentity primarySampler,
        Texture texture,
        Texture? lightmapTexture,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers,
        ShaderExecutionContract shaderExecution,
        UvRoute uvRoute,
        RenderState state,
        MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
        ShaderExecutionContract? depthPrepassShaderExecution,
        int unresolvedCodeSamplerCount,
        int? editorDrawGroupSurfaceIndex,
        byte sceneLightIndex)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(colorLayers);
        ArgumentNullException.ThrowIfNull(materialSamplers);
        ArgumentNullException.ThrowIfNull(shaderExecution);
        ArgumentNullException.ThrowIfNull(uvRoute);

        Pass = pass;
        PrimarySampler = primarySampler;
        Texture = TextureBindingKey.Create(texture);
        LightmapTexture = lightmapTexture is null
            ? null
            : TextureBindingKey.Create(lightmapTexture);
        _colorLayers = colorLayers;
        _materialSamplers = materialSamplers;
        ProgramCacheKey = shaderExecution.ProgramCacheKey;
        UvRoute = UvRouteBatchKey.Create(uvRoute);
        LoadBits0 = state.LoadBits0;
        LoadBits1 = state.LoadBits1;
        StateTail = state.Tail;
        EditorDepthPrepass = editorDepthPrepass;
        DepthPrepassProgramCacheKey =
            depthPrepassShaderExecution?.ProgramCacheKey;
        UnresolvedCodeSamplerCount = unresolvedCodeSamplerCount;
        EditorDrawGroupSurfaceIndex = editorDrawGroupSurfaceIndex;
        SceneLightIndex = sceneLightIndex;

        var hash = new HashCode();
        hash.Add(Pass);
        hash.Add(PrimarySampler);
        hash.Add(Texture);
        hash.Add(LightmapTexture);
        foreach (MaterialColorLayer layer in _colorLayers)
            hash.Add(ColorLayerKey.Create(layer));
        foreach (MapRenderWorldMaterialSamplerBinding sampler in _materialSamplers)
            hash.Add(SamplerBindingKey.Create(sampler));
        hash.Add(ProgramCacheKey, StringComparer.Ordinal);
        hash.Add(UvRoute);
        hash.Add(LoadBits0);
        hash.Add(LoadBits1);
        hash.Add(StateTail);
        hash.Add(EditorDepthPrepass);
        hash.Add(DepthPrepassProgramCacheKey, StringComparer.Ordinal);
        hash.Add(UnresolvedCodeSamplerCount);
        hash.Add(EditorDrawGroupSurfaceIndex);
        hash.Add(SceneLightIndex);
        _hashCode = hash.ToHashCode();
    }

    private MaterialPassIdentity Pass { get; }

    private MaterialSamplerIdentity PrimarySampler { get; }

    private TextureBindingKey Texture { get; }

    private TextureBindingKey? LightmapTexture { get; }

    private string ProgramCacheKey { get; }

    private UvRouteBatchKey UvRoute { get; }

    private uint LoadBits0 { get; }

    private uint LoadBits1 { get; }

    private uint StateTail { get; }

    private MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; }

    private string? DepthPrepassProgramCacheKey { get; }

    private int UnresolvedCodeSamplerCount { get; }

    private int? EditorDrawGroupSurfaceIndex { get; }

    private byte SceneLightIndex { get; }

    public bool Equals(WorldTexturedBatchKey? other)
    {
        if (other is null ||
            Pass != other.Pass ||
            PrimarySampler != other.PrimarySampler ||
            Texture != other.Texture ||
            LightmapTexture != other.LightmapTexture ||
            _colorLayers.Count != other._colorLayers.Count ||
            _materialSamplers.Count != other._materialSamplers.Count ||
            !string.Equals(
                ProgramCacheKey,
                other.ProgramCacheKey,
                StringComparison.Ordinal) ||
            UvRoute != other.UvRoute ||
            LoadBits0 != other.LoadBits0 ||
            LoadBits1 != other.LoadBits1 ||
            StateTail != other.StateTail ||
            EditorDepthPrepass != other.EditorDepthPrepass ||
            !string.Equals(
                DepthPrepassProgramCacheKey,
                other.DepthPrepassProgramCacheKey,
                StringComparison.Ordinal) ||
            UnresolvedCodeSamplerCount != other.UnresolvedCodeSamplerCount ||
            EditorDrawGroupSurfaceIndex !=
                other.EditorDrawGroupSurfaceIndex ||
            SceneLightIndex != other.SceneLightIndex)
        {
            return false;
        }

        for (int index = 0; index < _colorLayers.Count; index++)
        {
            if (ColorLayerKey.Create(_colorLayers[index]) !=
                ColorLayerKey.Create(other._colorLayers[index]))
            {
                return false;
            }
        }
        for (int index = 0; index < _materialSamplers.Count; index++)
        {
            if (SamplerBindingKey.Create(_materialSamplers[index]) !=
                SamplerBindingKey.Create(other._materialSamplers[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is WorldTexturedBatchKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private readonly record struct ColorLayerKey(
        int LayerIndex,
        int SamplerArgIndex,
        ushort SamplerDest,
        uint SamplerHash,
        TextureBindingKey Texture,
        UvRouteBatchKey UvRoute,
        int BlendWeightComponent)
    {
        internal static ColorLayerKey Create(MaterialColorLayer layer) => new(
            layer.LayerIndex,
            layer.Identity.SamplerArgIndex,
            layer.Identity.SamplerDest,
            layer.Identity.SamplerHash,
            TextureBindingKey.Create(layer.Texture),
            UvRouteBatchKey.Create(layer.UvRoute),
            layer.BlendWeightComponent);
    }

    private readonly record struct SamplerBindingKey(
        MaterialSamplerIdentity Identity,
        MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity,
        TextureBindingKey? Texture,
        UvRouteBatchKey? UvRoute)
    {
        internal static SamplerBindingKey Create(
            MapRenderWorldMaterialSamplerBinding binding) => new(
                binding.Binding.Identity,
                binding.RuntimeTextureIdentity,
                binding.Binding.Texture is null
                    ? null
                    : TextureBindingKey.Create(binding.Binding.Texture),
                binding.Binding.UvRoute is null
                    ? null
                    : UvRouteBatchKey.Create(binding.Binding.UvRoute));
    }
}

internal sealed class TexturedBatchBuilder(
    MaterialPassIdentity pass,
    MaterialSamplerIdentity primarySampler,
    Texture texture,
    Texture? lightmapTexture,
    IReadOnlyList<MaterialColorLayer> colorLayers,
    IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers,
    ShaderExecutionContract shaderExecution,
    UvRoute uvRoute,
    RenderState state,
    MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
    ShaderExecutionContract? depthPrepassShaderExecution,
    int unresolvedCodeSamplerCount,
    byte sceneLightIndex)
{
    public MaterialPassIdentity Pass { get; } = pass;
    public MaterialSamplerIdentity PrimarySampler { get; } = primarySampler;
    public Texture Texture { get; } = texture;
    public Texture? LightmapTexture { get; } = lightmapTexture;
    public IReadOnlyList<MaterialColorLayer> ColorLayers { get; } = colorLayers;
    public IReadOnlyList<MapRenderWorldMaterialSamplerBinding> MaterialSamplers { get; } = materialSamplers;
    public ShaderExecutionContract ShaderExecution { get; } = shaderExecution;
    public string ShaderExecutionStatus { get; } = shaderExecution.ProgramExecutionStatus;
    public UvRoute UvRoute { get; } = uvRoute;
    public RenderState State { get; } = state;
    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; } =
        editorDepthPrepass;
    public ShaderExecutionContract? DepthPrepassShaderExecution
    {
        get;
    } = depthPrepassShaderExecution;
    public int UnresolvedCodeSamplerCount { get; } = unresolvedCodeSamplerCount;
    public byte SceneLightIndex { get; } = sceneLightIndex;
    public List<float> Vertices { get; } = [];
    public List<float> RsxVertexInputs { get; } = [];
    public List<uint> Indices { get; } = [];
    public List<MapRenderPickRange> PickRanges { get; } = [];
}

internal sealed record PreparedWorldTexturedSubmission(
    SelectedColorPass SelectedPass,
    Texture Texture,
    Texture? LightmapTexture,
    IReadOnlyList<MaterialColorLayer> ColorLayers,
    IReadOnlyList<MapRenderWorldMaterialSamplerBinding> MaterialSamplers,
    ShaderExecutionContract ShaderExecution,
    UvRoute UvRoute,
    RenderState RenderState,
    MapRenderEditorDepthPrepassPlan? EditorDepthPrepass,
    ShaderExecutionContract? DepthPrepassShaderExecution,
    List<float> Vertices,
    List<float> RsxVertexInputs,
    List<uint> Indices,
    MapRenderPickRange PickRange,
    int TexturedTriangleCount,
    bool IsEditorTechniquePass,
    bool IsFallbackPass);
