using IW4.Formats.D3dbsp;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Zone;
using IW4.Gsc.Syntax;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

/// <summary>Map placements whose declared destructible recipe uses an animation.</summary>
internal static class XAnimMapReferences
{
    internal static IEnumerable<(string Source, string? Model)> Find(
        FastFileWorkspace workspace, string animationName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationName);
        if (workspace.IsBlank)
            return [];

        string? typesSource = XAnimScriptReferences.GetSources(workspace)
            .FirstOrDefault(source => source.Name.Replace('\\', '/').Equals(
                "common_scripts/_destructible_types.gsc", StringComparison.OrdinalIgnoreCase)).Text;
        IReadOnlyDictionary<string, IReadOnlySet<string>> animationsByType =
            typesSource is null ? new Dictionary<string, IReadOnlySet<string>>() : ReadDestructibleAnimations(typesSource);

        var matches = new List<(string Source, string? Model)>();
        foreach (var entry in workspace.LoadedZone.Context.AssetPool.Entries)
        {
            if (entry.IsReferencePlaceholder)
                continue;
            IReadOnlyList<byte>? bytes = entry.Asset switch
            {
                MapEntsAsset map when entry.AssetType == XAssetType.MapEnts => map.EntityStringBytes,
                AddonMapEntsAsset map when entry.AssetType == XAssetType.AddonMapEnts => map.EntityStringBytes,
                _ => null
            };
            if (bytes is null || bytes.Count == 0)
                continue;

            IReadOnlyList<IReadOnlyDictionary<string, string>> entities;
            try
            {
                entities = D3dbspFile.Create([(D3dbspLumpType.Entities, bytes.ToArray())]).GetEntities();
            }
            catch (InvalidDataException)
            {
                continue;
            }

            for (int index = 0; index < entities.Count; index++)
            {
                IReadOnlyDictionary<string, string> entity = entities[index];
                string? model = Value(entity, "model");
                if (string.IsNullOrWhiteSpace(model) || model.StartsWith('?'))
                    model = null;

                foreach (KeyValuePair<string, string> property in entity)
                {
                    if (property.Key is not ("anim" or "animation" or "script_anim" or "script_animation") ||
                        !property.Value.Equals(animationName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    matches.Add(($"{entry.Name} · entity #{index + 1} · {property.Key}", model));
                }

                string? type = Value(entity, "destructible_type");
                if (type is not null && animationsByType.TryGetValue(type, out IReadOnlySet<string>? animations) &&
                    animations.Contains(animationName))
                    matches.Add(($"{entry.Name} · entity #{index + 1} · destructible {type}", model));
            }
        }
        return matches;
    }

    private static string? Value(IReadOnlyDictionary<string, string> entity, string key) =>
        entity.FirstOrDefault(property => property.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReadDestructibleAnimations(string text)
    {
        var source = new GscSourceText(text);
        IReadOnlyList<GscToken> tokens = new GscSyntaxAnalyzer().Analyze(source).Tokens;
        var byType = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        if (FunctionBody(tokens, source, "makeType") is not { } dispatch)
            return byType;

        for (int index = dispatch.Start; index + 4 < dispatch.End; index++)
        {
            if (tokens[index].Kind != GscTokenKind.CaseKeyword ||
                tokens[index + 1].Kind != GscTokenKind.String ||
                tokens[index + 2].Kind != GscTokenKind.Colon ||
                tokens[index + 3].Kind != GscTokenKind.Identifier ||
                tokens[index + 4].Kind != GscTokenKind.OpenParenthesis)
                continue;

            string type = StringValue(source, tokens[index + 1]);
            string handler = source.GetText(tokens[index + 3].Span);
            if (FunctionBody(tokens, source, handler) is not { } body)
                continue;

            var animations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int call = body.Start; call + 3 < body.End; call++)
                if (tokens[call].Kind == GscTokenKind.Identifier &&
                    source.GetText(tokens[call].Span).Equals("get_precached_anim", StringComparison.OrdinalIgnoreCase) &&
                    tokens[call + 1].Kind == GscTokenKind.OpenParenthesis &&
                    tokens[call + 2].Kind == GscTokenKind.String &&
                    tokens[call + 3].Kind == GscTokenKind.CloseParenthesis)
                    animations.Add(StringValue(source, tokens[call + 2]));
            if (animations.Count > 0)
                byType[type] = animations;
        }
        return byType;
    }

    private static string StringValue(GscSourceText source, GscToken token)
    {
        string literal = source.GetText(token.Span);
        return literal.Length >= 2 ? literal[1..^1] : string.Empty;
    }

    private static (int Start, int End)? FunctionBody(
        IReadOnlyList<GscToken> tokens, GscSourceText source, string name)
    {
        int depth = 0;
        for (int index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Kind == GscTokenKind.CloseBrace)
                depth--;
            if (depth == 0 && tokens[index].Kind == GscTokenKind.Identifier &&
                source.GetText(tokens[index].Span).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                tokens[index + 1].Kind == GscTokenKind.OpenParenthesis)
            {
                int cursor = index + 2;
                int parentheses = 1;
                while (cursor < tokens.Count && parentheses > 0)
                {
                    parentheses += tokens[cursor].Kind switch
                    {
                        GscTokenKind.OpenParenthesis => 1,
                        GscTokenKind.CloseParenthesis => -1,
                        _ => 0
                    };
                    cursor++;
                }
                if (parentheses != 0 || cursor >= tokens.Count || tokens[cursor].Kind != GscTokenKind.OpenBrace)
                    continue;
                int start = cursor + 1;
                int braces = 1;
                for (cursor = start; cursor < tokens.Count; cursor++)
                {
                    braces += tokens[cursor].Kind switch
                    {
                        GscTokenKind.OpenBrace => 1,
                        GscTokenKind.CloseBrace => -1,
                        _ => 0
                    };
                    if (braces == 0)
                        return (start, cursor);
                }
                return null;
            }
            if (tokens[index].Kind == GscTokenKind.OpenBrace)
                depth++;
        }
        return null;
    }
}
