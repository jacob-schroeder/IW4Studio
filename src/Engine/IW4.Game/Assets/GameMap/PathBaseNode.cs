using IW4.Game.Math;

namespace IW4.Game.Assets.GameMap;

public sealed class PathBaseNode
{
    public const int SerializedSize = 0x10;

    public Vec3 Origin { get; init; }
    public uint Type { get; init; }
}
