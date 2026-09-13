using System.Text;

namespace Iw4Radiant.MapSource.Parsing;

internal static class MapTokenizer
{
    public static List<MapToken> Tokenize(string source)
    {
        List<MapToken> tokens = [];
        int position = 0, line = 1, logicalLine = 1;
        while (position < source.Length)
        {
            char character = source[position];
            if (char.IsWhiteSpace(character) || character == '\uFEFF')
            {
                if (character == '\n' || character == '\r' && (position + 1 == source.Length || source[position + 1] != '\n'))
                {
                    line++;
                    logicalLine++;
                }
                position++;
                continue;
            }
            if (character == '/' && position + 1 < source.Length && source[position + 1] == '/')
            {
                position = LineEnd(source, position);
                continue;
            }
            int start = position, tokenLine = line;
            if (character == '/' && position + 1 < source.Length && source[position + 1] == '*')
            {
                position += 2;
                while (position + 1 < source.Length && !(source[position] == '*' && source[position + 1] == '/'))
                {
                    if (source[position] == '\n' || source[position] == '\r' && source[position + 1] != '\n') line++;
                    position++;
                }
                if (position + 1 >= source.Length)
                    throw new FormatException($"Line {tokenLine}: Unterminated block comment.");
                position += 2;
                continue;
            }
            if (character == '"')
            {
                position++;
                var value = new StringBuilder();
                while (position < source.Length && source[position] != '"')
                {
                    character = source[position++];
                    if (character is '\r' or '\n' or '\0')
                        throw new FormatException($"Line {tokenLine}: Unterminated quoted string.");
                    if (character == '\\' && position < source.Length && source[position] is '\\' or '"')
                        character = source[position++];
                    value.Append(character);
                }
                if (position == source.Length)
                    throw new FormatException($"Line {tokenLine}: Unterminated quoted string.");
                tokens.Add(new MapToken(value.ToString(), start, ++position, tokenLine, logicalLine, true));
                continue;
            }
            if ("{}();".Contains(character))
                position++;
            else
                while (position < source.Length && !char.IsWhiteSpace(source[position]) && !"{}();\"".Contains(source[position]))
                {
                    if (source[position] == '\0')
                        throw new FormatException($"Line {line}: Unexpected null character.");
                    position++;
                }
            tokens.Add(new MapToken(source[start..position], start, position, tokenLine, logicalLine));
        }
        return tokens;
    }

    private static int LineEnd(string source, int start)
    {
        int end = start;
        while (end < source.Length && source[end] is not '\r' and not '\n')
            end++;
        return end;
    }
}
