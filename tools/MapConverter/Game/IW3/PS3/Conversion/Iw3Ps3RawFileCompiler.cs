using System.Text;
using IW4.Game.Assets.RawFile;
using MapConverter.Game.IW3.PC.Conversion;

namespace MapConverter.Game.IW3.PS3.Conversion;

/// <summary>Retains authored COD4 map scripts while retargeting owned asset names.</summary>
internal static class Iw3Ps3RawFileCompiler
{
    internal static RawFileAsset[] Compile(
        IReadOnlyList<RawFileAsset> source,
        string ownedNamePrefix,
        IReadOnlySet<string> fxNames,
        IReadOnlySet<string> modelNames,
        IReadOnlySet<string> materialNames)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(ownedNamePrefix) || !ownedNamePrefix.EndsWith('/'))
            throw new ArgumentException("An owned asset prefix ending in '/' is required.", nameof(ownedNamePrefix));
        var fx = OwnedNames(fxNames, ownedNamePrefix);
        var models = OwnedNames(modelNames, ownedNamePrefix);
        var materials = OwnedNames(materialNames, ownedNamePrefix);
        string mapName = ownedNamePrefix[..^1];
        string mainName = $"maps/mp/{mapName}.gsc";
        var result = new RawFileAsset[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            RawFileAsset raw = source[index] ?? throw new InvalidDataException("Null source rawfile.");
            string name = raw.Name ?? throw new InvalidDataException("Source rawfile has no name.");
            if (name == $"maps/mp/{mapName}_fx.gsc" ||
                name == $"maps/createfx/{mapName}_fx.gsc")
            {
                // This conversion path currently omits custom map FX. Keep the
                // map's entry-point call valid without requesting absent effects.
                byte[] emptyMain = Encoding.ASCII.GetBytes("main()\n{\n}\n");
                result[index] = new RawFileAsset
                {
                    Name = name, Len = emptyMain.Length, Buffer = [.. emptyMain, 0]
                };
                continue;
            }
            if (!name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) &&
                !(name.StartsWith("vision/", StringComparison.OrdinalIgnoreCase) &&
                  name.EndsWith(".vision", StringComparison.OrdinalIgnoreCase)))
            {
                result[index] = raw;
                continue;
            }

            byte[] content = LogicalBytes(raw);
            byte[] converted = name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase)
                ? RewriteScript(content, name == mainName, ownedNamePrefix,
                    fx, models, materials)
                : Iw3PcMapConverter.CompileVisionFile(content);
            result[index] = converted.AsSpan().SequenceEqual(content) ? raw : new RawFileAsset
            {
                Name = name,
                CompressedLen = 0,
                Len = converted.Length,
                Buffer = [.. converted, 0]
            };
        }
        return result;
    }

    private static Dictionary<string, string> OwnedNames(
        IReadOnlySet<string> names, string prefix)
    {
        ArgumentNullException.ThrowIfNull(names);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result.TryAdd(name, name);
        return result;
    }

    private static byte[] LogicalBytes(RawFileAsset raw)
    {
        if (raw.CompressedLen != 0 || raw.Len < 0 || raw.Buffer is not { } buffer ||
            buffer.Length != raw.Len + (long)1 || buffer[^1] != 0)
            throw new InvalidDataException($"Source rawfile '{raw.Name}' has no plain NUL-terminated content.");
        return buffer.AsSpan(0, raw.Len).ToArray();
    }

    private static byte[] RewriteScript(byte[] content, bool main, string ownedNamePrefix,
        IReadOnlyDictionary<string, string> fx,
        IReadOnlyDictionary<string, string> models,
        IReadOnlyDictionary<string, string> materials)
    {
        // Latin1 makes every untouched source byte round-trip exactly. The
        // inserted namespace and IW4 faction names are ASCII.
        string text = Encoding.Latin1.GetString(content);
        List<Token> tokens = Lex(text);
        var replacements = new List<Replacement>();
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Kind != TokenKind.Identifier) continue;
            if (main && Is(text, token, "game") && index + 6 < tokens.Count &&
                Is(text, tokens[index + 1], "[") &&
                Literal(text, tokens[index + 2]) is { } team &&
                Is(text, tokens[index + 3], "]") &&
                Is(text, tokens[index + 4], "=") &&
                Literal(text, tokens[index + 5]) is { } faction &&
                Is(text, tokens[index + 6], ";"))
            {
                if (team == "allies" && faction == "sas")
                    replacements.Add(ReplaceLiteral(tokens[index + 5], "us_army"));
                else if (team == "axis" && faction == "russian")
                    replacements.Add(ReplaceLiteral(tokens[index + 5], "opforce_airborne"));
            }

            if (index + 3 >= tokens.Count || !Is(text, tokens[index + 1], "(") ||
                Literal(text, tokens[index + 2]) is not { } assetName ||
                !(Is(text, tokens[index + 3], ")") || Is(text, tokens[index + 3], ",")))
                continue;
            if (main && Is(text, token, "ambientPlay") &&
                Is(text, tokens[index + 3], ")") && index + 4 < tokens.Count &&
                Is(text, tokens[index + 4], ";"))
            {
                // This path omits source audio aliases and their streamed
                // media. Do not request the COD4 ambience from MW2's stock DB.
                Token end = tokens[index + 4];
                replacements.Add(new(token.Start, end.Start + end.Length - token.Start, ""));
                index += 4;
                continue;
            }
            IReadOnlyDictionary<string, string>? available =
                Is(text, token, "loadfx") ? fx :
                Is(text, token, "precacheModel") || Is(text, token, "setModel") ? models :
                Is(text, token, "precacheShader") || Is(text, token, "setupMiniMap") ? materials : null;
            if (available is not null && available.TryGetValue(
                    ownedNamePrefix + assetName, out string? owned))
                replacements.Add(ReplaceLiteral(tokens[index + 2], owned));
        }
        if (replacements.Count == 0) return content;
        var output = new StringBuilder(text.Length + replacements.Count * 16);
        int copied = 0;
        foreach (Replacement change in replacements.OrderBy(change => change.Start))
        {
            if (change.Start < copied)
                throw new InvalidDataException("Overlapping GSC asset-name rewrites.");
            output.Append(text, copied, change.Start - copied);
            output.Append(change.Value);
            copied = change.Start + change.Length;
        }
        output.Append(text, copied, text.Length - copied);
        return Encoding.Latin1.GetBytes(output.ToString());
    }

    private static Replacement ReplaceLiteral(Token token, string value) =>
        new(token.Start + 1, token.Length - 2, value);

    private static bool Is(string source, Token token, string value) =>
        source.AsSpan(token.Start, token.Length).Equals(value, StringComparison.OrdinalIgnoreCase);

    private static string? Literal(string source, Token token)
    {
        if (token.Kind != TokenKind.String || token.Length < 2) return null;
        ReadOnlySpan<char> value = source.AsSpan(token.Start + 1, token.Length - 2);
        return value.Contains('\\') ? null : value.ToString();
    }

    private static List<Token> Lex(string source)
    {
        var tokens = new List<Token>();
        for (int position = 0; position < source.Length;)
        {
            char current = source[position];
            if (char.IsWhiteSpace(current)) { position++; continue; }
            if (current == '/' && position + 1 < source.Length && source[position + 1] == '/')
            {
                position += 2;
                while (position < source.Length && source[position] is not ('\r' or '\n')) position++;
                continue;
            }
            if (current == '/' && position + 1 < source.Length && source[position + 1] == '*')
            {
                int end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException("GSC block comment is unterminated.");
                position = end + 2;
                continue;
            }
            int start = position;
            if (current is '"' or '\'')
            {
                position++;
                bool closed = false;
                while (position < source.Length)
                {
                    if (source[position] == '\\')
                    {
                        position += 2;
                        continue;
                    }
                    if (source[position++] == current) { closed = true; break; }
                }
                if (!closed)
                    throw new InvalidDataException("GSC string literal is unterminated.");
                tokens.Add(new Token(current == '"' ? TokenKind.String : TokenKind.Other,
                    start, position - start));
                continue;
            }
            if (char.IsLetter(current) || current == '_')
            {
                position++;
                while (position < source.Length &&
                    (char.IsLetterOrDigit(source[position]) || source[position] == '_'))
                    position++;
                tokens.Add(new Token(TokenKind.Identifier, start, position - start));
                continue;
            }
            tokens.Add(new Token(TokenKind.Other, position++, 1));
        }
        return tokens;
    }

    private enum TokenKind { Identifier, String, Other }
    private readonly record struct Token(TokenKind Kind, int Start, int Length);
    private readonly record struct Replacement(int Start, int Length, string Value);
}
