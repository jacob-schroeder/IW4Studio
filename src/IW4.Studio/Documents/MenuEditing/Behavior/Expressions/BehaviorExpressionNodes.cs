using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

/// <summary>
/// Base type for the Desktop-safe semantic expression tree. These nodes never
/// expose <see cref="ExpressionEntry"/> or a packed pointer to callers.
/// </summary>
public abstract class BehaviorExpression
{
}

public sealed class BehaviorIntegerExpression(int value) : BehaviorExpression
{
    public int Value { get; } = value;
}

public sealed class BehaviorFloatExpression(float value) : BehaviorExpression
{
    public float Value { get; } = value;
}

public sealed class BehaviorStringExpression(string value) : BehaviorExpression
{
    public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
}

public sealed class BehaviorUnaryExpression(
    OperationEnum operation,
    BehaviorExpression operand) : BehaviorExpression
{
    public OperationEnum Operation { get; } = operation;
    public BehaviorExpression Operand { get; } = operand ?? throw new ArgumentNullException(nameof(operand));
}

public sealed class BehaviorBinaryExpression(
    OperationEnum operation,
    BehaviorExpression left,
    BehaviorExpression right) : BehaviorExpression
{
    public OperationEnum Operation { get; } = operation;
    public BehaviorExpression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    public BehaviorExpression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));
}

public sealed class BehaviorCallExpression : BehaviorExpression
{
    private readonly IReadOnlyList<BehaviorExpression> _arguments;

    public BehaviorCallExpression(
        OperationEnum operation,
        IEnumerable<BehaviorExpression> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Operation = operation;
        _arguments = Array.AsReadOnly(arguments.ToArray());
        if (_arguments.Any(argument => argument is null))
            throw new ArgumentException("A call cannot contain a null argument.", nameof(arguments));
    }

    public OperationEnum Operation { get; }
    public IReadOnlyList<BehaviorExpression> Arguments => _arguments;
}

/// <summary>Stable semantic identity for one support-table reusable expression.</summary>
public readonly record struct BehaviorReusableExpressionId(int Index)
{
    public override string ToString() => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>References a reusable support-table expression without exposing its statement pointer.</summary>
public sealed class BehaviorReusableExpressionReferenceExpression(
    BehaviorReusableExpressionId referenceId) : BehaviorExpression
{
    public BehaviorReusableExpressionId ReferenceId { get; } = referenceId;
}

/// <summary>References a static dvar by stable support-table index and friendly name.</summary>
public sealed class BehaviorStaticDvarExpression(
    OperationEnum operation,
    BehaviorStaticDvarReference dvar) : BehaviorExpression
{
    public OperationEnum Operation { get; } = operation;
    public BehaviorStaticDvarReference Dvar { get; } = dvar ?? throw new ArgumentNullException(nameof(dvar));
}

/// <summary>
/// Imported content that cannot safely be represented by the guided semantic
/// tree. It is displayable, but must remain untouched to retain its raw wire form.
/// </summary>
public sealed class BehaviorOpaqueExpression(string reason) : BehaviorExpression
{
    public string Reason { get; } = reason ?? throw new ArgumentNullException(nameof(reason));
}
