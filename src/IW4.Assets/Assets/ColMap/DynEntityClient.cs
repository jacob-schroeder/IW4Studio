using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class DynEntityClient
{
    public const int SerializedSize = 0x0C;

    public int PhysObjId { get; init; }
    public ushort Flags { get; init; }
    public ushort LightingHandle { get; init; }
    public int Health { get; init; }
}
