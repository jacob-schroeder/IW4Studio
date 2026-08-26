using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Pointers;

namespace IW4.FastFiles.Loaders.Assets.GfxMap;

internal sealed class GfxWorldHeader
{
    public XPointer<string> NamePointer { get; init; }
    public XPointer<string> BaseNamePointer { get; init; }
    public int PlaneCount { get; init; }
    public int NodeCount { get; init; }
    public int SurfaceCount { get; init; }
    public uint SkyCount { get; init; }
    public XPointer<GfxSky[]> SkiesPointer { get; init; }
    public int SunPrimaryLightIndex { get; init; }
    public int PrimaryLightCount { get; init; }
    public int SortKeyLitDecal { get; init; }
    public int SortKeyEffectDecal { get; init; }
    public int SortKeyEffectAuto { get; init; }
    public int SortKeyDistortion { get; init; }
    public GfxWorldDpvsPlanes DpvsPlanes { get; init; } = new();
    public XPointer<GfxCellTreeCount[]> CellTreeCountsPointer { get; init; }
    public XPointer<GfxAabbTree[]> CellTreesPointer { get; init; }
    public XPointer<GfxCell[]> CellsPointer { get; init; }
    public GfxWorldDraw WorldDraw { get; init; } = new();
    public GfxLightGrid LightGrid { get; init; } = new();
    public int ModelCount { get; init; }
    public XPointer<GfxBrushModel[]> ModelsPointer { get; init; }
    public IReadOnlyList<float> Mins { get; init; } = [];
    public IReadOnlyList<float> Maxs { get; init; } = [];
    public uint Checksum { get; init; }
    public int MaterialMemoryCount { get; init; }
    public XPointer<MaterialMemory[]> MaterialMemoryPointer { get; init; }
    public Sunflare Sun { get; init; } = new();
    public IReadOnlyList<float> OutdoorLookupMatrix { get; init; } = [];
    public XPointer<GfxImageAsset> OutdoorImagePointer { get; init; }
    public XPointer<uint[]> CellCasterBitsPointer { get; init; }
    public XPointer<uint[]> CellCasterBits2Pointer { get; init; }
    public XPointer<GfxSceneDynModel[]> SceneDynModelPointer { get; init; }
    public XPointer<GfxSceneDynBrush[]> SceneDynBrushPointer { get; init; }
    public XPointer<uint[]> PrimaryLightEntityShadowVisPointer { get; init; }
    public XPointer<uint[]> PrimaryLightDynEntShadowVis0Pointer { get; init; }
    public XPointer<uint[]> PrimaryLightDynEntShadowVis1Pointer { get; init; }
    public XPointer<byte[]> PrimaryLightForModelDynEntPointer { get; init; }
    public XPointer<GfxShadowGeometry[]> ShadowGeomPointer { get; init; }
    public XPointer<GfxLightRegion[]> LightRegionPointer { get; init; }
    public GfxWorldDpvsStatic Dpvs { get; init; } = new();
    public GfxWorldDpvsDynamic DpvsDyn { get; init; } = new();
    public uint MapVertexChecksum { get; init; }
    public uint HeroOnlyLightCount { get; init; }
    public XPointer<GfxHeroOnlyLight[]> HeroOnlyLightsPointer { get; init; }
    public FogTypesAllowed FogTypesAllowed { get; init; }
    public IReadOnlyList<byte> Pad279To27B { get; init; } = [];
    public int FragmentProgramUploadCapacity { get; init; }
    public XPointer<byte[]> FragmentProgramUploadArenaAPointer { get; init; }
    public XPointer<byte[]> FragmentProgramUploadArenaBPointer { get; init; }
}
