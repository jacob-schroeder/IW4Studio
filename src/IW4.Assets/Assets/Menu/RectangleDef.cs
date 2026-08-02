using IW4.Assets.Math;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class RectangleDef
{
    // Four float32 values, two alignment bytes, and two padding bytes.
    public const int SerializedSize = 0x14;

    public float X { get; init; }
    public float Y { get; init; }
    public float W { get; init; }
    public float H { get; init; }
    public HorizontalAlign HorzAlign { get; init; }
    public VerticalAlign VertAlign { get; init; }
    public ushort Pad12 { get; init; }
}
