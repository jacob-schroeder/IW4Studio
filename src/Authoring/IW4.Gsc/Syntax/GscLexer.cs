namespace IW4.Gsc.Syntax;

internal static class GscLexer
{
    private const int NormalStartState = 3;
    private const int BlockCommentStartState = 5;
    private const int MaximumStringContentByteLength = 0x1fff;

    internal static GscLexResult Lex(
        GscSourceText source,
        CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> bytes = source.Bytes;
        var tokens = new List<GscToken>();
        int offset = 0;
        int startState = NormalStartState;

        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int action, int acceptedLength) = ScanAction(bytes, offset, startState);
            acceptedLength = Math.Min(acceptedLength, bytes.Length - offset);
            if (acceptedLength <= 0)
                acceptedLength = 1;

            GscTextSpan span = source.GetTextSpan(offset, acceptedLength);
            if (action == 6)
            {
                startState = BlockCommentStartState;
            }
            else if (action == 2)
            {
                startState = NormalStartState;
            }

            if (action is 7 or 8)
            {
                int delimiterLength = action == 7 ? 2 : 3;
                if (acceptedLength - delimiterLength > MaximumStringContentByteLength)
                {
                    return GscLexResult.Failed(
                        tokens,
                        new GscLexError(
                            GscDiagnosticCodes.MaximumStringLengthExceeded,
                            span,
                            "Maximum string length exceeded."));
                }
            }

            GscTokenKind? tokenKind = GetTokenKind(action);
            if (tokenKind == GscTokenKind.BadToken || action == 0)
            {
                return GscLexResult.Failed(
                    tokens,
                    new GscLexError(
                        GscDiagnosticCodes.BadToken,
                        span,
                        $"Bad token {GscDiagnosticText.Quote(source.GetText(span))}."));
            }

            if (tokenKind is not null and not GscTokenKind.EndOfFile)
                tokens.Add(new GscToken(tokenKind.Value, span));

            offset += acceptedLength;
        }

        return GscLexResult.Succeeded(tokens);
    }

    private static (int Action, int AcceptedLength) ScanAction(
        ReadOnlySpan<byte> source,
        int tokenStart,
        int startState)
    {
        ReadOnlySpan<short> accept = Iw4GscLexerTables.Accept;
        ReadOnlySpan<byte> characterClasses = Iw4GscLexerTables.CharacterClasses;
        ReadOnlySpan<byte> meta = Iw4GscLexerTables.Meta;
        ReadOnlySpan<short> bases = Iw4GscLexerTables.Base;
        ReadOnlySpan<short> defaultStates = Iw4GscLexerTables.DefaultStates;
        ReadOnlySpan<short> nextStates = Iw4GscLexerTables.NextStates;
        ReadOnlySpan<short> check = Iw4GscLexerTables.Check;

        int state = startState;
        int lastAcceptingState = state;
        int lastAcceptingPosition = tokenStart;
        int position = tokenStart;
        int virtualEnd = source.Length + 1;

        while (true)
        {
            if (accept[state] != 0)
            {
                lastAcceptingState = state;
                lastAcceptingPosition = position;
            }

            if (position >= virtualEnd)
                return (accept[lastAcceptingState], lastAcceptingPosition - tokenStart);

            int characterClass = position == source.Length || source[position] == 0
                ? 1
                : characterClasses[source[position]];
            int index = bases[state] + characterClass;
            while (check[index] != state)
            {
                state = defaultStates[state];
                if (state > Iw4GscLexerTables.MetaThreshold)
                    characterClass = meta[characterClass];
                index = bases[state] + characterClass;
            }

            position++;
            state = nextStates[index];
            if (bases[state] == Iw4GscLexerTables.JamBase)
                break;
        }

        int action = accept[state];
        return action != 0
            ? (action, position - tokenStart)
            : (accept[lastAcceptingState], lastAcceptingPosition - tokenStart);
    }

    private static GscTokenKind? GetTokenKind(int action)
    {
        if (action is 1 or 2 or 3 or 4 or 5 or 6 or 99)
            return null;
        if (action == 7)
            return GscTokenKind.String;
        if (action == 8)
            return GscTokenKind.LocalizedString;
        if (action is >= 9 and <= 36)
            return (GscTokenKind)(action + 252);

        return action switch
        {
            37 => GscTokenKind.Comma,
            38 => GscTokenKind.Dot,
            39 => GscTokenKind.QuestionMark,
            40 => GscTokenKind.Colon,
            41 => GscTokenKind.Assign,
            42 => GscTokenKind.Semicolon,
            >= 43 and <= 76 => (GscTokenKind)(action + 252),
            >= 77 and <= 95 => (GscTokenKind)(action + 253),
            96 => GscTokenKind.Identifier,
            97 => GscTokenKind.Path,
            98 => GscTokenKind.BadToken,
            101 or 102 or 103 => GscTokenKind.EndOfFile,
            _ => GscTokenKind.BadToken
        };
    }
}

internal sealed class GscLexResult
{
    private GscLexResult(GscToken[] tokens, GscLexError? error)
    {
        Tokens = tokens;
        Error = error;
    }

    internal GscToken[] Tokens { get; }

    internal GscLexError? Error { get; }

    internal static GscLexResult Succeeded(List<GscToken> tokens) =>
        new([.. tokens], error: null);

    internal static GscLexResult Failed(List<GscToken> tokens, GscLexError error) =>
        new([.. tokens], error);
}

internal sealed record GscLexError(string Code, GscTextSpan Span, string Message);
