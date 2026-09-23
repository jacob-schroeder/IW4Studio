using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Physics;

public sealed class CBrushSide
{
    public const int SerializedSize = 0x08;

    public XPointer<CPlane> PlanePointer { get; init; }
    public CPlane? Plane { get; init; }
    public ushort MaterialNum { get; init; }
    public byte FirstAdjacentSideOffset { get; init; }
    public byte EdgeCount { get; init; }
}
