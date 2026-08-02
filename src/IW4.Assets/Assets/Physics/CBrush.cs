using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Physics;

public sealed class CBrush
{
    public const int SerializedSize = 0x24;

    public ushort NumSides { get; init; }
    public ushort GlassPieceIndex { get; init; }
    public XPointer<CBrushSide[]> SidesPointer { get; init; }
    public IReadOnlyList<CBrushSide> Sides { get; init; } = [];
    public XPointer<byte[]> BaseAdjacentSidePointer { get; init; }
    public IReadOnlyList<byte> BaseAdjacentSide { get; init; } = [];
    public IReadOnlyList<short> AxialMaterialNum { get; init; } = [];
    public IReadOnlyList<byte> FirstAdjacentSideOffsets { get; init; } = [];
    public IReadOnlyList<byte> EdgeCount { get; init; } = [];
}
