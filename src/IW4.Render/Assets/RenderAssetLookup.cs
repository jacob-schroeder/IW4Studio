using IW4.Assets.Assets;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.LightDef;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Database;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup :
    IMaterialTechniqueBindingResolver,
    IMaterialExecutionLookup,
    IMapRenderWorldTextureBindingResolver,
    IMapRenderWorldTextureBindingSnapshotFactory,
    IMapRenderCanonicalRawFileProvider
{
    private const int ShaderArgSize = 0x08;
    private const int LiteralFloat4Size = 0x10;
    private const int MaterialSortedIndexCount =
        GfxDrawSurf.MaterialSortedIndexMask + 1;

    private readonly IXAssetSourceMemory _blocks;
    private readonly XAssetPool? _assetPool;
    private readonly GfxWorldRuntimeState? _gfxWorldRuntimeState;
    private readonly IGfxImagePayloadResolver? _imageStreams;
    private readonly MaterialTechniqueGraphCache _materialTechniqueGraph;
    private readonly Dictionary<XBlockAddress, MaterialAsset> _materialsByAddress = new();
    private readonly Dictionary<int, MaterialAsset> _materialsByRuntimePointer = new();
    private readonly Dictionary<int, MaterialAsset> _materialsBySortedIndex = new();
    private readonly HashSet<int> _ambiguousMaterialSortedIndices = [];
    private readonly Dictionary<XBlockAddress, GfxImageAsset> _imagesByAddress = new();
    private readonly Dictionary<int, GfxImageAsset> _imagesByRuntimePointer = new();
    private readonly Dictionary<XBlockAddress, MaterialTechniqueSetAsset> _techsetsByAddress = new();
    private readonly Dictionary<int, MaterialTechniqueSetAsset> _techsetsByRuntimePointer = new();
    private readonly Dictionary<MaterialAsset, MaterialTechniqueSetAsset> _techniqueSetsByMaterial = new();
    private readonly HashSet<MaterialAsset> _stagedMaterials =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<GfxImageAsset> _stagedImages =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MaterialAsset> _materialsWithUnresolvedTechniqueSet = [];
    private readonly Dictionary<MaterialTechniqueSetAsset, IReadOnlyList<MaterialTechniqueSlot>> _resolvedTechniqueSlotsBySet = new();
    private readonly Dictionary<XBlockAddress, MaterialVertexDeclarationAsset> _vertexDeclsByAddress = new();
    private readonly Dictionary<XBlockAddress, MaterialShaderAsset> _vertexShadersByAddress = new();
    private readonly Dictionary<XBlockAddress, MaterialShaderAsset> _pixelShadersByAddress = new();
    private readonly Dictionary<XBlockAddress, MaterialShaderAsset> _vertexShadersByAliasCell = new();
    private readonly Dictionary<XBlockAddress, MaterialShaderAsset> _pixelShadersByAliasCell = new();
    private readonly Dictionary<GfxStateBits, IReadOnlyList<uint>> _stateLoadBitsByObject = new();
    private readonly Dictionary<string, MaterialShaderAsset> _vertexShadersByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MaterialShaderAsset> _pixelShadersByName = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguousVertexShaderNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguousPixelShaderNames = new(StringComparer.Ordinal);
    private readonly HashSet<MaterialAsset> _hydratedDependencyMaterials = [];
    private readonly HashSet<MaterialTechniqueSetAsset> _hydratedDependencyTechsets = [];
    private readonly HashSet<MaterialTechniqueSetAsset> _knownTechniqueSets = [];
    private readonly Dictionary<IXAssetSourceMemory, RenderAssetLookup> _dependencyLookupsByBlocks = [];
    private readonly Dictionary<MaterialTechniqueSetAsset, RenderAssetLookup> _dependencyLookupByTechset = [];
    private readonly Dictionary<MaterialPassAsset,
        IReadOnlyList<MaterialShaderArgumentAsset>> _resolvedShaderArgsByPass =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _resolvedShaderArgsGate = new();
    private readonly Dictionary<SelectedPassProgramSourceCacheKey,
        SelectedPassProgramSources> _selectedPassProgramSources = [];
    private readonly object _selectedPassProgramSourcesGate = new();
    private long? _selectedPassProgramSourcePoolRevision;
    private RenderAssetLookup(
        IXAssetSourceMemory blocks,
        XAssetPool? assetPool = null)
    {
        _blocks = blocks;
        _assetPool = assetPool;
        _gfxWorldRuntimeState = null;
        _imageStreams = null;
        _materialTechniqueGraph = new MaterialTechniqueGraphCache(
            blocks,
            ReadCString);
    }

    public RenderAssetLookup(
        RenderAssetSource source,
        IGfxImagePayloadResolver? imageStreams = null)
        : this(source, imageStreams, gfxWorldRuntimeState: null, stagedAssets: [])
    {
    }

    internal RenderAssetLookup(
        RenderAssetSource source,
        IGfxImagePayloadResolver? imageStreams,
        IReadOnlyList<BaseAsset> stagedAssets)
        : this(source, imageStreams, gfxWorldRuntimeState: null, stagedAssets)
    {
    }

    internal RenderAssetLookup(
        RenderAssetSource source,
        GfxWorldRuntimeState gfxWorldRuntimeState,
        IGfxImagePayloadResolver? imageStreams)
        : this(
            source,
            imageStreams,
            gfxWorldRuntimeState ?? throw new ArgumentNullException(
                nameof(gfxWorldRuntimeState)),
            stagedAssets: [])
    {
    }

    private RenderAssetLookup(
        RenderAssetSource source,
        IGfxImagePayloadResolver? imageStreams,
        GfxWorldRuntimeState? gfxWorldRuntimeState,
        IReadOnlyList<BaseAsset> stagedAssets)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stagedAssets);
        _blocks = source.Blocks;
        _assetPool = source.AssetPool;
        _gfxWorldRuntimeState = gfxWorldRuntimeState;
        _imageStreams = imageStreams;
        _materialTechniqueGraph = new MaterialTechniqueGraphCache(
            source.Blocks,
            ReadCString);
        _stagedMaterials.UnionWith(stagedAssets.OfType<MaterialAsset>());
        _stagedImages.UnionWith(stagedAssets.OfType<GfxImageAsset>());
        foreach ((XBlockAddress address, GfxImageAsset image) in source.GfxImagesByAddress)
            _imagesByAddress[address] = image;

        Dictionary<int, XAssetListEntrySnapshot> entriesByIndex = source.AssetListEntries.ToDictionary(x => x.Index);
        foreach (XAssetLoadResult result in source.LoadedAssets)
        {
            if (result.Asset is not { } asset)
                continue;

            if (asset is MaterialTechniqueSetAsset techset)
            {
                MaterialTechniqueSetAsset? activeTechset = AddPooledTechniqueSetGraph(techset, source.AssetPool);
                if (activeTechset is not null &&
                    entriesByIndex.TryGetValue(result.Index, out XAssetListEntrySnapshot? entry) &&
                    entry is not null)
                {
                    _techsetsByAddress.TryAdd(entry.AssetPointerCellAddress, activeTechset);
                }
            }
            else if (asset is MaterialAsset material)
            {
                MaterialAsset? activeMaterial = AddPooledMaterialGraph(material, source.AssetPool);
                if (activeMaterial is not null &&
                    entriesByIndex.TryGetValue(result.Index, out XAssetListEntrySnapshot? entry) &&
                    entry is not null)
                {
                    AddMaterial(activeMaterial, entry.AssetPointerCellAddress);
                }
            }
            else if (asset is GfxImageAsset image)
            {
                AddImage(image);
                if (entriesByIndex.TryGetValue(result.Index, out XAssetListEntrySnapshot? entry) && entry is not null)
                    AddImage(image, entry.AssetPointerCellAddress);
            }
            else if (asset is LightDefAsset lightDef)
            {
                AddImage(lightDef.Image);
            }
            else if (asset is MaterialShaderAsset shader)
            {
                IndexTopLevelShader(shader, source.AssetPool);
            }
        }

        foreach (GfxWorldAsset gfxWorld in source.LoadedAssets.Select(x => x.Asset).OfType<GfxWorldAsset>())
        {
            CollectGfxWorldMaterials(gfxWorld, source.AssetPool);
            CollectGfxWorldImages(gfxWorld);
        }

        HydrateDependencyTechniqueGraphs();

    }

    public int MaterialCount => _materialsByAddress.Count;
    public int ImageCount => _imagesByAddress.Count;
}
