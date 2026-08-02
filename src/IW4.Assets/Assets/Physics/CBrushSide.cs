using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Physics;

public sealed class CBrushSide
{
    public const int SerializedSize = 0x08;

    public XPointer<CPlane> PlanePointer { get; init; }
    public CPlane? Plane { get; init; }
    public ushort MaterialNum { get; init; }
    public byte FirstAdjacentSideOffset { get; init; }
    public byte EdgeCount { get; init; }
}
