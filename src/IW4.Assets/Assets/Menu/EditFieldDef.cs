namespace IW4.Assets.Assets.Menu;

public sealed class EditFieldDef
{
    public const int SerializedSize = 0x20;

    public float MinVal { get; init; }
    public float MaxVal { get; init; }
    public float DefVal { get; init; }
    public float Range { get; init; }
    public int MaxChars { get; init; }
    public int MaxCharsGotoNext { get; init; }
    public int MaxPaintChars { get; init; }
    public int PaintOffset { get; init; }
}
