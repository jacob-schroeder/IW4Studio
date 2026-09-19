using IW4.Gsc.Syntax;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Caret-oriented text queries that remain useful while the editor buffer is
/// syntactically incomplete. Workspace binding stays in IW4.Gsc.
/// </summary>
internal static class GscEditorTextQueries
{
    internal static GscCallSite? FindContainingCall(
        string source,
        int caretOffset,
        CancellationToken cancellationToken = default)
    {
        GscSyntaxResult syntax = new GscSyntaxAnalyzer().Analyze(
            source,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
}

internal sealed record GscCallSite(
    string Name,
    string? Qualifier,
    int ActiveParameter,
    int NameStart);
