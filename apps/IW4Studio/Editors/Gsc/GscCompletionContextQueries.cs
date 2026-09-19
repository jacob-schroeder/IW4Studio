using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Recovers completion contexts from a potentially incomplete editor buffer.
/// The recovered syntax tokens keep comments, strings, and invalid operators
/// from being mistaken for completion triggers.
/// </summary>
internal static class GscCompletionContextQueries
{
    internal static GscCompletionContext? Find(
        string source,
        int caretOffset,
        bool requireAutomaticContext,
        IReadOnlyList<GscToken> tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();
        if (caretOffset < 0 || caretOffset > source.Length)
            return null;

        GscFieldCompletionContext? field = FindFieldContext(
            source,
            caretOffset,
            tokens);
        if (field is not null)
            return field;

        GscCallableCompletionContext callable = FindCallableContext(
            source,
            caretOffset);
        if (!requireAutomaticContext)
            return callable;
        if (callable.Prefix.Length < 2 || caretOffset == 0)
            return null;

        int probeOffset = caretOffset - 1;
        int tokenIndex = FindTokenContainingOffset(tokens, probeOffset);
        if (tokenIndex < 0)
            return null;

        GscToken token = tokens[tokenIndex];
        if (token.Kind != GscTokenKind.Identifier ||
            token.Span.Start != callable.ReplacementStart ||
            token.Span.End != caretOffset)
        {
            return null;
        }

        GscTokenKind? previousKind = tokenIndex == 0
            ? null
            : tokens[tokenIndex - 1].Kind;
        return previousKind == GscTokenKind.Dot ? null : callable;
    }

    private static GscCallableCompletionContext FindCallableContext(
        string source,
        int caretOffset)
    {
        int nameStart = caretOffset;
        while (nameStart > 0 && IsIdentifierPart(source[nameStart - 1]))
            nameStart--;

        string name = source[nameStart..caretOffset].ToLowerInvariant();
        string? qualifier = null;
        if (nameStart >= 2 &&
            source.AsSpan(nameStart - 2, 2).SequenceEqual("::"))
        {
            int qualifierEnd = nameStart - 2;
            int qualifierStart = qualifierEnd;
            while (qualifierStart > 0 &&
                   IsScriptPathPart(source[qualifierStart - 1]))
            {
                qualifierStart--;
            }

            if (qualifierStart != qualifierEnd)
                qualifier = source[qualifierStart..qualifierEnd];
        }

        return new GscCallableCompletionContext(
            nameStart,
            name,
            qualifier);
    }

    private static GscFieldCompletionContext? FindFieldContext(
        string source,
        int caretOffset,
        IReadOnlyList<GscToken> tokens)
    {
        if (caretOffset == 0 ||
            (caretOffset < source.Length &&
             IsIdentifierPart(source[caretOffset])))
        {
            return null;
        }

        int prefixStart = caretOffset;
        while (prefixStart > 0 && IsIdentifierPart(source[prefixStart - 1]))
            prefixStart--;
        if (prefixStart != caretOffset &&
            !HasExactIdentifierToken(tokens, prefixStart, caretOffset))
        {
            return null;
        }

        int dotTokenIndex = FindPreviousTokenIndex(tokens, prefixStart);
        if (dotTokenIndex < 0 ||
            tokens[dotTokenIndex].Kind != GscTokenKind.Dot ||
            !IsTrivia(
                source,
                tokens[dotTokenIndex].Span.End,
                prefixStart))
        {
            return null;
        }

        return CreateFieldContext(
            source,
            tokens,
            dotTokenIndex,
            tokens[dotTokenIndex].Span.Start,
            prefixStart,
            source[prefixStart..caretOffset].ToLowerInvariant());
    }

    private static GscFieldCompletionContext? CreateFieldContext(
        string source,
        IReadOnlyList<GscToken> tokens,
        int operatorTokenIndex,
        int receiverEnd,
        int replacementStart,
        string prefix)
    {
        int? receiverStart = FindPrimaryStart(tokens, operatorTokenIndex - 1);
        if (receiverStart is not { } tokenIndex)
            return null;

        int sourceStart = tokens[tokenIndex].Span.Start;
        if (sourceStart >= receiverEnd)
            return null;

        string receiverSource = source[sourceStart..receiverEnd].Trim();
        if (receiverSource.Length == 0)
            return null;

        string expressionKey = GscObservedReceiver.NormalizeExpression(
            receiverSource);
        return new GscFieldCompletionContext(
            replacementStart,
            prefix,
            receiverSource,
            expressionKey,
            GscObservedReceiver.FindTerminalShape(expressionKey),
            GscObservedReceiver.FindBucket(expressionKey),
            FindReceiverProbeOffset(tokens, tokenIndex, operatorTokenIndex));
    }

