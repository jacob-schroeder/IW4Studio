using System.Text;
using IW4.Game.Assets.RawFile;
using MapConverter.Game.IW3.PC.Conversion;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Conversion;

/// <summary>Retains authored COD4 map scripts while retargeting owned asset names.</summary>
internal static class Iw3Ps3RawFileCompiler
{
    internal static IReadOnlyList<string> ReadEffectNames(IEnumerable<RawFileAsset> scripts)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RawFileAsset script in scripts.Where(raw => raw.Name?.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) == true))
        {
            string text = Encoding.Latin1.GetString(LogicalBytes(script));
            List<Token> tokens = Lex(text);
            for (int index = 0; index < tokens.Count; index++)
            {
                if (tokens[index].Kind == TokenKind.Identifier && Is(text, tokens[index], "loadfx") &&
                    index + 3 < tokens.Count && Is(text, tokens[index + 1], "("))
                {
                    string name = Literal(text, tokens[index + 2]) ??
                        throw new NotSupportedException($"Map script '{script.Name}' requires a literal loadfx asset name.");
                    names.Add(name);
                }
            }
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> ReadSoundNames(IEnumerable<RawFileAsset> scripts, bool ambientOnly = false)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RawFileAsset script in scripts.Where(raw => raw.Name?.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) == true))
        {
            string text = Encoding.Latin1.GetString(LogicalBytes(script));
            foreach ((Token _, string alias) in ReadSoundReferences(text, Lex(text), script.Name, ambientOnly))
                names.Add(alias);
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> ReadSharedPrefabSoundNames(RawFileAsset script,
        IReadOnlyList<IReadOnlyDictionary<string, string>> entities)
    {
        string text = Encoding.Latin1.GetString(LogicalBytes(script));
        List<Token> tokens = Lex(text);
        var declarations = new List<(string Target, string Alias)>();
        for (int index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Kind != TokenKind.Identifier || !Is(text, tokens[index], "global_FX") ||
                !Is(text, tokens[index + 1], "(")) continue;
            var arguments = new List<(int Start, int Count)>();
            int start = index + 2, depth = 0, cursor = start;
            for (; cursor < tokens.Count; cursor++)
            {
                Token token = tokens[cursor];
                if (Is(text, token, ")") && depth == 0)
                {
                    arguments.Add((start, cursor - start));
                    break;
                }
                if (Is(text, token, ",") && depth == 0)
                {
                    arguments.Add((start, cursor - start));
                    start = cursor + 1;
                }
                else if (Is(text, token, "(") || Is(text, token, "[") || Is(text, token, "{")) depth++;
                else if (Is(text, token, ")") || Is(text, token, "]") || Is(text, token, "}")) depth--;
            }
            int line = text.AsSpan(0, tokens[index].Start).Count('\n') + 1;
            if (cursor == tokens.Count)
                throw new InvalidDataException($"Shared script '{script.Name}' line {line} has an unterminated global_FX call.");
            // The function declaration has variable parameters; only calls declare dependencies.
            if (cursor + 1 < tokens.Count && Is(text, tokens[cursor + 1], "{")) continue;
            if (arguments.Count == 4) continue;
            if (arguments.Count != 5 || arguments[0].Count != 1 || arguments[4].Count != 1 ||
                Literal(text, tokens[arguments[0].Start]) is not { Length: > 0 } target ||
                Literal(text, tokens[arguments[4].Start]) is not { Length: > 0 } alias)
                throw new NotSupportedException($"Shared script '{script.Name}' line {line} requires literal global_FX target and sound alias arguments.");
            declarations.Add((target, alias));
            index = cursor;
        }
        return SoundRules.SharedPrefabAliases(entities, declarations);
    }

    private static IEnumerable<(Token Literal, string Name)> ReadSoundReferences(
        string text, List<Token> tokens, string? scriptName, bool ambientOnly = false)
    {
        int braceDepth = 0;
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (Is(text, token, "{")) { braceDepth++; continue; }
            if (Is(text, token, "}")) { braceDepth--; continue; }

            bool field = !ambientOnly && index > 0 && index + 2 < tokens.Count &&
                Is(text, tokens[index - 1], "[") && Literal(text, token) is "soundalias" or "loopsound" &&
                Is(text, tokens[index + 1], "]") && Is(text, tokens[index + 2], "=") &&
                !(index + 3 < tokens.Count && Is(text, tokens[index + 3], "="));
            int aliasIndex = index + 3;
            if (!field)
            {
                // Function declarations occur outside script bodies. The :: form names a script helper.
                if (braceDepth == 0 || token.Kind != TokenKind.Identifier || index + 1 >= tokens.Count ||
                    !Is(text, tokens[index + 1], "(") ||
                    (index >= 2 && Is(text, tokens[index - 2], ":") && Is(text, tokens[index - 1], ":")))
                    continue;
                ScriptSoundAliasArgument argument = SoundRules.ScriptAliasArgument(
                    text.Substring(token.Start, token.Length), ambientOnly);
                if (argument == ScriptSoundAliasArgument.None) continue;
                aliasIndex = index + 2;
                if (argument == ScriptSoundAliasArgument.Optional && aliasIndex < tokens.Count &&
                    Is(text, tokens[aliasIndex], ")")) continue;
            }

            if (aliasIndex + 1 < tokens.Count && Literal(text, tokens[aliasIndex]) is { } alias &&
                (field ? Is(text, tokens[aliasIndex + 1], ";") :
                    Is(text, tokens[aliasIndex + 1], ")") || Is(text, tokens[aliasIndex + 1], ",")))
            {
                if (!field || alias is not ("" or "nil")) yield return (tokens[aliasIndex], alias);
                continue;
            }

            int line = 1;
            for (int position = 0; position < token.Start; position++)
                if (text[position] == '\n' || (text[position] == '\r' && text[position + 1] != '\n')) line++;
            string reference = text.Substring(token.Start, token.Length);
            throw new NotSupportedException($"Map script '{scriptName}' line {line} requires a literal sound alias for {reference}.");
        }
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
                ? RewriteScript(scriptContent, name, name == mainName, ownedNamePrefix,
                    fx, models, materials, sounds, splitStaticModelNames, hasVehicleRecipes, factions)
                : Iw3PcMapConverter.CompileVisionFile(content);
            result[index] = WithContent(raw, content, converted);
        }
        return result;
    }

    internal static RawFileAsset[] CompilePcGameplay(
        IReadOnlyList<RawFileAsset> rawFiles, string mapName, FactionProfile factions,
        IReadOnlySet<string> soundNames, bool allowMissingSounds)
    {
        string prefix = mapName + "/";
        var ownedSounds = OwnedNames(soundNames, prefix);
        bool hasVehicleRecipes = rawFiles.Any(raw => raw.Name == $"maps/mp/{mapName}_vehicles.gsc");
        return rawFiles.Select(raw =>
        {
            if (raw.Name is not { } name || !name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase))
                return raw;
            byte[] content = LogicalBytes(raw);
            string text = Encoding.Latin1.GetString(content);
            if (name == $"maps/createfx/{mapName}_fx.gsc")
                text = ScriptRules.CreateFxHelpers(text);
            List<Token> tokens = Lex(text);
            bool main = name == $"maps/mp/{mapName}.gsc";
            var changes = GameplayReplacements(text, tokens, name, main,
                prefix, ownedSounds, factions, allowMissingSounds, soundNames);
            if (main && hasVehicleRecipes)
                changes.Add(VehicleStartup(text, tokens, prefix));
            return WithContent(raw, content, ApplyReplacements(text, changes));
        }).ToArray();
    }

    private static RawFileAsset WithContent(RawFileAsset raw, byte[] original, byte[] converted) =>
        converted.AsSpan().SequenceEqual(original) ? raw : new RawFileAsset
        {
            Name = raw.Name, CompressedLen = 0, Len = converted.Length, Buffer = [.. converted, 0]
        };

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

    private static byte[] RewriteScript(byte[] content, string scriptName, bool main, string ownedNamePrefix,
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
        var replacements = GameplayReplacements(text, tokens, scriptName, main, ownedNamePrefix, sounds, factions);
        if (main && hasVehicleRecipes)
            replacements.Add(VehicleStartup(text, tokens, ownedNamePrefix));
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Kind != TokenKind.Identifier) continue;
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
                Is(text, token, "precacheModel") || Is(text, token, "setModel") ? models :
                Is(text, token, "precacheShader") || Is(text, token, "setupMiniMap") ? materials : null;
            if (available is not null && available.TryGetValue(
                    ownedNamePrefix + assetName, out string? owned))
                replacements.Add(ReplaceLiteral(tokens[index + 2], owned));
            else if (Is(text, token, "loadfx"))
                throw new InvalidDataException($"Source map asset '{assetName}' has no converted provider.");
        }
        return ApplyReplacements(text, replacements);
    }

    private static Replacement VehicleStartup(string text, List<Token> tokens, string prefix)
    {
        string[] startup = ["maps", "\\", "mp", "\\", "_load", ":", ":", "main", "(", ")", ";"];
        Token[] calls = tokens.Where((token, index) => index + startup.Length <= tokens.Count &&
            startup.Select((value, offset) => Is(text, tokens[index + offset], value)).All(match => match))
            .ToArray();
        if (calls.Length != 1)
            throw new InvalidDataException("Vehicle recipes require exactly one native map startup call.");
        return new Replacement(calls[0].Start, 0, ScriptRules.VehicleStartupCall(prefix));
    }

    private static List<Replacement> GameplayReplacements(string text, List<Token> tokens, string scriptName,
        bool main, string ownedNamePrefix, IReadOnlyDictionary<string, string> sounds, FactionProfile factions,
        bool allowMissingSounds = false, IReadOnlySet<string>? nativeSounds = null)
    {
        var replacements = new List<Replacement>();
        foreach ((Token literal, string alias) in ReadSoundReferences(text, tokens, scriptName))
        {
            if (!sounds.TryGetValue(ownedNamePrefix + alias, out string? ownedSound))
            {
                if (allowMissingSounds || nativeSounds?.Contains(alias) == true) continue;
                throw new InvalidDataException($"Source map sound alias '{alias}' has no converted provider.");
            }
            replacements.Add(ReplaceLiteral(literal, ownedSound));
        }
        if (!main) return replacements;
        for (int index = 0; index + 6 < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (Is(text, token, "game") &&
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
        }
        return replacements;
    }

    private static byte[] ApplyReplacements(string text, List<Replacement> replacements)
    {
        if (replacements.Count == 0) return Encoding.Latin1.GetBytes(text);
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
