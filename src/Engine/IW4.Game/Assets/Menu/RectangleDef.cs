using IW4.Game.Math;
using IW4.Game.Assets.Material;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

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
