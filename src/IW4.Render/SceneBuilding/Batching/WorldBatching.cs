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
    private readonly IReadOnlyList<MapRenderColorLayer> _colorLayers;
    private readonly IReadOnlyList<MapRenderMaterialSamplerBinding>
        _materialSamplers;
    private readonly int _hashCode;

    internal WorldTexturedBatchKey(
        MapRenderMaterialPass pass,
        MapRenderTexture texture,
        MapRenderTexture? lightmapTexture,
        IReadOnlyList<MapRenderColorLayer> colorLayers,
        IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
        MapRenderShaderExecutionContract shaderExecution,
        MapRenderUvRoute uvRoute,
        MapRenderState state,
        MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
        MapRenderShaderExecutionContract? depthPrepassShaderExecution,
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
        Texture = MapRenderTextureBindingKey.Create(texture);
        LightmapTexture = lightmapTexture is null
            ? null
            : MapRenderTextureBindingKey.Create(lightmapTexture);
        _colorLayers = colorLayers;
        _materialSamplers = materialSamplers;
        ProgramCacheKey = shaderExecution.ProgramCacheKey;
        UvRoute = MapRenderUvRouteBatchKey.Create(uvRoute);
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
        hash.Add(Texture);
        hash.Add(LightmapTexture);
        foreach (MapRenderColorLayer layer in _colorLayers)
            hash.Add(ColorLayerKey.Create(layer));
        foreach (MapRenderMaterialSamplerBinding sampler in _materialSamplers)
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

    private MapRenderMaterialPass Pass { get; }

    private MapRenderTextureBindingKey Texture { get; }

    private MapRenderTextureBindingKey? LightmapTexture { get; }

    private string ProgramCacheKey { get; }

    private MapRenderUvRouteBatchKey UvRoute { get; }

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
        MapRenderTextureBindingKey Texture,
        MapRenderUvRouteBatchKey UvRoute,
        int BlendWeightComponent)
    {
        internal static ColorLayerKey Create(MapRenderColorLayer layer) => new(
            layer.LayerIndex,
            layer.SamplerArgIndex,
            layer.SamplerDest,
            layer.SamplerHash,
            MapRenderTextureBindingKey.Create(layer.Texture),
            MapRenderUvRouteBatchKey.Create(layer.UvRoute),
            layer.BlendWeightComponent);
    }

    private readonly record struct SamplerBindingKey(
        int SamplerArgIndex,
        ushort SamplerDest,
        uint SamplerHash,
        byte TextureSemantic,
        MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity,
        MapRenderTextureBindingKey? Texture,
        MapRenderUvRouteBatchKey? UvRoute)
    {
        internal static SamplerBindingKey Create(
            MapRenderMaterialSamplerBinding binding) => new(
                binding.SamplerArgIndex,
                binding.SamplerDest,
                binding.SamplerHash,
                binding.TextureSemantic,
                binding.WorldRuntimeTextureIdentity,
                binding.Texture is null
                    ? null
                    : MapRenderTextureBindingKey.Create(binding.Texture),
                binding.UvRoute is null
                    ? null
                    : MapRenderUvRouteBatchKey.Create(binding.UvRoute));
    }
}

internal sealed class TexturedBatchBuilder(
    MapRenderMaterialPass pass,
    MapRenderTexture texture,
    MapRenderTexture? lightmapTexture,
    IReadOnlyList<MapRenderColorLayer> colorLayers,
    IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
    MapRenderShaderExecutionContract shaderExecution,
    MapRenderUvRoute uvRoute,
    MapRenderState state,
    MapRenderEditorDepthPrepassPlan? editorDepthPrepass,
    MapRenderShaderExecutionContract? depthPrepassShaderExecution,
    int unresolvedCodeSamplerCount,
    byte sceneLightIndex)
{
    public MapRenderMaterialPass Pass { get; } = pass;
    public MapRenderTexture Texture { get; } = texture;
    public MapRenderTexture? LightmapTexture { get; } = lightmapTexture;
    public IReadOnlyList<MapRenderColorLayer> ColorLayers { get; } = colorLayers;
    public IReadOnlyList<MapRenderMaterialSamplerBinding> MaterialSamplers { get; } = materialSamplers;
    public MapRenderShaderExecutionContract ShaderExecution { get; } = shaderExecution;
    public string ShaderExecutionStatus { get; } = shaderExecution.ProgramExecutionStatus;
    public MapRenderUvRoute UvRoute { get; } = uvRoute;
    public MapRenderState State { get; } = state;
    public MapRenderEditorDepthPrepassPlan? EditorDepthPrepass { get; } =
        editorDepthPrepass;
    public MapRenderShaderExecutionContract? DepthPrepassShaderExecution
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
    MapRenderTexture Texture,
    MapRenderTexture? LightmapTexture,
    IReadOnlyList<MapRenderColorLayer> ColorLayers,
    IReadOnlyList<MapRenderMaterialSamplerBinding> MaterialSamplers,
    MapRenderShaderExecutionContract ShaderExecution,
    MapRenderUvRoute UvRoute,
    MapRenderState RenderState,
    MapRenderEditorDepthPrepassPlan? EditorDepthPrepass,
    MapRenderShaderExecutionContract? DepthPrepassShaderExecution,
    List<float> Vertices,
    List<float> RsxVertexInputs,
    List<uint> Indices,
    MapRenderPickRange PickRange,
    int TexturedTriangleCount,
    bool IsEditorTechniquePass,
    bool IsFallbackPass);
