using IW4.Gsc.Syntax;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Caret-oriented text queries that remain useful while the editor buffer is
/// syntactically incomplete. Workspace binding stays in IW4.Gsc.
/// </summary>
internal static class GscEditorTextQueries
{
    internal static GscCompletionPrefix FindCompletionPrefix(
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

        return new GscCompletionPrefix(nameStart, name, qualifier);
    }

    internal static bool IsAutomaticCompletionContext(
        string source,
        int caretOffset)
    {
        if (caretOffset <= 0 || caretOffset > source.Length)
            return false;

        GscCompletionPrefix prefix = FindCompletionPrefix(source, caretOffset);
        if (prefix.Name.Length < 2)
            return false;

        GscSyntaxResult syntax = new GscSyntaxAnalyzer().Analyze(source);
        GscToken[] tokens = syntax.Tokens.ToArray();
        int probeOffset = caretOffset - 1;
        int tokenIndex = Array.FindIndex(
            tokens,
            token =>
                token.Span.Start <= probeOffset &&
                probeOffset < token.Span.End);
        if (tokenIndex < 0)
            return false;

        GscToken token = tokens[tokenIndex];
        if (token.Kind != GscTokenKind.Identifier ||
            token.Span.Start != prefix.ReplacementStart)
        {
            return false;
        }

        GscTokenKind? previousKind = tokenIndex == 0
            ? null
            : tokens[tokenIndex - 1].Kind;
        return previousKind != GscTokenKind.Dot;
    }

    internal static GscCallSite? FindContainingCall(
        string source,
        int caretOffset)
    {
        GscSyntaxResult syntax = new GscSyntaxAnalyzer().Analyze(source);
        GscToken[] tokens = syntax.Tokens
            .Where(token => token.Span.Start < caretOffset)
            .ToArray();
        int openIndex = FindContainingOpenParenthesis(tokens);
        if (openIndex <= 0 ||
            tokens[openIndex - 1].Kind != GscTokenKind.Identifier)
        {
            return null;
        }

        GscToken nameToken = tokens[openIndex - 1];
        string name = source.Substring(
            nameToken.Span.Start,
            nameToken.Span.Length).ToLowerInvariant();
        string? qualifier = ReadQualifier(source, tokens, openIndex);
        int? activeParameter = CountActiveParameter(tokens, openIndex, caretOffset);
        return activeParameter is null
            ? null
            : new GscCallSite(
                name,
                qualifier,
                activeParameter.Value,
                nameToken.Span.Start);
    }

    private static int FindContainingOpenParenthesis(
        IReadOnlyList<GscToken> tokens)
    {
        int depth = 0;
        for (int index = tokens.Count - 1; index >= 0; index--)
        {
            switch (tokens[index].Kind)
            {
                case GscTokenKind.CloseParenthesis:
                    depth++;
                    break;
                case GscTokenKind.OpenParenthesis when depth == 0:
                    return index;
                case GscTokenKind.OpenParenthesis:
                    depth--;
                    break;
            }
        }

        return -1;
    }

    private static string? ReadQualifier(
        string source,
        IReadOnlyList<GscToken> tokens,
        int openIndex)
    {
        if (openIndex < 3 ||
            tokens[openIndex - 2].Kind != GscTokenKind.Scope ||
            tokens[openIndex - 3].Kind is not (
                GscTokenKind.Path or GscTokenKind.Identifier))
        {
            return null;
        }

        GscToken pathToken = tokens[openIndex - 3];
        return source.Substring(pathToken.Span.Start, pathToken.Span.Length);
    }

    private static int? CountActiveParameter(
        IReadOnlyList<GscToken> tokens,
        int openIndex,
        int caretOffset)
    {
        int activeParameter = 0;
        int depth = 0;
        for (int index = openIndex + 1; index < tokens.Count; index++)
        {
            GscToken token = tokens[index];
            if (token.Span.Start >= caretOffset)
                break;

            switch (token.Kind)
            {
                case GscTokenKind.OpenParenthesis:
                case GscTokenKind.OpenBracket:
                    depth++;
                    break;
                case GscTokenKind.CloseParenthesis when depth == 0:
                    return null;
                case GscTokenKind.CloseParenthesis:
                case GscTokenKind.CloseBracket:
                    depth--;
                    break;
                case GscTokenKind.Comma when depth == 0:
                    activeParameter++;
                    break;
            }
        }

        return activeParameter;
    }

    private static bool IsIdentifierPart(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    private static bool IsScriptPathPart(char character) =>
        IsIdentifierPart(character) || character is '\\' or '/';
}

internal sealed record GscCompletionPrefix(
    int ReplacementStart,
    string Name,
    string? Qualifier);

internal sealed record GscCallSite(
    string Name,
    string? Qualifier,
    int ActiveParameter,
    int NameStart);
