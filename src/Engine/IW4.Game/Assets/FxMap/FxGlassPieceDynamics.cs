namespace IW4.Game.Assets.FxMap;

public sealed record FxGlassPieceDynamics(
    int FallTime,
    int PhysObjId,
    int PhysJointId,
    FxVec3 Vel,
    FxVec3 AVel)
{
    public const int SerializedSize = 0x24;
}
