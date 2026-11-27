using IW4.Game.Math;
namespace IW4.Game.Assets.FxMap;

public sealed record FxGlassPieceDynamics(
    int FallTime,
    int PhysObjId,
    int PhysJointId,
    Vec3 Vel,
    Vec3 AVel)
{
    public const int SerializedSize = 0x24;
}
