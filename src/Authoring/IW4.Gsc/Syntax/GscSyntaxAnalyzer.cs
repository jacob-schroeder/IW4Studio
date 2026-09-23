namespace IW4.Gsc.Syntax;

/// <summary>Runs the recovered IW4 scanner and LR grammar without game runtime state.</summary>
public sealed class GscSyntaxAnalyzer : IGscSyntaxAnalyzer
{
    public GscSyntaxResult Analyze(
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Analyze(new GscSourceText(source), cancellationToken);
    }

    public GscSyntaxResult Analyze(
        GscSourceText source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        GscLexResult lexResult = GscLexer.Lex(source, cancellationToken);
        if (lexResult.Error is not null)
        {
            return new GscSyntaxResult(
                lexResult.Tokens,
                [CreateDiagnostic(source, lexResult.Error)]);
        }

        GscParseResult parseResult = GscParser.Parse(
            lexResult.Tokens,
            source.Length,
            cancellationToken);
        if (parseResult.IsAccepted)
        {
            return new GscSyntaxResult(
                lexResult.Tokens,
                [],
                parseResult.SyntaxTree ?? throw new InvalidOperationException(
                    "An accepted parse did not produce a syntax tree."));
        }

        GscToken unexpectedToken = parseResult.UnexpectedToken;
        bool isEndOfFile = unexpectedToken.Kind == GscTokenKind.EndOfFile;
        string message = CreateParserMessage(
            source,
            unexpectedToken,
            parseResult.ExpectedTokens);
        string code = isEndOfFile
            ? GscDiagnosticCodes.UnexpectedEndOfFile
            : GscDiagnosticCodes.BadSyntax;

        return new GscSyntaxResult(
            lexResult.Tokens,
            [CreateDiagnostic(
                source,
                code,
                GscDiagnosticStage.Syntax,
                unexpectedToken.Span,
                message)]);
    }

    private static string CreateParserMessage(
        GscSourceText source,
        GscToken unexpectedToken,
        IReadOnlyList<GscTokenKind> expectedTokens)
    {
        const int maximumDisplayedExpectedTokens = 4;
        bool isEndOfFile = unexpectedToken.Kind == GscTokenKind.EndOfFile;
        string unexpected = isEndOfFile
            ? "end of file"
            : GscDiagnosticText.Quote(source.GetText(unexpectedToken.Span));

        if (expectedTokens.Count is > 0 and <= maximumDisplayedExpectedTokens)
        {
            return $"Expected {FormatExpectedTokens(expectedTokens)} before {unexpected}.";
        }

        return isEndOfFile
            ? "Unexpected end of file found."
            : $"Bad syntax near {unexpected}.";
    }

    private static string FormatExpectedTokens(
        IReadOnlyList<GscTokenKind> expectedTokens)
    {
        string[] descriptions = expectedTokens
            .Select(GscTokenDisplay.Describe)
            .ToArray();
        return descriptions.Length switch
        {
            1 => descriptions[0],
            2 => $"{descriptions[0]} or {descriptions[1]}",
            _ => $"{string.Join(", ", descriptions[..^1])}, or {descriptions[^1]}"
        };
    }

    private static GscDiagnostic CreateDiagnostic(
        GscSourceText source,
        GscLexError error) =>
        CreateDiagnostic(
            source,
            error.Code,
            GscDiagnosticStage.Lexical,
            error.Span,
            error.Message);

    private static GscDiagnostic CreateDiagnostic(
        GscSourceText source,
        string code,
        GscDiagnosticStage stage,
        GscTextSpan span,
        string message) =>
        new(
            code,
            stage,
            GscDiagnosticSeverity.Error,
            span,
            source.GetLinePositionSpan(span),
            message);
}
