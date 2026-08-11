using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Physics;

public sealed class PhysCollmapAsset : BaseAsset
{
    public const int SerializedSize = 0x48;

    public override XAssetType SerializedAssetType => XAssetType.PhysCollmap;
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public int Count { get; init; }
    public XPointer<PhysGeomInfo[]> GeomsPointer { get; init; }
    public IReadOnlyList<PhysGeomInfo> Geoms { get; init; } = [];
    public PhysMass Mass { get; init; } = new();
    public Bounds Bounds { get; init; } = new();
}
