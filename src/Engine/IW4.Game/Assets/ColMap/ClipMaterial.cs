using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class ClipMaterial
{
    public const int SerializedSize = 0x0C;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public int SurfaceFlags { get; init; }
    public int Contents { get; init; }
}
