using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class Operand
{
    public const int SerializedSize = 0x08;

    public ExpDataType DataType { get; init; }
    public OperandValue Value { get; init; } = new IntOperandValue(0);
    public int EncodedValue => Value.EncodedValue;
}
