using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.XModel;

public sealed class XSurfaceCollisionTree
{
    public const int SerializedSize = 0x28;

    public XBlockAddress? RuntimeAddress { get; init; }
    public Vec3 Trans { get; init; } = new();
    public Vec3 Scale { get; init; } = new();
    public int NodeCount { get; init; }
    public XPointer<XSurfaceCollisionNode[]> NodesPointer { get; init; }
    public XBlockAddress? NodesRuntimeAddress { get; init; }
    public IReadOnlyList<XSurfaceCollisionNode> Nodes { get; init; } = [];
    public int LeafCount { get; init; }
    public XPointer<XSurfaceCollisionLeaf[]> LeafsPointer { get; init; }
    public XBlockAddress? LeafsRuntimeAddress { get; init; }
    public IReadOnlyList<XSurfaceCollisionLeaf> Leafs { get; init; } = [];
}
