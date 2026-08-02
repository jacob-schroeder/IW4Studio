namespace IW4.Render.Shaders;

public static class MapRenderCodeConstantLayout
{
    // IW4 code constants [0, 0x4B) are float4 draw-context values. Derived
    // matrix groups begin at 0x4B and occupy four transform variants each.
    public const int Float4Count = 0x4B;
}

/// <summary>
/// One immutable direct float4 row. Operational sources contain managed
/// producer values; diagnostic snapshots use the same value shape.
/// </summary>
public sealed record MapRenderDirectCodeConstantRow
{
    public MapRenderDirectCodeConstantRow(
        int sourceRowIndex,
        MapRenderShaderConstantValue value)
    {
        if ((uint)sourceRowIndex >= MapRenderCodeConstantLayout.Float4Count)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowIndex));
        }

        SourceRowIndex = sourceRowIndex;
        Value = value;
    }

    public int SourceRowIndex { get; }

    public MapRenderShaderConstantValue Value { get; }
}

/// <summary>
/// Exact PS3 single-precision gamma-to-linear transfer shared by the
/// fog and scene-light constant writers.
/// </summary>
internal static class GammaColorTransfer
{
    private static readonly float Threshold = Float(0x3D20E411);
    private static readonly float LowScale = Float(0x3D9E8391);
    private static readonly float Offset = Float(0x3D6147AE);
    private static readonly float HighScale = Float(0x3F72A76F);
    private static readonly float Exponent = Float(0x4019999A);

    internal static float ToLinear(float value) =>
        value <= Threshold
            ? value * LowScale
            : MathF.Pow((value + Offset) * HighScale, Exponent);

    private static float Float(uint bits) =>
        BitConverter.Int32BitsToSingle(unchecked((int)bits));
}
