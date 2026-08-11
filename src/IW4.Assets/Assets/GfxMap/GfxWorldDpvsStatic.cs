using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldDpvsStatic
{
    public const int SerializedSize = 0x68;

    public uint SModelCount { get; init; }
    public uint StaticSurfaceCount { get; init; }
    public uint LitSurfsBegin { get; init; }
    public uint LitSurfsEnd { get; init; }
    public IReadOnlyList<uint> VisibilityCounts { get; init; } = [];
    public IReadOnlyList<XPointer<uint[]>> SModelVisDataPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<uint>> SModelVisData { get; init; } = [];
    public IReadOnlyList<XPointer<uint[]>> SurfaceVisDataPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<uint>> SurfaceVisData { get; init; } = [];
    public XPointer<ushort[]> SortedSurfIndexPointer { get; init; }
    public XBlockAddress? SortedSurfIndexAddress { get; init; }
    public IReadOnlyList<ushort> SortedSurfIndex { get; set; } = [];
    public XPointer<GfxStaticModelInst[]> SModelInstsPointer { get; init; }
    public IReadOnlyList<GfxStaticModelInst> SModelInsts { get; init; } = [];
    public XPointer<GfxSurface[]> SurfacesPointer { get; init; }
    public XBlockAddress? SurfacesAddress { get; init; }
    public IReadOnlyList<GfxSurface> Surfaces { get; set; } = [];
    // Runtime-to-serialized surface index mapping maintained during sorting.
    public IReadOnlyList<int> AuthoredSurfaceIndexByRuntimeSlot { get; set; } = [];
    public XPointer<GfxSurfaceBounds[]> SurfaceBoundsPointer { get; init; }
    public XBlockAddress? SurfaceBoundsAddress { get; init; }
    public IReadOnlyList<GfxSurfaceBounds> SurfaceBounds { get; set; } = [];
    public XPointer<GfxStaticModelDrawInst[]> SModelDrawInstsPointer { get; init; }
    public IReadOnlyList<GfxStaticModelDrawInst> SModelDrawInsts { get; init; } = [];
    public XPointer<GfxMapDrawSurf[]> SurfaceMaterialsPointer { get; init; }
    public XBlockAddress? SurfaceMaterialsAddress { get; init; }
    // Rebuilt by the PS3 post-load world-surface material-key pass.
    public IReadOnlyList<GfxMapDrawSurf> SurfaceMaterials { get; set; } = [];
    public XPointer<uint[]> SurfaceCastsSunShadowPointer { get; init; }
    public XBlockAddress? SurfaceCastsSunShadowAddress { get; init; }
    public IReadOnlyList<uint> SurfaceCastsSunShadow { get; set; } = [];
    public uint UsageCount { get; init; }
}
