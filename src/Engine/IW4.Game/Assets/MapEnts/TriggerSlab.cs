using IW4.Game.Math;

namespace IW4.Game.Assets.MapEnts;

public sealed class TriggerSlab
{
    public const int SerializedSize = 0x14;

    public Vec3 Dir { get; init; }
    public float MidPoint { get; init; }
    public float HalfSize { get; init; }
}
