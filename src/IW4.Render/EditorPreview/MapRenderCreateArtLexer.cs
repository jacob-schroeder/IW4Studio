namespace IW4.Render.EditorPreview;

internal enum MapRenderCreateArtLexicalFailure
{
    None,
    UnterminatedQuotedString,
    UnterminatedBlockComment,
    UnterminatedParentheses
}

/// <summary>
/// Shared lexical mechanics for the small executable createart command
/// subsets interpreted by the editor preview.
/// </summary>
internal static class MapRenderCreateArtLexer
{
    internal static bool TryMaskComments(
        string text,
        out string masked,
        out MapRenderCreateArtLexicalFailure failure)
    {
        char[] result = text.ToCharArray();
        failure = MapRenderCreateArtLexicalFailure.None;
        for (int index = 0; index < result.Length;)
        {
            if (result[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(text, index, out int next))
                {
                    masked = string.Empty;
                    failure = MapRenderCreateArtLexicalFailure
                        .UnterminatedQuotedString;
                    return false;
                }
                index = next;
                continue;
            }
            if (result[index] != '/' || index + 1 >= result.Length)
            {
                index++;
                continue;
            }
            if (result[index + 1] == '/')
            {
                result[index++] = ' ';
                result[index++] = ' ';
                while (index < result.Length &&
                       result[index] is not ('\r' or '\n'))
                {
                    result[index++] = ' ';
                }
                continue;
            }
            if (result[index + 1] == '*')
            {
                result[index++] = ' ';
                result[index++] = ' ';
                bool closed = false;
                while (index < result.Length)
                {
                    if (index + 1 < result.Length &&
                        result[index] == '*' && result[index + 1] == '/')
                    {
                        result[index++] = ' ';
                        result[index++] = ' ';
                        closed = true;
                        break;
                    }
                    if (result[index] is not ('\r' or '\n'))
                        result[index] = ' ';
                    index++;
                }
                if (!closed)
                {
                    masked = string.Empty;
                    failure = MapRenderCreateArtLexicalFailure
                        .UnterminatedBlockComment;
                    return false;
                }
                continue;
            }
            index++;
        }

        masked = new string(result);
        return true;
    }

    internal static bool TryReadBalancedParentheses(
        string text,
        int open,
        out int close,
        out MapRenderCreateArtLexicalFailure failure)
    {
        close = -1;
        failure = MapRenderCreateArtLexicalFailure.None;
        int depth = 0;
        for (int index = open; index < text.Length; index++)
        {
            if (text[index] is '\'' or '"')
            {
                if (!TrySkipQuoted(text, index, out int next))
                {
                    failure = MapRenderCreateArtLexicalFailure
                        .UnterminatedQuotedString;
                    return false;
                }
                index = next - 1;
                continue;
            }
            if (text[index] == '(')
                depth++;
            else if (text[index] == ')' && --depth == 0)
            {
                close = index;
                return true;
            }
        }

        failure = MapRenderCreateArtLexicalFailure.UnterminatedParentheses;
        return false;
    }

    internal static bool TrySkipQuoted(string text, int quote, out int next)
    {
        char delimiter = text[quote];
        for (int index = quote + 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }
            if (text[index] == delimiter)
            {
                next = index + 1;
                return true;
            }
        }

        next = text.Length;
        return false;
    }

    internal static bool IsIdentifierStart(char character) =>
        char.IsAsciiLetter(character) || character == '_';

    internal static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || char.IsAsciiDigit(character);
}
