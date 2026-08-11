using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class ClipMapAsset : BaseAsset
{
    private int _isInUse;

    public const int SerializedSize = 0x100;

    // ColMapSp (0x0D) and ColMapMp (0x0E) share the same serialized body and
    // are registered in the ColMapMp pool family.
    public XAssetType Type => XAssetType.ColMapMp;
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: registration sets this to 1 before capturing the 0x100-byte pool copy.
    public int IsInUse
    {
        get => _isInUse;
        init => _isInUse = value;
    }
    public int PlaneCount { get; init; }
    public XPointer<CPlane[]> PlanesPointer { get; init; }
    public IReadOnlyList<CPlane> Planes { get; init; } = [];
    public int NumStaticModels { get; init; }
    public XPointer<ClipStaticModel[]> StaticModelListPointer { get; init; }
    public IReadOnlyList<ClipStaticModel> StaticModelList { get; init; } = [];
    public int NumMaterials { get; init; }
    public XPointer<ClipMaterial[]> MaterialsPointer { get; init; }
    public IReadOnlyList<ClipMaterial> Materials { get; init; } = [];
    public int NumBrushSides { get; init; }
    public XPointer<CBrushSide[]> BrushSidesPointer { get; init; }
    public IReadOnlyList<CBrushSide> BrushSides { get; init; } = [];
    public int NumBrushEdges { get; init; }
    public XPointer<byte[]> BrushEdgesPointer { get; init; }
    public IReadOnlyList<byte> BrushEdges { get; init; } = [];
    public int NumNodes { get; init; }
    public XPointer<CNode[]> NodesPointer { get; init; }
    public IReadOnlyList<CNode> Nodes { get; init; } = [];
    public int NumLeafs { get; init; }
    public XPointer<CLeaf[]> LeafsPointer { get; init; }
    public IReadOnlyList<CLeaf> Leafs { get; init; } = [];
    public int LeafBrushNodesCount { get; init; }
    public XPointer<CLeafBrushNode[]> LeafBrushNodesPointer { get; init; }
    public IReadOnlyList<CLeafBrushNode> LeafBrushNodes { get; init; } = [];
    public int NumLeafBrushes { get; init; }
    public XPointer<ushort[]> LeafBrushesPointer { get; init; }
    public IReadOnlyList<ushort> LeafBrushes { get; init; } = [];
    public int NumLeafSurfaces { get; init; }
    public XPointer<uint[]> LeafSurfacesPointer { get; init; }
    public IReadOnlyList<uint> LeafSurfaces { get; init; } = [];
    public int VertCount { get; init; }
    public XPointer<ModelVec3[]> VertsPointer { get; init; }
    public IReadOnlyList<ModelVec3> Verts { get; init; } = [];
    public int TriCount { get; init; }
    public XPointer<ushort[]> TriIndicesPointer { get; init; }
    public IReadOnlyList<ushort> TriIndices { get; init; } = [];
    public XPointer<byte[]> TriEdgeIsWalkablePointer { get; init; }
    public IReadOnlyList<byte> TriEdgeIsWalkable { get; init; } = [];
    public int BorderCount { get; init; }
    public XPointer<CollisionBorder[]> BordersPointer { get; init; }
    public IReadOnlyList<CollisionBorder> Borders { get; init; } = [];
    public int PartitionCount { get; init; }
    public XPointer<CollisionPartition[]> PartitionsPointer { get; init; }
    public IReadOnlyList<CollisionPartition> Partitions { get; init; } = [];
    public int AabbTreeCount { get; init; }
    public XPointer<CollisionAabbTree[]> AabbTreesPointer { get; init; }
    public IReadOnlyList<CollisionAabbTree> AabbTrees { get; init; } = [];
    public int NumSubModels { get; init; }
    public XPointer<CModel[]> CModelsPointer { get; init; }
    public IReadOnlyList<CModel> CModels { get; init; } = [];
    public ushort NumBrushes { get; init; }
    public ushort Pad8ETo8F { get; init; }
    public XPointer<CBrush[]> BrushesPointer { get; init; }
    public IReadOnlyList<CBrush> Brushes { get; init; } = [];
    public XPointer<ModelBounds[]> BrushBoundsPointer { get; init; }
    public IReadOnlyList<ModelBounds> BrushBounds { get; init; } = [];
    public XPointer<uint[]> BrushContentsPointer { get; init; }
    public IReadOnlyList<uint> BrushContents { get; init; } = [];
    public XPointer<MapEntsAsset> MapEntsPointer { get; init; }
    public MapEntsAsset? MapEnts { get; init; }
    public ushort SModelNodeCount { get; init; }
    public ushort PadA2ToA3 { get; init; }
    public XPointer<SModelAabbNode[]> SModelNodesPointer { get; init; }
    public IReadOnlyList<SModelAabbNode> SModelNodes { get; init; } = [];
    public IReadOnlyList<ushort> DynEntCount { get; init; } = [];
    public IReadOnlyList<XPointer<DynEntityDef[]>> DynEntDefListPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<DynEntityDef>> DynEntDefList { get; init; } = [];
    public IReadOnlyList<XPointer<DynEntityPose[]>> DynEntPoseListPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<DynEntityPose>> DynEntPoseList { get; init; } = [];
    public IReadOnlyList<XPointer<DynEntityClient[]>> DynEntClientListPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<DynEntityClient>> DynEntClientList { get; init; } = [];
    public IReadOnlyList<XPointer<DynEntityColl[]>> DynEntCollListPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<DynEntityColl>> DynEntCollList { get; init; } = [];
    public uint Checksum { get; init; }
    // 0xD0..0xFF: preserved tail padding.
    public IReadOnlyList<byte> PadD0ToFF { get; init; } = [];

    internal void MarkInUseForRegistration() => _isInUse = 1;
}