    private static int? FindPrimaryStart(
        IReadOnlyList<GscToken> tokens,
        int endIndex)
    {
        if (endIndex < 0 || !CanEndPrimary(tokens[endIndex].Kind))
            return null;

        int startIndex;
        switch (tokens[endIndex].Kind)
        {
            case GscTokenKind.CloseBracket:
                startIndex = FindMatchingOpen(
                    tokens,
                    endIndex,
                    GscTokenKind.OpenBracket,
                    GscTokenKind.CloseBracket);
                if (startIndex < 0)
                    return null;
                if (startIndex > 0 && CanEndPrimary(tokens[startIndex - 1].Kind))
                {
                    startIndex = FindPrimaryStart(tokens, startIndex - 1)
                        ?? startIndex;
                }
                break;

            case GscTokenKind.CloseParenthesis:
                startIndex = FindMatchingOpen(
                    tokens,
                    endIndex,
                    GscTokenKind.OpenParenthesis,
                    GscTokenKind.CloseParenthesis);
                if (startIndex < 0)
                    return null;
                startIndex = FindCallExpressionStart(tokens, startIndex);
                break;

            case GscTokenKind.Size:
                startIndex = FindPrimaryStart(tokens, endIndex - 1)
                    ?? endIndex;
                break;

            default:
                startIndex = endIndex;
                break;
        }

        if (tokens[startIndex].Kind == GscTokenKind.Identifier)
        {
            if (startIndex > 0 &&
                tokens[startIndex - 1].Kind == GscTokenKind.Scope)
            {
                startIndex--;
                if (startIndex > 0 &&
                    tokens[startIndex - 1].Kind is
                        GscTokenKind.Identifier or GscTokenKind.Path)
                {
                    startIndex--;
                }
            }
            else if (startIndex > 0 &&
                     tokens[startIndex - 1].Kind == GscTokenKind.Modulo &&
                     (startIndex < 2 ||
                      !CanEndPrimary(tokens[startIndex - 2].Kind)))
            {
                startIndex--;
            }
        }

        if (startIndex >= 2 &&
            tokens[startIndex - 1].Kind == GscTokenKind.Dot &&
            CanEndPrimary(tokens[startIndex - 2].Kind))
        {
            startIndex = FindPrimaryStart(tokens, startIndex - 2)
                ?? startIndex;
        }

        if (startIndex > 0 &&
            tokens[startIndex - 1].Kind == GscTokenKind.Dollar)
        {
            startIndex--;
        }

        return startIndex;
    }

    private static int FindMatchingOpen(
        IReadOnlyList<GscToken> tokens,
        int closeIndex,
        GscTokenKind openKind,
        GscTokenKind closeKind)
    {
        int depth = 0;
        for (int index = closeIndex; index >= 0; index--)
        {
            if (tokens[index].Kind == closeKind)
                depth++;
            else if (tokens[index].Kind == openKind && --depth == 0)
                return index;
        }

        return -1;
    }

    private static int FindCallExpressionStart(
        IReadOnlyList<GscToken> tokens,
        int openParenthesisIndex)
    {
        int callableStart;
        if (openParenthesisIndex > 0 &&
            tokens[openParenthesisIndex - 1].Kind == GscTokenKind.Identifier)
        {
            callableStart = openParenthesisIndex - 1;
            if (callableStart >= 2 &&
                tokens[callableStart - 1].Kind == GscTokenKind.Scope &&
                tokens[callableStart - 2].Kind is
                    GscTokenKind.Identifier or GscTokenKind.Path)
            {
                callableStart -= 2;
            }
        }
        else if (openParenthesisIndex > 0 &&
                 tokens[openParenthesisIndex - 1].Kind ==
                    GscTokenKind.CloseBracket)
        {
            callableStart = FindMatchingOpen(
                tokens,
                openParenthesisIndex - 1,
                GscTokenKind.OpenBracket,
                GscTokenKind.CloseBracket);
            if (callableStart < 0)
                return openParenthesisIndex;
        }
        else
        {
            return openParenthesisIndex;
        }

        int callStart = callableStart;
        if (callStart > 0 && tokens[callStart - 1].Kind is
            GscTokenKind.ThreadKeyword or
            GscTokenKind.ChildThreadKeyword or
            GscTokenKind.CallKeyword)
        {
            callStart--;
        }

        if (callStart > 0 && CanEndPrimary(tokens[callStart - 1].Kind))
            return FindPrimaryStart(tokens, callStart - 1) ?? callStart;
        return callStart;
    }

    private static bool CanEndPrimary(GscTokenKind kind) => kind is
        GscTokenKind.Identifier or
        GscTokenKind.String or
        GscTokenKind.LocalizedString or
        GscTokenKind.Integer or
        GscTokenKind.Float or
        GscTokenKind.CloseParenthesis or
        GscTokenKind.CloseBracket or
        GscTokenKind.UndefinedKeyword or
        GscTokenKind.SelfKeyword or
        GscTokenKind.ThisThreadKeyword or
        GscTokenKind.LevelKeyword or
        GscTokenKind.GameKeyword or
        GscTokenKind.AnimKeyword or
        GscTokenKind.Size or
        GscTokenKind.FalseKeyword or
        GscTokenKind.TrueKeyword or
        GscTokenKind.AnimTreeDirective;

