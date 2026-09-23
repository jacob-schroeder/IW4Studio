using IW4.Game.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

public static class CodeConstantLayout
{
    // IW4 code constants [0, 0x4B) are float4 draw-context values. Derived
    // matrix groups begin at 0x4B and occupy four transform variants each.
    public const int Float4Count = (int)MaterialConstantSource.Float4Count;
}

/// <summary>
/// One immutable direct float4 row. Operational sources contain managed
/// producer values; diagnostic snapshots use the same value shape.
/// </summary>
public sealed record DirectCodeConstantRow
{
    public DirectCodeConstantRow(
        int sourceRowIndex,
        ShaderConstantValue value)
    {
        if ((uint)sourceRowIndex >= CodeConstantLayout.Float4Count)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowIndex));
        }

        SourceRowIndex = sourceRowIndex;
        Value = value;
    }

    public int SourceRowIndex { get; }

    public ShaderConstantValue Value { get; }
}
