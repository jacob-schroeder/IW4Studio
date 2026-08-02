using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class CModel
{
    public const int SerializedSize = 0x44;

    public ModelVec3 Mins { get; init; }
    public ModelVec3 Maxs { get; init; }
    public float Radius { get; init; }
    public CLeaf Leaf { get; init; } = new();
}
