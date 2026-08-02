using System.Collections.ObjectModel;
using IW4.Gsc.Analysis;
using IW4.Gsc.Syntax;

namespace IW4.Gsc.Workspace;

/// <summary>
/// Immutable semantic index over a complete set of host-supplied script
/// snapshots. It has no dependency on asset pools, zones, or editor state.
/// </summary>
public sealed class GscWorkspaceIndex
{
    private readonly IReadOnlyDictionary<GscScriptPath, GscWorkspaceSourceDocument> _sources;
    private readonly IReadOnlyDictionary<GscScriptPath, GscIndexedDocument> _documentsByPath;
    private readonly IReadOnlyDictionary<GscSymbolId, GscSymbolDefinition> _definitionsById;
    private readonly IReadOnlyList<GscIndexedDocument> _documents;
    private readonly IReadOnlyList<GscFunctionDefinition> _functions;

    private GscWorkspaceIndex(
        IReadOnlyDictionary<GscScriptPath, GscWorkspaceSourceDocument> sources,
        CancellationToken cancellationToken)
    {
        _sources = new ReadOnlyDictionary<GscScriptPath, GscWorkspaceSourceDocument>(
            new Dictionary<GscScriptPath, GscWorkspaceSourceDocument>(sources));

        Dictionary<GscScriptPath, GscIndexedDocument> resolved =
            GscWorkspaceResolver.Resolve(_sources, cancellationToken);
        _documentsByPath = new ReadOnlyDictionary<GscScriptPath, GscIndexedDocument>(resolved);
        _documents = Array.AsReadOnly(
            resolved.Values
                .OrderBy(document => document.Path.Value, StringComparer.Ordinal)
                .ToArray());
        _functions = Array.AsReadOnly(
            _documents.SelectMany(document => document.Functions)
                .OrderBy(function => function.Location.Path.Value, StringComparer.Ordinal)
                .ThenBy(function => function.Location.Span.Start)
                .ToArray());
        _definitionsById = new ReadOnlyDictionary<GscSymbolId, GscSymbolDefinition>(
            _documents.SelectMany(document => document.Definitions)
                .ToDictionary(definition => definition.Id));
    }

    public IReadOnlyList<GscIndexedDocument> Documents => _documents;

    public IReadOnlyList<GscFunctionDefinition> Functions => _functions;

    public static GscWorkspaceIndex Create(
        IEnumerable<GscDocumentSnapshot> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var sources = new Dictionary<GscScriptPath, GscWorkspaceSourceDocument>();
        foreach (GscDocumentSnapshot document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document is null)
                throw new ArgumentException("A workspace document cannot be null.", nameof(documents));
            if (!sources.TryAdd(
                    document.Path,
                    GscDocumentIndexer.Build(document, cancellationToken)))
            {
                throw new ArgumentException(
                    $"The workspace contains duplicate script path '{document.Path}'.",
                    nameof(documents));
            }
        }

