using System.Text;
using IW4.Game.Assets.RawFile;
using MapConverter.Game.IW3.PC.Conversion;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Conversion;

/// <summary>Retains authored COD4 map scripts while retargeting owned asset names.</summary>
internal static class Iw3Ps3RawFileCompiler
{
    internal static IReadOnlyList<string> ReadEffectNames(IEnumerable<RawFileAsset> scripts) =>
        ReadAssetNames(scripts, "loadfx", includeSoundFields: false);

    internal static IReadOnlyList<string> ReadSoundNames(IEnumerable<RawFileAsset> scripts, bool ambientOnly = false) =>
        ReadAssetNames(scripts, "ambientPlay", includeSoundFields: !ambientOnly);

    private static IReadOnlyList<string> ReadAssetNames(IEnumerable<RawFileAsset> scripts,
        string function, bool includeSoundFields)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RawFileAsset script in scripts.Where(raw => raw.Name?.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) == true))
        {
            string text = Encoding.Latin1.GetString(LogicalBytes(script));
            List<Token> tokens = Lex(text);
            for (int index = 0; index < tokens.Count; index++)
            {
                if (tokens[index].Kind == TokenKind.Identifier && Is(text, tokens[index], function) &&
                    index + 3 < tokens.Count && Is(text, tokens[index + 1], "("))
                {
                    string name = Literal(text, tokens[index + 2]) ??
                        throw new NotSupportedException($"Map script '{script.Name}' requires a literal {function} asset name.");
                    names.Add(name);
                }
                else if (includeSoundFields && index + 3 < tokens.Count &&
                    Literal(text, tokens[index]) is "soundalias" or "loopsound" &&
                    Is(text, tokens[index + 1], "]") && Is(text, tokens[index + 2], "=") &&
                    Literal(text, tokens[index + 3]) is { Length: > 0 } alias && alias != "nil")
                    names.Add(alias);
            }
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> ReadMinimapMaterialNames(RawFileAsset main)
    {
        string text = Encoding.Latin1.GetString(LogicalBytes(main));
        List<Token> tokens = Lex(text);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Kind != TokenKind.Identifier || !Is(text, tokens[index], "setupMiniMap") ||
                !Is(text, tokens[index + 1], "(")) continue;
            if (index + 3 >= tokens.Count || Literal(text, tokens[index + 2]) is not { } name ||
                !Is(text, tokens[index + 3], ")"))
                throw new NotSupportedException($"Map script '{main.Name}' requires a literal minimap material name.");
            names.Add(name);
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    internal static RawFileAsset[] Compile(
        IReadOnlyList<RawFileAsset> source,
        string ownedNamePrefix,
        IReadOnlySet<string> fxNames,
        IReadOnlySet<string> modelNames,
        IReadOnlySet<string> materialNames,
        IReadOnlySet<string> soundNames,
        IReadOnlySet<string> splitStaticModelNames,
        FactionProfile factions)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(ownedNamePrefix) || !ownedNamePrefix.EndsWith('/'))
            throw new ArgumentException("An owned asset prefix ending in '/' is required.", nameof(ownedNamePrefix));
        var fx = OwnedNames(fxNames, ownedNamePrefix);
        var models = OwnedNames(modelNames, ownedNamePrefix);
        var materials = OwnedNames(materialNames, ownedNamePrefix);
        var sounds = OwnedNames(soundNames, ownedNamePrefix);
        string mapName = ownedNamePrefix[..^1];
        string mainName = $"maps/mp/{mapName}.gsc";
        bool hasVehicleRecipes = source.Any(raw => raw.Name == $"maps/mp/{mapName}_vehicles.gsc");
        var result = new RawFileAsset[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            RawFileAsset raw = source[index] ?? throw new InvalidDataException("Null source rawfile.");
            string name = raw.Name ?? throw new InvalidDataException("Source rawfile has no name.");
            if (!name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) &&
                !(name.StartsWith("vision/", StringComparison.OrdinalIgnoreCase) &&
                  name.EndsWith(".vision", StringComparison.OrdinalIgnoreCase)))
            {
                result[index] = raw;
                continue;
            }

            byte[] content = LogicalBytes(raw);
            byte[] scriptContent = name == $"maps/createfx/{mapName}_fx.gsc"
                ? Encoding.Latin1.GetBytes(ScriptRules.CreateFxHelpers(Encoding.Latin1.GetString(content))) : content;
            byte[] converted = name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase)
                ? RewriteScript(scriptContent, name == mainName, ownedNamePrefix,
                    fx, models, materials, sounds, splitStaticModelNames, hasVehicleRecipes, factions)
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
        IReadOnlyDictionary<string, string> materials,
        IReadOnlyDictionary<string, string> sounds,
        IReadOnlySet<string> splitStaticModelNames, bool hasVehicleRecipes, FactionProfile factions)
    {
        // Latin1 makes every untouched source byte round-trip exactly. The
        // inserted namespace and IW4 faction names are ASCII.
        string text = Encoding.Latin1.GetString(content);
        List<Token> tokens = Lex(text);
        var replacements = new List<Replacement>();
        int vehicleStartupCalls = 0;
        string[] nativeStartup = ["maps", "\\", "mp", "\\", "_load", ":", ":", "main", "(", ")", ";"];
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (main && hasVehicleRecipes && index + nativeStartup.Length <= tokens.Count &&
                nativeStartup.Select((value, offset) => Is(text, tokens[index + offset], value)).All(matches => matches))
            {
                replacements.Add(new Replacement(token.Start, 0, ScriptRules.VehicleStartupCall(ownedNamePrefix)));
                vehicleStartupCalls++;
            }
            if (Literal(text, token) is "soundalias" or "loopsound" && index + 3 < tokens.Count &&
                Is(text, tokens[index + 1], "]") && Is(text, tokens[index + 2], "=") &&
                Literal(text, tokens[index + 3]) is { Length: > 0 } alias && alias != "nil")
            {
                if (!sounds.TryGetValue(ownedNamePrefix + alias, out string? ownedSound))
                    throw new InvalidDataException($"Source map sound alias '{alias}' has no converted provider.");
                replacements.Add(ReplaceLiteral(tokens[index + 3], ownedSound));
            }
            if (token.Kind != TokenKind.Identifier) continue;
            if (main && Is(text, token, "game") && index + 6 < tokens.Count &&
                Is(text, tokens[index + 1], "[") &&
                Literal(text, tokens[index + 2]) is { } team &&
                Is(text, tokens[index + 3], "]") &&
                Is(text, tokens[index + 4], "=") &&
                Literal(text, tokens[index + 5]) is { } faction &&
                Is(text, tokens[index + 6], ";"))
            {
                string selected = FactionRules.TeamValue(factions, team, faction);
                if (selected != faction)
                    replacements.Add(ReplaceLiteral(tokens[index + 5], selected));
            }

            if (index + 3 >= tokens.Count || !Is(text, tokens[index + 1], "(") ||
                Literal(text, tokens[index + 2]) is not { } assetName ||
                !(Is(text, tokens[index + 3], ")") || Is(text, tokens[index + 3], ",")))
                continue;
            if ((Is(text, token, "precacheModel") || Is(text, token, "setModel")) &&
                (splitStaticModelNames.Contains(assetName) ||
                 splitStaticModelNames.Contains(ownedNamePrefix + assetName)))
                throw new NotSupportedException(
                    $"Script call '{text.Substring(token.Start, token.Length)}' references split static model '{assetName}'; " +
                    "scripted model instances require their own part expansion.");
            IReadOnlyDictionary<string, string>? available =
                Is(text, token, "loadfx") ? fx :
                Is(text, token, "ambientPlay") ? sounds :
                Is(text, token, "precacheModel") || Is(text, token, "setModel") ? models :
                Is(text, token, "precacheShader") || Is(text, token, "setupMiniMap") ? materials : null;
            if (available is not null && available.TryGetValue(
                    ownedNamePrefix + assetName, out string? owned))
                replacements.Add(ReplaceLiteral(tokens[index + 2], owned));
            else if (Is(text, token, "loadfx") || Is(text, token, "ambientPlay"))
                throw new InvalidDataException($"Source map asset '{assetName}' has no converted provider.");
        }
        if (main && hasVehicleRecipes && vehicleStartupCalls != 1)
            throw new InvalidDataException("Vehicle recipes require exactly one native map startup call.");
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
