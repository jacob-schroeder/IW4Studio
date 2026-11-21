using System.Runtime.CompilerServices;
using System.Text;
using IW4.Formats.RawFile;
using IW4.Game.Assets.RawFile;
using IW4.Game.Zone;
using IW4.Gsc.Syntax;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

/// <summary>Exact animation uses in loaded script and animtree RawFiles.</summary>
internal static class XAnimScriptReferences
{
    private static readonly ConditionalWeakTable<FastFileWorkspace, Cache> Caches = new();

    internal static IEnumerable<(string Source, string? Model)> Find(
        FastFileWorkspace workspace,
        string animationName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationName);
        Cache cache = Caches.GetValue(workspace, _ => new Cache());
        lock (cache)
        {
            Refresh(workspace, cache);
            cache.References ??= Index(cache.Sources);
            return cache.References.TryGetValue(animationName, out var references)
                ? references
                : [];
        }
    }

    // Shared with the map/destructible lookup: source payloads are decoded only
    // once for each active asset-pool revision, even when many XAnims are opened.
    internal static IReadOnlyList<(string Name, string Text)> GetSources(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Cache cache = Caches.GetValue(workspace, _ => new Cache());
        lock (cache)
        {
            Refresh(workspace, cache);
            return cache.Sources;
        }
    }

    private static void Refresh(FastFileWorkspace workspace, Cache cache)
    {
        XAssetPool pool = workspace.LoadedZone.Context.AssetPool;
        long revision = pool.Revision;
        if (cache.Revision == revision)
            return;

        var sources = new List<(string Name, string Text)>();
        foreach (XAssetSlot slot in pool.Slots)
        {
            if (slot.AssetType != XAssetType.RawFile || !IsRelevant(slot.Name) ||
                slot.ActiveProvider.IsReferencePlaceholder ||
                slot.ActiveProvider.Asset is not RawFileAsset rawFile)
                continue;

            try
            {
                byte[] bytes = RawFileContentCodec.DecodeStrictSerializedContent(slot.Name, rawFile);
                RawFileTextEncoding? encoding = RawFileContentClassifier.Classify(slot.Name, bytes).TextEncoding;
                if (encoding is { } textEncoding)
                    sources.Add((slot.Name, RawFileContentClassifier.DecodeText(bytes, textEncoding)));
            }
            catch (InvalidDataException)
            {
                // A damaged RawFile cannot establish a reference.
            }
        }

        if (pool.Revision != revision)
            throw new InvalidOperationException("The loaded asset pool changed during XAnim reference capture.");

        cache.Sources = sources.AsReadOnly();
        cache.References = null;
        cache.Revision = revision;
    }

    private static bool IsRelevant(string name) =>
        name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".csc", StringComparison.OrdinalIgnoreCase) ||
        name.Replace('\\', '/').Equals("mp/playeranim.script", StringComparison.OrdinalIgnoreCase) ||
        (name.Replace('\\', '/').StartsWith("animtrees/", StringComparison.OrdinalIgnoreCase) &&
         name.EndsWith(".atr", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, IReadOnlyList<(string Source, string? Model)>> Index(
        IReadOnlyList<(string Name, string Text)> sources)
    {
        var found = new Dictionary<string, HashSet<(string Source, string? Model)>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string text) in sources)
        {
            if (name.EndsWith(".atr", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string animation in AtrPrimitives(text))
                    Add(animation, name, null);
            }
            else if (name.EndsWith(".script", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string animation in PlayerAnimCommands(text))
                    Add(animation, name, null);
            }
            else
            {
                GscSyntaxResult syntax = new GscSyntaxAnalyzer().Analyze(text);
                if (!syntax.IsAccepted)
                    continue;
                IReadOnlyList<GscToken> tokens = syntax.Tokens;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (TryAnimationToken(text, tokens, i, out string? animation))
                        Add(animation, name, FindImmediateReceiverModel(text, tokens, i));
                    if (TryAnimationCall(text, tokens, i, out animation))
                        Add(animation, name, null);
                }
            }
        }

        return found.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<(string Source, string? Model)>)pair.Value
                .OrderBy(value => value.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Model, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        void Add(string animation, string source, string? model)
        {
            if (!found.TryGetValue(animation, out var references))
                found.Add(animation, references = []);
            references.Add((source, model));
        }
    }

    private static bool TryAnimationToken(
        string text, IReadOnlyList<GscToken> tokens, int index, out string animation)
    {
        animation = string.Empty;
        if (index + 1 >= tokens.Count || tokens[index].Kind != GscTokenKind.Modulo ||
            tokens[index + 1].Kind != GscTokenKind.Identifier ||
            tokens[index].Span.End != tokens[index + 1].Span.Start ||
            (index > 0 && EndsExpression(tokens[index - 1].Kind)))
            return false;

        animation = Slice(text, tokens[index + 1]);
        return IsAssetName(animation);
    }

    private static bool EndsExpression(GscTokenKind kind) => kind is
        GscTokenKind.Identifier or GscTokenKind.String or GscTokenKind.LocalizedString or
        GscTokenKind.Integer or GscTokenKind.Float or GscTokenKind.CloseParenthesis or
        GscTokenKind.CloseBracket or GscTokenKind.TrueKeyword or GscTokenKind.FalseKeyword or
        GscTokenKind.SelfKeyword or GscTokenKind.LevelKeyword or GscTokenKind.GameKeyword;

    private static bool TryAnimationCall(
        string text, IReadOnlyList<GscToken> tokens, int index, out string animation)
    {
        animation = string.Empty;
        if (index + 3 >= tokens.Count || tokens[index].Kind != GscTokenKind.Identifier ||
            tokens[index + 1].Kind != GscTokenKind.OpenParenthesis ||
            tokens[index + 2].Kind != GscTokenKind.String ||
            tokens[index + 3].Kind != GscTokenKind.CloseParenthesis)
            return false;

        string call = Slice(text, tokens[index]);
        if (!call.Equals("get_precached_anim", StringComparison.OrdinalIgnoreCase) &&
            !call.Equals("precacheMpAnim", StringComparison.OrdinalIgnoreCase))
            return false;

        return TrySimpleString(text, tokens[index + 2], out animation);
    }

    private static string? FindImmediateReceiverModel(
        string text, IReadOnlyList<GscToken> tokens, int animationIndex)
    {
        // A direct, adjacent pair proves which literal model belongs to the
        // receiver. No binding survives an intervening statement or branch.
        for (int i = Math.Max(0, animationIndex - 12); i + 11 < tokens.Count && i < animationIndex; i++)
        {
            if (tokens[i].Kind != GscTokenKind.Identifier ||
                tokens[i + 1].Kind != GscTokenKind.Identifier ||
                !Slice(text, tokens[i + 1]).Equals("setmodel", StringComparison.OrdinalIgnoreCase) ||
                tokens[i + 2].Kind != GscTokenKind.OpenParenthesis ||
                tokens[i + 3].Kind != GscTokenKind.String ||
                tokens[i + 4].Kind != GscTokenKind.CloseParenthesis ||
                tokens[i + 5].Kind != GscTokenKind.Semicolon ||
                tokens[i + 6].Kind != GscTokenKind.Identifier ||
                !Slice(text, tokens[i]).Equals(Slice(text, tokens[i + 6]), StringComparison.Ordinal) ||
                tokens[i + 7].Kind != GscTokenKind.Identifier ||
                !IsAnimationMethod(Slice(text, tokens[i + 7])) ||
                tokens[i + 8].Kind != GscTokenKind.OpenParenthesis ||
                tokens[i + 9].Kind != GscTokenKind.Modulo ||
                i + 9 != animationIndex)
                continue;

            return TrySimpleString(text, tokens[i + 3], out string model) ? model : null;
        }
        return null;
    }

    private static bool IsAnimationMethod(string name) =>
        name.Equals("setanim", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("setanimknob", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("playanim", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("scriptmodelplayanim", StringComparison.OrdinalIgnoreCase);

    private static bool TrySimpleString(string text, GscToken token, out string value)
    {
        string literal = Slice(text, token);
        value = string.Empty;
        if (literal.Length < 2 || literal[0] != '"' || literal[^1] != '"')
            return false;
        value = literal[1..^1];
        return IsAssetName(value);
    }

    private static bool IsAssetName(string value) =>
        value.Length != 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');

    private static string Slice(string text, GscToken token) =>
        text.Substring(token.Span.Start, token.Span.Length);

    private static IEnumerable<string> AtrPrimitives(string text)
    {
        // A primitive is a complete name-only line. A following opening brace
        // makes it a blend-group name instead of a concrete XAnim entry.
        IReadOnlyList<string> lines = ContentLines(text);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (!IsAssetName(line))
                continue;
            int next = i + 1;
            while (next < lines.Count && lines[next].Length == 0)
                next++;
            if (next < lines.Count && lines[next].StartsWith('{'))
                continue;
            yield return line;
        }
    }

    private static IEnumerable<string> PlayerAnimCommands(string text)
    {
        foreach (string line in ContentLines(text))
        {
            int separator = line.IndexOfAny([' ', '\t']);
            if (separator < 0)
                continue;
            string command = line[..separator];
            if (!command.Equals("both", StringComparison.OrdinalIgnoreCase) &&
                !command.Equals("legs", StringComparison.OrdinalIgnoreCase) &&
                !command.Equals("torso", StringComparison.OrdinalIgnoreCase) &&
                !command.Equals("turret", StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = line[(separator + 1)..].TrimStart();
            int end = rest.IndexOfAny([' ', '\t', '\r']);
            string animation = end < 0 ? rest : rest[..end];
            if (IsAssetName(animation))
                yield return animation;
        }
    }

    private static IReadOnlyList<string> ContentLines(string text)
    {
        var lines = new List<string>();
        bool blockComment = false;
        foreach (string line in text.Split('\n'))
        {
            var cleaned = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (blockComment)
                {
                    if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        blockComment = false;
                        i++;
                    }
                }
                else if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    blockComment = true;
                    i++;
                }
                else if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    break;
                else
                    cleaned.Append(line[i]);
            }
            lines.Add(cleaned.ToString().Trim());
        }
        return lines;
    }

    private sealed class Cache
    {
        internal long Revision = -1;
        internal IReadOnlyList<(string Name, string Text)> Sources = [];
        internal Dictionary<string, IReadOnlyList<(string Source, string? Model)>>? References;
    }
}
