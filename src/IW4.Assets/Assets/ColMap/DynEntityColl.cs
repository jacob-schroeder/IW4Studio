using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class DynEntityColl
{
    public const int SerializedSize = 0x14;

    public ushort Sector { get; init; }
    public ushort NextEntInSector { get; init; }
    public ModelVec2 LinkMins { get; init; }
    public ModelVec2 LinkMaxs { get; init; }
}
