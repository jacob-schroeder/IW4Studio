namespace IW4.Game.Assets.TechniqueSet;

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

    // Several table boundaries alias real sources. Display the binding at that index.
    public string SourceName => Source switch
    {
        MaterialConstantSource.LightPosition => nameof(MaterialConstantSource.LightPosition),
        MaterialConstantSource.PixelCostFractions => nameof(MaterialConstantSource.PixelCostFractions),
        MaterialConstantSource.DofEquationViewModelAndFarBlur => nameof(MaterialConstantSource.DofEquationViewModelAndFarBlur),
        MaterialConstantSource.BaseLightingCoords => nameof(MaterialConstantSource.BaseLightingCoords),
        MaterialConstantSource.CodeMeshArgument1 => nameof(MaterialConstantSource.CodeMeshArgument1),
        MaterialConstantSource.ViewMatrix => nameof(MaterialConstantSource.ViewMatrix),
        _ => Source.ToString()
    };

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
