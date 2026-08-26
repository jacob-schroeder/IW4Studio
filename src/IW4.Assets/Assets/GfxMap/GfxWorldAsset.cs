using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldAsset : BaseAsset
{
    public const int SerializedSize = 0x288;

    public override XAssetType SerializedAssetType => XAssetType.GfxMap;

    // 0x00, 0x04: PS3 root stores each cell into varXString and calls Load_XString.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public XPointer<string> BaseNamePointer { get; init; }
    public string? BaseName { get; init; }

    public int PlaneCount { get; init; }
    public int NodeCount { get; init; }
    public int SurfaceCount { get; init; }
    public uint SkyCount { get; init; }
    public XPointer<GfxSky[]> SkiesPointer { get; init; }
    public IReadOnlyList<GfxSky> Skies { get; init; } = [];
    // +0x1C: directional-sun primary-light index. Non-sun candidates begin
    // at this index + 1; shadow geometry spans this row through the end.
    public int SunPrimaryLightIndex { get; init; }
    public int PrimaryLightCount { get; init; }
    public int SortKeyLitDecal { get; init; }
    public int SortKeyEffectDecal { get; init; }
    public int SortKeyEffectAuto { get; init; }
    public int SortKeyDistortion { get; init; }

    public GfxWorldDpvsPlanes DpvsPlanes { get; init; } = new();
    public XPointer<GfxCellTreeCount[]> CellTreeCountsPointer { get; init; }
    public IReadOnlyList<GfxCellTreeCount> CellTreeCounts { get; init; } = [];
    public XPointer<GfxAabbTree[]> CellTreesPointer { get; init; }
    public IReadOnlyList<GfxCellTree> CellTrees { get; init; } = [];
    public XPointer<GfxCell[]> CellsPointer { get; init; }
    public IReadOnlyList<GfxCell> Cells { get; init; } = [];

    public GfxWorldDraw WorldDraw { get; init; } = new();
    public GfxLightGrid LightGrid { get; init; } = new();
    public int ModelCount { get; init; }
    public XPointer<GfxBrushModel[]> ModelsPointer { get; init; }
    public IReadOnlyList<GfxBrushModel> Models { get; init; } = [];
    // Native GfxWorld +0xE4 embeds Bounds as midpoint[3], halfSize[3].
    // These historical property names describe neither ordered endpoint.
    public IReadOnlyList<float> Mins { get; init; } = [];
    public IReadOnlyList<float> Maxs { get; init; } = [];
    public uint Checksum { get; init; }
    public int MaterialMemoryCount { get; init; }
    public XPointer<MaterialMemory[]> MaterialMemoryPointer { get; init; }
    public IReadOnlyList<MaterialMemory> MaterialMemory { get; init; } = [];
    public Sunflare Sun { get; init; } = new();
    public IReadOnlyList<float> OutdoorLookupMatrix { get; init; } = [];
    public XPointer<GfxImageAsset> OutdoorImagePointer { get; init; }
    public GfxImageAsset? OutdoorImage { get; init; }

    public XPointer<uint[]> CellCasterBitsPointer { get; init; }
    public IReadOnlyList<uint> CellCasterBits { get; init; } = [];
    public XPointer<uint[]> CellCasterBits2Pointer { get; init; }
    public IReadOnlyList<uint> CellCasterBits2 { get; init; } = [];
    public XPointer<GfxSceneDynModel[]> SceneDynModelPointer { get; init; }
    public IReadOnlyList<GfxSceneDynModel> SceneDynModels { get; init; } = [];
    public XPointer<GfxSceneDynBrush[]> SceneDynBrushPointer { get; init; }
    public IReadOnlyList<GfxSceneDynBrush> SceneDynBrushes { get; init; } = [];
    public XPointer<uint[]> PrimaryLightEntityShadowVisPointer { get; init; }
    public IReadOnlyList<uint> PrimaryLightEntityShadowVis { get; init; } = [];
    public XPointer<uint[]> PrimaryLightDynEntShadowVis0Pointer { get; init; }
    public IReadOnlyList<uint> PrimaryLightDynEntShadowVis0 { get; init; } = [];
    public XPointer<uint[]> PrimaryLightDynEntShadowVis1Pointer { get; init; }
    public IReadOnlyList<uint> PrimaryLightDynEntShadowVis1 { get; init; } = [];
    public XPointer<byte[]> PrimaryLightForModelDynEntPointer { get; init; }
    public IReadOnlyList<byte> PrimaryLightForModelDynEnt { get; init; } = [];
    public XPointer<GfxShadowGeometry[]> ShadowGeomPointer { get; init; }
    public IReadOnlyList<GfxShadowGeometry> ShadowGeom { get; init; } = [];
    public XPointer<GfxLightRegion[]> LightRegionPointer { get; init; }
    public IReadOnlyList<GfxLightRegion> LightRegions { get; init; } = [];

    public GfxWorldDpvsStatic Dpvs { get; init; } = new();
    public GfxWorldDpvsDynamic DpvsDyn { get; init; } = new();
    // 0x26C: map vertex checksum.
    public uint MapVertexChecksum { get; init; }
    public uint HeroOnlyLightCount { get; init; }
    public XPointer<GfxHeroOnlyLight[]> HeroOnlyLightsPointer { get; init; }
    public IReadOnlyList<GfxHeroOnlyLight> HeroOnlyLights { get; init; } = [];
    // 0x278: allowed fog-type flags.
    public FogTypesAllowed FogTypesAllowed { get; init; }
    public IReadOnlyList<byte> Pad279To27B { get; init; } = [];
    /// <summary>
    /// PS3 GfxWorld +0x27C: usable bytes in each alternating fragment-program
    /// upload arena. Each serialized arena reserves an additional 0x1000 bytes.
    /// </summary>
    public int FragmentProgramUploadCapacity { get; init; }
    public XPointer<byte[]> FragmentProgramUploadArenaAPointer { get; init; }
    public IReadOnlyList<byte> FragmentProgramUploadArenaA { get; init; } = [];
    public XPointer<byte[]> FragmentProgramUploadArenaBPointer { get; init; }
    public IReadOnlyList<byte> FragmentProgramUploadArenaB { get; init; } = [];
}
