using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class CLeafBrushNode
{
    public const int SerializedSize = 0x14;

    public byte Axis { get; init; }
    public byte Pad01 { get; init; }
    public short LeafBrushCount { get; init; }
    public int Contents { get; init; }
    public CLeafBrushNodeData Data { get; init; } = new();
}
