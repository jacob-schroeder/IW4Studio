using System.Runtime.CompilerServices;
using IW4.Formats.D3dbsp;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Zone;
using IW4.Gsc.Syntax;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

/// <summary>Explicit map animation fields, animated models, and placed destructible recipes.</summary>
internal static class XAnimMapReferences
{
    private static readonly ConditionalWeakTable<FastFileWorkspace, Cache> Caches = new();

    internal static IEnumerable<(string Source, string? Model)> Find(
        FastFileWorkspace workspace, string animationName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationName);
        if (workspace.IsBlank)
            return [];

        Cache cache = Caches.GetValue(workspace, _ => new Cache());
        lock (cache)
        {
            long revision = workspace.LoadedZone.Context.AssetPool.Revision;
            if (cache.Revision != revision)
            {
                cache.References = Index(workspace);
                cache.Revision = revision;
            }
            return cache.References.TryGetValue(XAssetStableIdentity.NormalizeLookupName(animationName), out var matches)
                ? matches
                : [];
        }
    }

    private static Dictionary<string, IReadOnlyList<(string Source, string? Model)>> Index(FastFileWorkspace workspace)
    {
        long revision = workspace.LoadedZone.Context.AssetPool.Revision;

        IReadOnlyList<(string Name, string Text)> sources = XAnimScriptReferences.GetSources(workspace);
        string? typesSource = sources
            .FirstOrDefault(source => XAssetStableIdentity.NormalizeLookupName(source.Name) ==
                "common_scripts/_destructible_types.gsc").Text;
        IReadOnlyDictionary<string, IReadOnlySet<string>> animationsByType =
            typesSource is null ? new Dictionary<string, IReadOnlySet<string>>() : ReadDestructibleAnimations(typesSource);
        HashSet<AnimatedRegistration> registrations = ReadAnimatedRegistrations(sources);
        ILookup<(string Script, string Model), AnimatedRegistration> registrationsBySource =
            registrations.ToLookup(registration => (registration.Script, registration.Model));
        var placedRegistrations = new HashSet<AnimatedRegistration>();

        var placements = new Dictionary<string, Dictionary<(string Map, string Rule, string? Model), int>>(
            StringComparer.OrdinalIgnoreCase);
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

            string mapName = Path.GetFileNameWithoutExtension(
                XAssetStableIdentity.GetLookupSpelling(entry.Name).Replace('\\', '/'));
            for (int index = 0; index < entities.Count; index++)
            {
                IReadOnlyDictionary<string, string> entity = entities[index];
                string? model = Value(entity, "model");
                model = string.IsNullOrWhiteSpace(model) || model.StartsWith('?') || model.StartsWith('*')
                    ? null
                    : XAssetStableIdentity.NormalizeLookupName(model);

                string? directAnimation = Value(entity, "xanim");
                if (!string.IsNullOrWhiteSpace(directAnimation))
                    AddPlacement(directAnimation, mapName, "xanim field", model);

                string? target = Value(entity, "targetname");
                if (target == "animated_model")
                {
                    string? assignedAnimation = Value(entity, "animation");
                    if (!string.IsNullOrWhiteSpace(assignedAnimation))
                        AddPlacement(assignedAnimation, mapName, "animation field", model);
                    else if (model is not null && ScriptPath(Value(entity, "precache_script")) is { } script)
                    {
                        foreach (AnimatedRegistration registration in registrationsBySource[(script, model)])
                        {
                            AddPlacement(registration.Animation, mapName, "animated_model", model);
                            placedRegistrations.Add(registration);
                        }
                    }
                }
                if (target is not ("destructible_toy" or "destructible_vehicle"))
                    continue;
                if (model is null)
                    continue;

                string? type = Value(entity, "destructible_type");
                if (type is null || !animationsByType.TryGetValue(type, out IReadOnlySet<string>? animations))
                    continue;
                foreach (string animation in animations)
                    AddPlacement(animation, mapName, type, model);
            }
        }
        if (workspace.LoadedZone.Context.AssetPool.Revision != revision)
            throw new InvalidOperationException("The loaded asset pool changed during map reference capture.");
        Dictionary<string, IReadOnlyList<(string Source, string? Model)>> references = placements.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<(string Source, string? Model)>)pair.Value
                .OrderBy(group => group.Key.Map, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key.Rule, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key.Model ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => (
                    $"{group.Key.Map} · {group.Key.Rule} · {group.Value} placement{(group.Value == 1 ? "" : "s")}",
                    group.Key.Model))
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        foreach (AnimatedRegistration registration in registrations.Except(placedRegistrations)
                     .OrderBy(item => item.Script, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Model, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Animation, StringComparer.OrdinalIgnoreCase))
        {
            if (!references.TryGetValue(registration.Animation, out IReadOnlyList<(string Source, string? Model)>? existing))
                existing = [];
            references[registration.Animation] = [.. existing,
                ($"{registration.Script} · anim_prop_models", registration.Model)];
        }
        return references;

        void AddPlacement(string animation, string map, string rule, string? model)
        {
            string name = XAssetStableIdentity.NormalizeLookupName(animation);
            if (!placements.TryGetValue(name, out var grouped))
                placements.Add(name, grouped = []);
            var key = (map, rule, model);
            grouped[key] = grouped.GetValueOrDefault(key) + 1;
        }
    }

    private readonly record struct AnimatedRegistration(string Script, string Model, string Animation);

    private static string? ScriptPath(string? precache)
    {
        if (string.IsNullOrWhiteSpace(precache))
            return null;
        string[] parts = precache.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string path = string.Join('/', parts);
        return XAssetStableIdentity.NormalizeLookupName(
            path.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ? path : path + ".gsc");
    }

    private sealed class Cache
    {
        internal long Revision = -1;
        internal Dictionary<string, IReadOnlyList<(string Source, string? Model)>> References = [];
    }

    private static string? Value(IReadOnlyDictionary<string, string> entity, string key) =>
        entity.FirstOrDefault(property => property.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static HashSet<AnimatedRegistration> ReadAnimatedRegistrations(
        IReadOnlyList<(string Name, string Text)> sources)
    {
        var registrations = new HashSet<AnimatedRegistration>();
        foreach ((string name, string text) in sources)
        {
            if (!name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ||
                !text.Contains("anim_prop_models", StringComparison.OrdinalIgnoreCase))
                continue;

            var source = new GscSourceText(text);
            GscSyntaxResult syntax = new GscSyntaxAnalyzer().Analyze(source);
            if (!syntax.IsAccepted || FunctionBody(syntax.Tokens, source, "main") is not { } body)
                continue;
            IReadOnlyList<GscToken> tokens = syntax.Tokens;
            string script = XAssetStableIdentity.NormalizeLookupName(name);
            string? localModel = null;
            int depth = 0;
            for (int index = body.Start; index + 11 < body.End; index++)
            {
                if (tokens[index].Kind == GscTokenKind.OpenBrace) depth++;
                if (tokens[index].Kind == GscTokenKind.CloseBrace) depth--;
                if (Token(tokens, source, index, GscTokenKind.Identifier, "model") &&
                    tokens[index - 1].Kind != GscTokenKind.Dot && tokens[index + 1].Kind == GscTokenKind.Assign)
                {
                    // Keep only a literal assignment that dominates the later registration.
                    bool unconditional = depth == 0 && tokens[index - 1].Kind is
                        GscTokenKind.Semicolon or GscTokenKind.OpenBrace or GscTokenKind.CloseBrace;
                    localModel = unconditional && tokens[index + 2].Kind == GscTokenKind.String &&
                        tokens[index + 3].Kind == GscTokenKind.Semicolon ? StringValue(source, tokens[index + 2]) : null;
                }
                if (tokens[index].Kind != GscTokenKind.LevelKeyword ||
                    tokens[index + 1].Kind != GscTokenKind.Dot ||
                    !Token(tokens, source, index + 2, GscTokenKind.Identifier, "anim_prop_models") ||
                    tokens[index + 3].Kind != GscTokenKind.OpenBracket ||
                    tokens[index + 5].Kind != GscTokenKind.CloseBracket ||
                    tokens[index + 6].Kind != GscTokenKind.OpenBracket ||
                    tokens[index + 7].Kind != GscTokenKind.String ||
                    tokens[index + 8].Kind != GscTokenKind.CloseBracket ||
                    tokens[index + 9].Kind != GscTokenKind.Assign)
                    continue;

                string? model = tokens[index + 4].Kind == GscTokenKind.String
                    ? StringValue(source, tokens[index + 4])
                    : Token(tokens, source, index + 4, GscTokenKind.Identifier, "model") ? localModel : null;
                if (string.IsNullOrWhiteSpace(model))
                    continue;
                string? animation = tokens[index + 10].Kind == GscTokenKind.String &&
                    tokens[index + 11].Kind == GscTokenKind.Semicolon
                    ? StringValue(source, tokens[index + 10])
                    : index + 12 < body.End && tokens[index + 10].Kind == GscTokenKind.Modulo &&
                      tokens[index + 11].Kind == GscTokenKind.Identifier &&
                      tokens[index + 12].Kind == GscTokenKind.Semicolon
                        ? source.GetText(tokens[index + 11].Span)
                        : null;
                if (!string.IsNullOrWhiteSpace(animation))
                    registrations.Add(new(script, XAssetStableIdentity.NormalizeLookupName(model),
                        XAssetStableIdentity.NormalizeLookupName(animation)));
            }
        }
        return registrations;
    }

    private static bool Token(IReadOnlyList<GscToken> tokens, GscSourceText source, int index,
        GscTokenKind kind, string value) =>
        tokens[index].Kind == kind && source.GetText(tokens[index].Span).Equals(value, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReadDestructibleAnimations(string text)
    {
        var source = new GscSourceText(text);
        GscSyntaxResult syntax = new GscSyntaxAnalyzer().Analyze(source);
        var byType = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        if (!syntax.IsAccepted)
            return byType;
        IReadOnlyList<GscToken> tokens = syntax.Tokens;
        if (FunctionBody(tokens, source, "makeType") is not { } dispatch)
            return byType;
        var byHandler = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

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
            int callEnd = index + 5;
            int parentheses = 1;
            while (callEnd < dispatch.End && parentheses > 0)
            {
                parentheses += tokens[callEnd].Kind switch
                {
                    GscTokenKind.OpenParenthesis => 1,
                    GscTokenKind.CloseParenthesis => -1,
                    _ => 0
                };
                callEnd++;
            }
            // This case must directly call its recipe and break; fallthrough is not inferred.
            if (parentheses != 0 || callEnd + 2 >= dispatch.End ||
                tokens[callEnd].Kind != GscTokenKind.Semicolon ||
                tokens[callEnd + 1].Kind != GscTokenKind.BreakKeyword ||
                tokens[callEnd + 2].Kind != GscTokenKind.Semicolon)
                continue;
            if (!byHandler.TryGetValue(handler, out IReadOnlySet<string>? animations))
            {
                if (FunctionBody(tokens, source, handler) is not { } body)
                    continue;
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int call = body.Start; call + 3 < body.End; call++)
                {
                    if (tokens[call].Kind != GscTokenKind.Identifier)
                        continue;
                    string callName = source.GetText(tokens[call].Span);
                    if (callName.Equals("get_precached_anim", StringComparison.OrdinalIgnoreCase) &&
                        tokens[call + 1].Kind == GscTokenKind.OpenParenthesis &&
                        tokens[call + 2].Kind == GscTokenKind.String &&
                        tokens[call + 3].Kind == GscTokenKind.CloseParenthesis)
                        found.Add(StringValue(source, tokens[call + 2]));
                    else if (callName.Equals("destructible_anim", StringComparison.OrdinalIgnoreCase) &&
                             tokens[call + 1].Kind == GscTokenKind.OpenParenthesis &&
                             tokens[call + 2].Kind == GscTokenKind.Modulo &&
                             tokens[call + 3].Kind == GscTokenKind.Identifier)
                        found.Add(source.GetText(tokens[call + 3].Span));
                }
                animations = found;
                byHandler.Add(handler, animations);
            }
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
