using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Physics;

public sealed class CPlane
{
    public const int SerializedSize = 0x14;

    public Vec3 Normal { get; init; }
    public float Dist { get; init; }
    public byte Type { get; init; }
    public byte SignBits { get; init; }
    public IReadOnlyList<byte> Pad12 { get; init; } = [];
}
