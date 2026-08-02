using IW4.Assets.Math;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ColumnInfo
{
    public const int SerializedSize = 0x10;

    public int Pos { get; init; }
    public int Width { get; init; }
    public int MaxChars { get; init; }
    public int Alignment { get; init; }
}
