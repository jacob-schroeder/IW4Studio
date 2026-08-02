using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ExpressionEntry
{
    public const int SerializedSize = 0x0c;

    public ExpressionEntryKind Kind { get; init; }

    /// <summary>Operator-only dword at +0x04.  It is intentionally separate
    /// from <see cref="Operand"/>: valid PS3 opcodes are not ExpDataType
    /// discriminators.</summary>
    public int OperationCode { get; init; }

    /// <summary>Operator-only dword at +0x08. It is preserved separately
    /// because it is not an operand value.</summary>
    public int OperatorTail { get; init; }

    /// <summary>Operand-only payload.  It is ignored for an operator entry.</summary>
    public Operand Operand { get; init; } = new();
    public string? StringValue { get; set; }
    public Statement? FunctionStatement { get; set; }
    public bool IsOperator => Kind == ExpressionEntryKind.Operator;
    public bool IsOperand => Kind == ExpressionEntryKind.Operand;
    public OperationEnum Operation => (OperationEnum)OperationCode;
}