        return new GscWorkspaceIndex(sources, cancellationToken);
    }

    /// <summary>
    /// Returns a new index in which <paramref name="document"/> adds or
    /// replaces exactly one normalized path. Unchanged documents are not
    /// parsed or bound again.
    /// </summary>
    public GscWorkspaceIndex WithDocument(
        GscDocumentSnapshot document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var sources = new Dictionary<GscScriptPath, GscWorkspaceSourceDocument>(_sources)
        {
            [document.Path] = GscDocumentIndexer.Build(document, cancellationToken)
        };
        return new GscWorkspaceIndex(sources, cancellationToken);
    }

    public GscAnalysisResult GetAnalysis(GscScriptPath path) =>
        GetDocument(path).Analysis;

    public GscIndexedDocument GetDocument(GscScriptPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _documentsByPath.TryGetValue(path, out GscIndexedDocument? document)
            ? document
            : throw new KeyNotFoundException($"Script '{path}' is not indexed.");
    }

    public IReadOnlyList<GscSymbolDefinition> FindDefinitions(
        GscScriptPath sourcePath,
        int sourceOffset)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        GscIndexedDocument document = GetDocument(sourcePath);
        if (sourceOffset > document.Snapshot.Source.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));

        GscSymbolReference[] matchingReferences = document.References
            .Where(reference => Contains(reference.Location.Span, sourceOffset))
            .ToArray();
        if (matchingReferences.Length == 0 && sourceOffset > 0)
        {
            matchingReferences = document.References
                .Where(reference => Contains(reference.Location.Span, sourceOffset - 1))
                .ToArray();
        }

        GscSymbolId[] referencedIds = matchingReferences
            .SelectMany(reference => reference.Targets)
            .Distinct()
            .ToArray();
        IEnumerable<GscSymbolDefinition> definitions;
        if (referencedIds.Length != 0)
        {
            definitions = referencedIds.Select(id => _definitionsById[id]);
        }
        else
        {
            GscSymbolDefinition[] matchingDefinitions = document.Definitions
                .Where(definition => Contains(definition.Location.Span, sourceOffset))
                .ToArray();
            if (matchingDefinitions.Length == 0 && sourceOffset > 0)
            {
                matchingDefinitions = document.Definitions
                    .Where(definition => Contains(
                        definition.Location.Span,
                        sourceOffset - 1))
                    .ToArray();
            }
            definitions = matchingDefinitions;
        }

        return Array.AsReadOnly(
            definitions
                .DistinctBy(definition => definition.Id)
                .OrderBy(definition => definition.Location.Path.Value, StringComparer.Ordinal)
                .ThenBy(definition => definition.Location.Span.Start)
                .ToArray());
    }

    public IReadOnlyList<GscSymbolReference> FindUsages(GscSymbolId definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitionsById.ContainsKey(definition))
            return [];

        return Array.AsReadOnly(
            _documents.SelectMany(document => document.References)
                .Where(reference =>
                    reference.Kind != GscWorkspaceReferenceKind.Declaration &&
                    reference.Targets.Contains(definition))
                .OrderBy(reference => reference.Location.Path.Value, StringComparer.Ordinal)
                .ThenBy(reference => reference.Location.Span.Start)
                .ToArray());
    }

    /// <summary>
    /// Searches indexed declarations without parsing a potentially incomplete
    /// editor buffer.
    /// </summary>
    public IReadOnlyList<GscFunctionDefinition> FindFunctions(
        string namePrefix,
        GscScriptPath? targetPath = null)
    {
        ArgumentNullException.ThrowIfNull(namePrefix);
        IEnumerable<GscFunctionDefinition> candidates;
        if (targetPath is null)
        {
            candidates = _functions;
        }
        else if (_documentsByPath.TryGetValue(
                     targetPath,
                     out GscIndexedDocument? targetDocument))
        {
            candidates = targetDocument.Functions;
        }
        else
        {
            return [];
        }

        string canonicalPrefix = namePrefix.ToLowerInvariant();
        return Array.AsReadOnly(
            candidates.Where(function =>
                    function.Name.StartsWith(canonicalPrefix, StringComparison.Ordinal))
                .OrderBy(function => function.Name, StringComparer.Ordinal)
                .ThenBy(function => function.Location.Path.Value, StringComparer.Ordinal)
                .ThenBy(function => function.Location.Span.Start)
                .ToArray());
    }

    /// <summary>
    /// Resolves signatures using the same local-or-qualified rule as binding.
    /// </summary>
    public IReadOnlyList<GscFunctionDefinition> FindFunctionSignatures(
        GscScriptPath callingPath,
        string functionName,
        GscScriptPath? qualifiedTarget = null)
    {
        ArgumentNullException.ThrowIfNull(callingPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        GscScriptPath target = qualifiedTarget ?? callingPath;
        if (!_documentsByPath.TryGetValue(target, out GscIndexedDocument? targetDocument))
            return [];

        string canonicalName = functionName.ToLowerInvariant();
        return Array.AsReadOnly(
            targetDocument.Functions
                .Where(function => function.Name == canonicalName)
                .OrderBy(function => function.Location.Span.Start)
                .ToArray());
    }

    private static bool Contains(GscTextSpan span, int offset) =>
        span.Length == 0
            ? offset == span.Start
            : offset >= span.Start && offset < span.End;
}