    private static bool IsTrivia(string source, int start, int end)
    {
        int offset = start;
        while (offset < end)
        {
            if (char.IsWhiteSpace(source[offset]))
            {
                offset++;
                continue;
            }

            if (offset + 1 >= end || source[offset] != '/')
                return false;
            if (source[offset + 1] == '/')
            {
                offset += 2;
                while (offset < end && source[offset] is not ('\r' or '\n'))
                    offset++;
                if (offset == end)
                    return false;
                continue;
            }
            if (source[offset + 1] != '*')
                return false;

            offset += 2;
            bool closed = false;
            while (offset + 1 < end)
            {
                if (source[offset] == '*' && source[offset + 1] == '/')
                {
                    offset += 2;
                    closed = true;
                    break;
                }
                offset++;
            }
            if (!closed)
                return false;
        }

        return true;
    }

    private static int FindReceiverProbeOffset(
        IReadOnlyList<GscToken> tokens,
        int receiverStartIndex,
        int operatorTokenIndex)
    {
        if (TryParseBoundReceiver(
                tokens,
                receiverStartIndex,
                operatorTokenIndex,
                out int rootIndex,
                out int nextIndex) &&
            nextIndex == operatorTokenIndex)
        {
            return tokens[rootIndex].Span.Start;
        }

        return tokens[receiverStartIndex].Span.Start;
    }

    private static bool TryParseBoundReceiver(
        IReadOnlyList<GscToken> tokens,
        int startIndex,
        int endIndex,
        out int rootIndex,
        out int nextIndex)
    {
        rootIndex = -1;
        nextIndex = startIndex;
        if (startIndex >= endIndex)
            return false;

        int index;
        if (tokens[startIndex].Kind == GscTokenKind.Identifier)
        {
            rootIndex = startIndex;
            index = startIndex + 1;
        }
        else if (tokens[startIndex].Kind == GscTokenKind.OpenParenthesis)
        {
            int closeIndex = FindMatchingClose(
                tokens,
                startIndex,
                endIndex,
                GscTokenKind.OpenParenthesis,
                GscTokenKind.CloseParenthesis);
            if (closeIndex < 0 ||
                !TryParseBoundReceiver(
                    tokens,
                    startIndex + 1,
                    closeIndex,
                    out rootIndex,
                    out int innerNext) ||
                innerNext != closeIndex)
            {
                return false;
            }
            index = closeIndex + 1;
        }
        else
        {
            return false;
        }

        while (index < endIndex)
        {
            if (tokens[index].Kind == GscTokenKind.Dot &&
                index + 1 < endIndex &&
                tokens[index + 1].Kind == GscTokenKind.Identifier)
            {
                index += 2;
                continue;
            }

            if (tokens[index].Kind != GscTokenKind.OpenBracket)
                break;
            int closeIndex = FindMatchingClose(
                tokens,
                index,
                endIndex,
                GscTokenKind.OpenBracket,
                GscTokenKind.CloseBracket);
            if (closeIndex < 0)
                return false;
            index = closeIndex + 1;
        }

        nextIndex = index;
        return true;
    }

    private static int FindMatchingClose(
        IReadOnlyList<GscToken> tokens,
        int openIndex,
        int endIndex,
        GscTokenKind openKind,
        GscTokenKind closeKind)
    {
        int depth = 0;
        for (int index = openIndex; index < endIndex; index++)
        {
            if (tokens[index].Kind == openKind)
                depth++;
            else if (tokens[index].Kind == closeKind && --depth == 0)
                return index;
        }

        return -1;
    }

    private static int FindPreviousTokenIndex(
        IReadOnlyList<GscToken> tokens,
        int offset)
    {
        for (int index = tokens.Count - 1; index >= 0; index--)
        {
            if (tokens[index].Span.End <= offset)
                return index;
        }

        return -1;
    }

    private static int FindTokenContainingOffset(
        IReadOnlyList<GscToken> tokens,
        int offset)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            GscToken token = tokens[index];
            if (token.Span.Start <= offset && offset < token.Span.End)
                return index;
        }

        return -1;
    }

    private static bool HasExactIdentifierToken(
        IReadOnlyList<GscToken> tokens,
        int start,
        int end) =>
        tokens.Any(token =>
            token.Kind == GscTokenKind.Identifier &&
            token.Span.Start == start &&
            token.Span.End == end);

    private static bool IsIdentifierPart(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    private static bool IsScriptPathPart(char character) =>
        IsIdentifierPart(character) || character is '\\' or '/';
}

internal abstract record GscCompletionContext(
    int ReplacementStart,
    string Prefix);

internal sealed record GscCallableCompletionContext(
    int ReplacementStart,
    string Prefix,
    string? Qualifier)
    : GscCompletionContext(ReplacementStart, Prefix);

internal sealed record GscFieldCompletionContext(
    int ReplacementStart,
    string Prefix,
    string ReceiverSource,
    string ReceiverExpressionKey,
    string? ReceiverTerminalShape,
    string? ReceiverBucket,
    int ReceiverProbeOffset)
    : GscCompletionContext(ReplacementStart, Prefix);
