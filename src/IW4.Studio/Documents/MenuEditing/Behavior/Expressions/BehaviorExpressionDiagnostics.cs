namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

/// <summary>Semantic value kinds used by the authoring surface and catalog.</summary>
public enum BehaviorExpressionResultKind
{
    Unknown,
    Boolean,
    Integer,
    Float,
    String,
    Number
}

public enum BehaviorExpressionDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum BehaviorExpressionDiagnosticCode
{
    EmptyExpression,
    InvalidToken,
    UnexpectedToken,
    MissingClosingParenthesis,
    UnknownOperation,
    InvalidArity,
    InvalidOperand,
    InvalidStaticDvarReference,
    InvalidReusableExpressionReference,
    UnsupportedOpaqueExpression,
    UnsupportedRawStatement,
    InvalidStatementShape
}

/// <summary>One user-presentable expression diagnostic. Position is a formula-character offset when available.</summary>
public sealed record BehaviorExpressionDiagnostic(
    BehaviorExpressionDiagnosticCode Code,
    BehaviorExpressionDiagnosticSeverity Severity,
    string Message,
    int? Position = null);

/// <summary>Immutable result used by parsing, formatting, import, and lowering.</summary>
public sealed class BehaviorExpressionResult<T>
{
    private readonly IReadOnlyList<BehaviorExpressionDiagnostic> _diagnostics;

    public BehaviorExpressionResult(
        T? value,
        IEnumerable<BehaviorExpressionDiagnostic>? diagnostics = null)
    {
        Value = value;
        _diagnostics = Array.AsReadOnly((diagnostics ?? []).ToArray());
    }

    public T? Value { get; }
    public IReadOnlyList<BehaviorExpressionDiagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(value =>
        value.Severity == BehaviorExpressionDiagnosticSeverity.Error);
}
