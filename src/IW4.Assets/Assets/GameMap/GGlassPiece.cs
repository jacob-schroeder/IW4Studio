using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GameMap;

public sealed class GGlassPiece
{
    public const int SerializedSize = 0x0C;

    public int Offset { get; init; }

    // 0x00..0x07: G_GlassPiece state fields.
    public ushort DamageTaken { get; init; }
    public ushort CollapseTime { get; init; }
    public int LastStateChangeTime { get; init; }

    // 0x08..0x0B: packed impact direction and position.
    public ushort PackedImpactDir { get; init; }
    public ushort PackedImpactPos { get; init; }
}
