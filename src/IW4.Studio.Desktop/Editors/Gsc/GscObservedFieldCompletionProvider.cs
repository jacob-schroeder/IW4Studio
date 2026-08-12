using IW4.Gsc.Workspace;
using IW4.Studio.Desktop.Gsc;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Produces advisory field completions from field accesses observed in active
/// scripts. Ranking favors exact bindings and receiver shapes without claiming
/// a static type relationship that GSC does not provide.
/// </summary>
internal static class GscObservedFieldCompletionProvider
{
    private const int MaximumCompletionCount = 200;

    internal static IReadOnlyList<GscEditorCompletion> GetCompletions(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath,
        GscFieldCompletionContext context,
        bool includeBaseDocument,
        CancellationToken cancellationToken)
    {
        GscSymbolId? receiverBinding = overlay.Index
            .FindDefinitions(currentPath, context.ReceiverProbeOffset)
            .FirstOrDefault(definition => definition.Kind is
                GscWorkspaceSymbolKind.Local or
                GscWorkspaceSymbolKind.Parameter)
            ?.Id;
        ObservedFieldCandidate[] candidates = EnumerateObservedFields(
                baseSnapshot,
                overlay,
                currentPath,
                includeBaseDocument,
                cancellationToken)
            .Where(field =>
                !field.Name.Equals("size", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(field.SourceName) &&
                field.Name.StartsWith(
                    context.Prefix,
                    StringComparison.OrdinalIgnoreCase))
            .Select(field => new
            {
                Field = field,
                Rank = RankField(
                    field,
                    context,
                    currentPath,
                    receiverBinding)
            })
            .GroupBy(
                candidate => candidate.Field.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                int rank = group.Min(candidate => candidate.Rank);
                var matchingRank = group
                    .Where(candidate => candidate.Rank == rank)
                    .ToArray();
                GscObservedField best = matchingRank
                    .OrderBy(candidate =>
                        candidate.Field.Location.Path == currentPath ? 0 : 1)
                    .ThenBy(candidate =>
                        candidate.Field.Location.Path.Value,
                        StringComparer.Ordinal)
                    .ThenBy(candidate =>
                        candidate.Field.Location.Span.Start)
                    .Select(candidate => candidate.Field)
                    .First();
                return new ObservedFieldCandidate(
                    best,
                    rank,
                    matchingRank.Length);
            })
            .OrderBy(candidate => candidate.Rank)
            .ThenByDescending(candidate => candidate.Frequency)
            .ThenBy(
                candidate => candidate.Field.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var completions = new List<GscEditorCompletion>(MaximumCompletionCount);
        if ("size".StartsWith(
                context.Prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            completions.Add(new GscEditorCompletion(
                context.ReplacementStart,
                "size",
                "size",
                "size",
                "Built-in size property",
                Kind: GscEditorCompletionKind.BuiltIn,
                Priority: 1_000));
        }

        completions.AddRange(candidates
            .Take(MaximumCompletionCount - completions.Count)
            .Select(candidate => CreateCompletion(context, candidate)));
        return Array.AsReadOnly(completions.ToArray());
    }

    private static int RankField(
        GscObservedField field,
        GscFieldCompletionContext context,
        GscScriptPath currentPath,
        GscSymbolId? receiverBinding)
    {
        bool currentDocument = field.Location.Path == currentPath;
        if (receiverBinding is not null &&
            field.Receiver.Binding == receiverBinding &&
            field.Receiver.ExpressionKey == context.ReceiverExpressionKey)
        {
            return 0;
        }
        if (field.Receiver.ExpressionKey == context.ReceiverExpressionKey)
            return currentDocument ? 1 : 2;
        if (context.ReceiverTerminalShape is not null &&
            field.Receiver.TerminalShape == context.ReceiverTerminalShape)
        {
            return currentDocument ? 3 : 4;
        }
        if (context.ReceiverBucket is not null &&
            field.Receiver.Bucket == context.ReceiverBucket)
        {
            return currentDocument ? 5 : 6;
        }

        return currentDocument ? 7 : 8;
    }

    private static GscEditorCompletion CreateCompletion(
        GscFieldCompletionContext context,
        ObservedFieldCandidate candidate)
    {
        GscObservedField field = candidate.Field;
        string observationText = candidate.Frequency == 1
            ? "1 observation"
            : $"{candidate.Frequency:N0} observations";
        return new GscEditorCompletion(
            context.ReplacementStart,
            field.SourceName,
            field.SourceName,
            field.SourceName,
            $"Observed field on '{field.Receiver.SourceText}' in " +
            $"active scripts ({observationText}; advisory)",
            Kind: GscEditorCompletionKind.Field,
            Priority: 100 - candidate.Rank);
    }

    private static IEnumerable<GscObservedField> EnumerateObservedFields(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath,
        bool includeBaseDocument,
        CancellationToken cancellationToken)
    {
        IEnumerable<GscObservedField> fields = overlay.Index.Documents
            .Where(document => document.Path.Kind == currentPath.Kind)
            .SelectMany(document => document.ObservedFields);
        if (includeBaseDocument)
        {
            GscIndexedDocument? baseDocument = baseSnapshot.Index.Documents
                .FirstOrDefault(document => document.Path == currentPath);
            if (baseDocument is not null)
                fields = fields.Concat(baseDocument.ObservedFields);
        }

        return EnumerateWithCancellation(fields, cancellationToken)
            .DistinctBy(field => (field.Location, field.Name));
    }

    private static IEnumerable<T> EnumerateWithCancellation<T>(
        IEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        foreach (T item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private sealed record ObservedFieldCandidate(
        GscObservedField Field,
        int Rank,
        int Frequency);
}
