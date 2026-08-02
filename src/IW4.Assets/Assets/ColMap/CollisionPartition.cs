using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class CollisionPartition
{
    public const int SerializedSize = 0x0C;

    public byte TriCount { get; init; }
    public byte BorderCount { get; init; }
    public byte FirstVertSegment { get; init; }
    public byte Pad03 { get; init; }
    public int FirstTri { get; init; }
    public XPointer<CollisionBorder[]> BordersPointer { get; init; }
    public IReadOnlyList<CollisionBorder> Borders { get; init; } = [];
}
