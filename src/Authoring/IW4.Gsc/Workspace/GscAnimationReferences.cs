using IW4.Gsc.Analysis;
using IW4.Gsc.Semantics;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

/// <summary>Animation expressions and literal animation calls in a loaded script.</summary>
public static class GscAnimationReferences
{
    public static IReadOnlyList<(string Name, string? Model)> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var source = new GscSourceText(text);
        GscAnalysisResult analysis = new GscAnalyzer().Analyze(source);
        if (analysis.SemanticModel is null)
            return [];

        var references = new List<(string Name, string? Model)>();
        var tokens = analysis.Tokens;
        var modelAtArgument = new Dictionary<int, string>();
        int developerDepth = 0;
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind == GscTokenKind.DeveloperBlockOpen)
                developerDepth++;
            if (tokens[i].Kind == GscTokenKind.DeveloperBlockClose)
                developerDepth--;
            if (developerDepth != 0 || tokens[i].Kind != GscTokenKind.Identifier ||
                tokens[i + 1].Kind != GscTokenKind.OpenParenthesis)
                continue;

            string method = source.GetText(tokens[i].Span).ToLowerInvariant();
            bool playback = method is "setanim" or "setanimknob" or "playanim" or "scriptmodelplayanim";
            string? model = playback ? PrecedingSetModel(source, tokens, i) : null;
            if (model is not null)
                modelAtArgument[tokens[i + 2].Span.Start] = model;

            if (tokens[i + 2].Kind != GscTokenKind.String)
                continue;
            bool namedClip = method switch
            {
                "precachempanim" or "get_precached_anim" => tokens[i + 3].Kind == GscTokenKind.CloseParenthesis,
                "scriptmodelplayanim" => tokens[i + 3].Kind is GscTokenKind.CloseParenthesis or GscTokenKind.Comma,
                _ => false
            };
            if (namedClip)
            {
                string clip = GscConstantEvaluator.DecodeString(source.GetText(tokens[i + 2].Span));
                if (!string.IsNullOrWhiteSpace(clip))
                    references.Add((clip, model));
            }
        }

        foreach (var (tree, name, span) in GscWorkspaceResolver.FindAnimationReferences(
                     source, analysis.SemanticModel, CancellationToken.None))
        {
            if (name is not null)
                references.Add((name, modelAtArgument.GetValueOrDefault(span.Start)));
        }
        return references;
    }

    private static string? PrecedingSetModel(GscSourceText source, IReadOnlyList<GscToken> tokens, int method)
    {
        // Only adjacent statements on the same receiver establish its model.
        // A preceding unbraced conditional or an intervening call breaks the association.
        int first = method - 7;
        if (first < 1 || tokens[first - 1].Kind is not (GscTokenKind.OpenBrace or GscTokenKind.Semicolon) ||
            tokens[first].Kind is not (GscTokenKind.Identifier or GscTokenKind.SelfKeyword) ||
            tokens[first + 1].Kind != GscTokenKind.Identifier ||
            !source.GetText(tokens[first + 1].Span).Equals("setmodel", StringComparison.OrdinalIgnoreCase) ||
            tokens[first + 2].Kind != GscTokenKind.OpenParenthesis ||
            tokens[first + 3].Kind != GscTokenKind.String ||
            tokens[first + 4].Kind != GscTokenKind.CloseParenthesis ||
            tokens[first + 5].Kind != GscTokenKind.Semicolon ||
            tokens[first + 6].Kind != tokens[first].Kind ||
            !source.GetText(tokens[first].Span).Equals(source.GetText(tokens[first + 6].Span), StringComparison.OrdinalIgnoreCase))
            return null;

        string model = GscConstantEvaluator.DecodeString(source.GetText(tokens[first + 3].Span));
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }
}
