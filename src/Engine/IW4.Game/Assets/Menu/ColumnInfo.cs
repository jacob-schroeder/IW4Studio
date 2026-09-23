using IW4.Game.Math;
using IW4.Game.Assets.Material;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class ColumnInfo
{
    public const int SerializedSize = 0x10;

    public int Pos { get; init; }
    public int Width { get; init; }
    public int MaxChars { get; init; }
    public int Alignment { get; init; }
}
