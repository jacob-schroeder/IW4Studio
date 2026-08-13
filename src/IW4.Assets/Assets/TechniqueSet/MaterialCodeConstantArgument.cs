namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// The four-byte material argument union shape used by code vertex and code
/// pixel constants: a table index followed by the first row and row count.
/// </summary>
public readonly record struct MaterialCodeConstantArgument(
    MaterialConstantSource Source,
    byte FirstRow,
    byte RowCount)
{
    public ushort SourceIndex => (ushort)Source;

    public uint PackedValue =>
        ((uint)SourceIndex << 16) |
        ((uint)FirstRow << 8) |
        RowCount;

    public int Raw => unchecked((int)PackedValue);

    public static MaterialCodeConstantArgument FromRaw(int raw)
    {
        uint packed = unchecked((uint)raw);
        return new MaterialCodeConstantArgument(
            (MaterialConstantSource)(packed >> 16),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));
    }
}
