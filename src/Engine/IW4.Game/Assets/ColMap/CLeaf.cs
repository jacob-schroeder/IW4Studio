using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class CLeaf
{
    public const int SerializedSize = 0x28;

    public ushort FirstCollAabbIndex { get; init; }
    public ushort CollAabbCount { get; init; }
    public int BrushContents { get; init; }
    public int TerrainContents { get; init; }
    public ModelVec3 Mins { get; init; }
    public ModelVec3 Maxs { get; init; }
    public int LeafBrushNode { get; init; }
}
