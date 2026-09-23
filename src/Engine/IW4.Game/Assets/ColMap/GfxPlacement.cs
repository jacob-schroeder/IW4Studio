using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class GfxPlacement
{
    public const int SerializedSize = 0x1C;

    public IReadOnlyList<float> Quat { get; init; } = [];
    public ModelVec3 Origin { get; init; }
}
