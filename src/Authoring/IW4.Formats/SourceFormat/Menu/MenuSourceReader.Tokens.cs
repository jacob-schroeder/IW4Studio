using System.Globalization;
using System.Text;

namespace IW4.Formats.SourceFormat.Menu;

internal sealed partial class MenuSourceReader
{
    private enum TokenKind { Atom, Quoted, Symbol, End }

    private readonly record struct Token(TokenKind Kind, string Value, int Line);

    private Token Current => Peek(0);

    private Token Peek(int offset) => _tokens.Count == 0
        ? new Token(TokenKind.End, string.Empty, 1)
        : _tokens[Math.Min(_position + offset, _tokens.Count - 1)];

    private bool At(string value) => Current.Kind is TokenKind.Atom or TokenKind.Symbol &&
        string.Equals(Current.Value, value, StringComparison.OrdinalIgnoreCase);

    private bool Take(string value)
    {
        if (!At(value))
            return false;
        _position++;
        return true;
    }

    private void Expect(string value)
    {
        if (!Take(value))
            throw Error($"Expected '{value}'.");
    }

    private string Word()
    {
        if (Current.Kind != TokenKind.Atom)
            throw Error("Expected a menu source keyword.");
        return _tokens[_position++].Value;
    }

    private string Text()
    {
        if (Current.Kind is not (TokenKind.Atom or TokenKind.Quoted))
            throw Error("Expected a text value.");
        return _tokens[_position++].Value;
    }

    private bool AtNumber
    {
        get
        {
            Token token = Peek(At("-") || At("+") ? 1 : 0);
            return token.Kind == TokenKind.Atom &&
                (float.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                 token.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
        }
    }

    private string NumberToken()
    {
        string sign = Take("-") ? "-" : Take("+") ? "+" : string.Empty;
        return sign + Word();
    }

    private int Integer()
    {
        string value = NumberToken();
        if (!TryInteger(value, out int parsed))
            throw Error($"Expected an integer, found '{value}'.");
        return parsed;
    }

    private static bool TryInteger(string value, out int result) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private float Number()
    {
        string value = NumberToken();
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
            !float.IsFinite(parsed))
            throw Error($"Expected a finite number, found '{value}'.");
        return parsed;
    }

    private InvalidDataException Error(string message) => AtSource(_sourcePath, Current.Line, message);

    private static InvalidDataException AtSource(string? path, int line, string message) =>
        new($"{(path is null ? "Menu source" : Path.GetFileName(path))} line {line}: {message}");

    private static IReadOnlyList<Token> Tokenize(string source, string? path)
    {
        var tokens = new List<Token>();
        int position = 0, line = 1;
        while (position < source.Length)
        {
            char value = source[position];
            if (char.IsWhiteSpace(value) || (position == 0 && value == '\uFEFF'))
            {
                if (value == '\n') line++;
                position++;
                continue;
            }

            if (source.AsSpan(position).StartsWith("//", StringComparison.Ordinal))
            {
                while (position < source.Length && source[position] != '\n') position++;
                continue;
            }
            if (source.AsSpan(position).StartsWith("/*", StringComparison.Ordinal))
            {
                int end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw AtSource(path, line, "Unterminated block comment.");
                while (position < end + 2)
                    if (source[position++] == '\n') line++;
                continue;
            }

            int start = position++;
            if (value == '"')
            {
                tokens.Add(new(TokenKind.Quoted, ReadQuoted(source, ref position, path, line), line));
            }
            else if (IsSymbol(value))
            {
                if (position < source.Length && IsPairedOperator(value, source[position]))
                    position++;
                tokens.Add(new(TokenKind.Symbol, source[start..position], line));
            }
            else
            {
                while (position < source.Length && !char.IsWhiteSpace(source[position]) &&
                       source[position] != '"' && (!IsSymbol(source[position]) ||
                           IsExponentSign(source, start, position)))
                    position++;
                tokens.Add(new(TokenKind.Atom, source[start..position], line));
            }
        }
        tokens.Add(new(TokenKind.End, string.Empty, line));
        return tokens;
    }

    private static string ReadQuoted(string source, ref int position, string? path, int line)
    {
        var result = new StringBuilder();
        while (position < source.Length)
        {
            char value = source[position++];
            if (value == '"')
                return result.ToString();
            if (value is '\r' or '\n')
                break;
            if (value == '\\' && position < source.Length)
            {
                char escaped = source[position];
                switch (escaped)
                {
                    case 'r': value = '\r'; position++; break;
                    case 'n': value = '\n'; position++; break;
                    case 't': value = '\t'; position++; break;
                    case 'f': value = '\f'; position++; break;
                    case '"':
                    case '\\': value = escaped; position++; break;
                }
            }
            result.Append(value);
        }
        throw AtSource(path, line, "Unterminated quoted string.");
    }

    private static bool IsSymbol(char value) => value is
        '{' or '}' or '(' or ')' or ',' or ';' or '+' or '-' or '*' or '/' or '%' or
        '!' or '<' or '>' or '=' or '&' or '|' or '~';

    private static bool IsPairedOperator(char first, char second) => (first, second) is
        ('<', '=') or ('>', '=') or ('=', '=') or ('!', '=') or ('&', '&') or ('|', '|') or
        ('<', '<') or ('>', '>');

    private static bool IsExponentSign(string source, int start, int position) =>
        (source[position] is '+' or '-') && (source[position - 1] is 'e' or 'E') &&
        (char.IsAsciiDigit(source[start]) || source[start] == '.') &&
        position + 1 < source.Length && char.IsAsciiDigit(source[position + 1]);
}
