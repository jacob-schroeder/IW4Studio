using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class CollisionBorder
{
    public const int SerializedSize = 0x1C;

    public IReadOnlyList<float> DistEq { get; init; } = [];
    public float ZBase { get; init; }
    public float ZSlope { get; init; }
    public float Start { get; init; }
    public float Length { get; init; }
}
