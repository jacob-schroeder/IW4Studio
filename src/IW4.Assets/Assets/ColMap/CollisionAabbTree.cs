using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class CollisionAabbTree
{
    public const int SerializedSize = 0x20;

    public ModelVec3 Origin { get; init; }
    public ushort MaterialIndex { get; init; }
    public ushort ChildCount { get; init; }
    public ModelVec3 HalfSize { get; init; }
    public int FirstChildOrPartitionIndex { get; init; }
}
