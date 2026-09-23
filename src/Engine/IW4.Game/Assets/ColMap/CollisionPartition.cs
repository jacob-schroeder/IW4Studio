using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

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
