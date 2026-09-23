using System.Text;

namespace IW4.Gsc.Workspace;

/// <summary>
/// Advisory receiver identity recovered from a dynamic field access.
/// It is intentionally weaker than a symbol or runtime type.
/// </summary>
public sealed record GscObservedReceiver
{
    internal GscObservedReceiver(
        string sourceText,
        GscSymbolId? binding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        SourceText = sourceText;
        ExpressionKey = NormalizeExpression(sourceText);
        TerminalShape = FindTerminalShape(ExpressionKey);
        Bucket = FindBucket(ExpressionKey);
        Binding = binding;
    }

    public string SourceText { get; }

    public string ExpressionKey { get; }

    public string? TerminalShape { get; }

    /// <summary>
    /// The coarse <c>self</c>, <c>level</c>, <c>game</c>, <c>anim</c>, or
    /// <c>thisthread</c> root when one is lexically visible.
    /// </summary>
    public string? Bucket { get; }

    /// <summary>
    /// Exact local or parameter identity when the receiver root was bound.
    /// </summary>
    public GscSymbolId? Binding { get; }

    public static string NormalizeExpression(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var result = new StringBuilder(sourceText.Length);
        bool inString = false;
        bool escaped = false;
        bool skippedWhitespace = false;
        foreach (char character in sourceText)
        {
            if (inString)
            {
                result.Append(character);
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                skippedWhitespace = true;
                continue;
            }
            if (skippedWhitespace &&
                result.Length > 0 &&
                IsIdentifierPart(result[^1]) &&
                IsIdentifierPart(character))
            {
                result.Append(' ');
            }
            skippedWhitespace = false;
            if (character == '"')
            {
                inString = true;
                result.Append(character);
                continue;
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    public static string? FindTerminalShape(string expressionKey)
    {
        ArgumentNullException.ThrowIfNull(expressionKey);
        if (expressionKey.Length == 0)
            return null;

        if (!IsIdentifierPart(expressionKey[^1]))
            return null;

        bool inString = false;
        bool escaped = false;
        int lastDot = -1;
        for (int index = 0; index < expressionKey.Length; index++)
        {
            char character = expressionKey[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (character == '"')
                inString = true;
            else if (character == '.')
                lastDot = index;
        }

        if (lastDot < 0)
            return null;
        for (int index = lastDot + 1; index < expressionKey.Length; index++)
        {
            if (!IsIdentifierPart(expressionKey[index]))
                return null;
        }

        return expressionKey[lastDot..];
    }

    public static string? FindBucket(string expressionKey)
    {
        ArgumentNullException.ThrowIfNull(expressionKey);
        int end = 0;
        while (end < expressionKey.Length && IsIdentifierPart(expressionKey[end]))
            end++;

        string root = expressionKey[..end];
        return root is "self" or "level" or "game" or "anim" or "thisthread"
            ? root
            : null;
    }

    private static bool IsIdentifierPart(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}

/// <summary>
/// A field observed in valid source. These facts are completion hints only and
/// never represent statically resolved members.
/// </summary>
public sealed record GscObservedField(
    GscSourceLocation Location,
    string Name,
    string SourceName,
    GscObservedReceiver Receiver);
