using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class DynEntityColl
{
    public const int SerializedSize = 0x14;

    public ushort Sector { get; init; }
    public ushort NextEntInSector { get; init; }
    public ModelVec2 LinkMins { get; init; }
    public ModelVec2 LinkMaxs { get; init; }
}
