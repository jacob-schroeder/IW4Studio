namespace IW4.Gsc.Syntax;

public enum GscDiagnosticSeverity
{
    Warning,
    Error
}

/// <summary>The compiler pipeline stage that produced a diagnostic.</summary>
public enum GscDiagnosticStage
{
    Lexical,
    Syntax,
    Semantic
}

/// <summary>A stable, location-aware diagnostic produced from GSC source.</summary>
public sealed record GscDiagnostic
{
    public GscDiagnostic(
        string code,
        GscDiagnosticStage stage,
        GscDiagnosticSeverity severity,
        GscTextSpan span,
        GscLinePositionSpan lineSpan,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(stage))
            throw new ArgumentOutOfRangeException(nameof(stage));
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));

        Code = code;
        Stage = stage;
        Severity = severity;
        Span = span;
        LineSpan = lineSpan;
        Message = message;
    }

    public string Code { get; }

    public GscDiagnosticStage Stage { get; }

    public GscDiagnosticSeverity Severity { get; }

    public GscTextSpan Span { get; }

    public GscLinePositionSpan LineSpan { get; }

    public string Message { get; }
}
