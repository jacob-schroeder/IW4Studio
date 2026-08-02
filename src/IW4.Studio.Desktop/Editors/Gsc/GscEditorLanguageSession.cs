using IW4.Gsc.Analysis;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using IW4.Studio.Gsc;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Editor-local façade over the immutable Studio workspace. It overlays one
/// unsaved buffer and invalidates its derived snapshot for either a buffer or
/// XAssetPool revision change.
/// </summary>
internal sealed class GscEditorLanguageSession
{
    private const string ObservedCallableDescription =
        "Observed callable in active scripts; parameter metadata unavailable";

    private readonly object _sync = new();
    private readonly GscWorkspaceIndexService _workspace;
    private GscWorkspaceSnapshot? _cachedBaseSnapshot;
    private GscWorkspaceSnapshot? _cachedSnapshot;
    private GscScriptPath? _cachedPath;
    private long _cachedBufferVersion = -1;

    internal GscEditorLanguageSession(GscWorkspaceIndexService workspace) =>
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    internal GscAnalysisResult Analyze(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        CancellationToken cancellationToken) =>
        GetSnapshots(assetName, source, bufferVersion, cancellationToken)
            .Overlay
            .GetAnalysis(assetName);

    internal void FindDefinitions(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        int sourceOffset,
        out GscWorkspaceSnapshot snapshot,
        out GscSymbolDefinition[] definitions,
        CancellationToken cancellationToken = default)
    {
        snapshot = GetSnapshots(
            assetName,
            source,
            bufferVersion,
            cancellationToken).Overlay;
        GscScriptPath path = GscScriptPath.FromAssetName(assetName);
        definitions = snapshot.Index.FindDefinitions(path, sourceOffset).ToArray();
        if (definitions.Length == 0 && sourceOffset > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            definitions = snapshot.Index.FindDefinitions(path, sourceOffset - 1)
                .ToArray();
        }
    }

    internal IReadOnlyList<GscEditorCompletion> GetFunctionCompletions(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        int caretOffset)
    {
        (GscWorkspaceSnapshot baseSnapshot, GscWorkspaceSnapshot overlay) =
            GetSnapshots(
                assetName,
                source,
                bufferVersion);
        GscScriptPath currentPath = GscScriptPath.FromAssetName(assetName);
        GscCompletionPrefix prefix = GscEditorTextQueries.FindCompletionPrefix(
            source.Text,
            caretOffset);
        GscScriptPath? targetPath = ResolveQualifier(currentPath, prefix.Qualifier);
        if (prefix.Qualifier is not null && targetPath is null)
            return [];

        IEnumerable<GscFunctionDefinition> functions =
            overlay.Index.FindFunctions(prefix.Name, targetPath);
        if ((targetPath is null || targetPath == currentPath) &&
            HasSyntaxErrors(overlay, currentPath))
        {
            functions = functions.Concat(
                baseSnapshot.Index.FindFunctions(prefix.Name, currentPath));
        }

        GscFunctionDefinition[] orderedFunctions = functions
            .OrderBy(function =>
                function.Location.Path == currentPath ? 0 : 1)
            .ThenBy(function => function.Name, StringComparer.Ordinal)
            .ThenBy(
                function => function.Location.Path.Value,
                StringComparer.Ordinal)
            .ToArray();
        IEnumerable<GscEditorCompletion> completions;
        if (targetPath is not null)
        {
            completions = orderedFunctions.Select(function => CreateCompletion(
                currentPath,
                function,
                prefix,
                hasExplicitQualifier: true));
        }
        else
        {
            completions = orderedFunctions
                .Where(function => function.Location.Path == currentPath)
                .Select(function => CreateCompletion(
                    currentPath,
                    function,
                    prefix,
                    hasExplicitQualifier: false))
                .Concat(FindObservedCallables(
                        baseSnapshot,
                        overlay,
                        currentPath,
                        prefix.Name)
                    .Select(reference => CreateObservedCompletion(
                        prefix,
                        reference)))
                .Concat(orderedFunctions
                    .Where(function => function.Location.Path != currentPath)
                    .Select(function => CreateCompletion(
                        currentPath,
                        function,
                        prefix,
                        hasExplicitQualifier: false)));
        }

        return Array.AsReadOnly(completions
            .DistinctBy(
                completion => completion.InsertionText,
                StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToArray());
    }

    internal GscEditorSignatureHelp? GetSignatureHelp(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        int caretOffset)
    {
        GscCallSite? call = GscEditorTextQueries.FindContainingCall(
            source.Text,
            caretOffset);
        if (call is null)
            return null;

        GscScriptPath currentPath = GscScriptPath.FromAssetName(assetName);
        GscScriptPath? targetPath = ResolveQualifier(currentPath, call.Qualifier);
        if (call.Qualifier is not null && targetPath is null)
            return null;

        (GscWorkspaceSnapshot baseSnapshot, GscWorkspaceSnapshot overlay) =
            GetSnapshots(
                assetName,
                source,
                bufferVersion);
        IReadOnlyList<GscFunctionDefinition> functions =
            overlay.Index.FindFunctionSignatures(
                currentPath,
                call.Name,
                targetPath);
        if (functions.Count == 0 &&
            (targetPath is null || targetPath == currentPath) &&
            HasSyntaxErrors(overlay, currentPath))
        {
            functions = baseSnapshot.Index.FindFunctionSignatures(
                currentPath,
                call.Name,
                currentPath);
        }
        if (functions.Count == 0)
        {
            GscSymbolReference? observed = targetPath is null
                ? FindObservedCallable(
                    baseSnapshot,
                    overlay,
                    currentPath,
                    call.Name,
                    call.NameStart)
                : null;
            if (observed is null)
                return null;

            return new GscEditorSignatureHelp(
                [CreateObservedSignature(
                    observed.SourceName,
                    call.ActiveParameter)],
                call.ActiveParameter);
        }

        return new GscEditorSignatureHelp(
            functions.Select(function => CreateSignature(
                function,
                call.ActiveParameter)),
            call.ActiveParameter);
    }

    private (GscWorkspaceSnapshot Base, GscWorkspaceSnapshot Overlay) GetSnapshots(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        CancellationToken cancellationToken = default)
    {
        GscScriptPath path = GscScriptPath.FromAssetName(assetName);
        lock (_sync)
        {
            GscWorkspaceSnapshot baseSnapshot = _workspace.GetSnapshot(
                cancellationToken);
            if (_cachedSnapshot is not null &&
                _cachedSnapshot.AssetPoolRevision == baseSnapshot.AssetPoolRevision &&
                _cachedBufferVersion == bufferVersion &&
                _cachedPath == path)
            {
                return (_cachedBaseSnapshot!, _cachedSnapshot);
            }

            _cachedSnapshot = _workspace.GetSnapshot(
                new GscWorkspaceBufferOverlay(assetName, source),
                cancellationToken);
            _cachedBaseSnapshot = baseSnapshot;
            _cachedPath = path;
            _cachedBufferVersion = bufferVersion;
            return (baseSnapshot, _cachedSnapshot);
        }
    }

    private static bool HasSyntaxErrors(
        GscWorkspaceSnapshot snapshot,
        GscScriptPath path) =>
        snapshot.Index.GetAnalysis(path).Diagnostics.Any(diagnostic =>
            diagnostic.Stage == GscDiagnosticStage.Syntax &&
            diagnostic.Severity == GscDiagnosticSeverity.Error);

    private static IEnumerable<GscSymbolReference> FindObservedCallables(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath,
        string namePrefix) =>
        EnumerateObservedCallables(baseSnapshot, overlay, currentPath)
            .Where(reference => reference.Name.StartsWith(
                namePrefix,
                StringComparison.Ordinal))
            .GroupBy(reference => reference.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(reference => reference.Name, StringComparer.Ordinal);

    private static GscSymbolReference? FindObservedCallable(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath,
        string name,
        int currentCallStart) =>
        EnumerateObservedCallables(baseSnapshot, overlay, currentPath).FirstOrDefault(
            reference =>
                reference.Name == name &&
                (reference.Location.Path != currentPath ||
                 reference.Location.Span.Start != currentCallStart));

    private static IEnumerable<GscSymbolReference> EnumerateObservedCallables(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath)
    {
        IEnumerable<GscSymbolReference> references = overlay.Index.Documents
            .Where(document => document.Path.Kind == currentPath.Kind)
            .SelectMany(document => document.References);
        if (HasSyntaxErrors(overlay, currentPath))
        {
            GscIndexedDocument? baseDocument = baseSnapshot.Index.Documents
                .FirstOrDefault(document => document.Path == currentPath);
            if (baseDocument is not null)
                references = references.Concat(baseDocument.References);
        }

        return references
            .Where(reference =>
                reference.Kind == GscWorkspaceReferenceKind.Call &&
                reference.Targets.Count == 0 &&
                reference.QualifiedTargetPath is null)
            .DistinctBy(reference => (
                reference.Location,
                reference.Name,
                reference.Kind));
    }

    private static GscScriptPath? ResolveQualifier(
        GscScriptPath currentPath,
        string? qualifier)
    {
        if (qualifier is null)
            return null;

        try
        {
            return currentPath.ResolveReference(qualifier);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static GscEditorCompletion CreateCompletion(
        GscScriptPath currentPath,
        GscFunctionDefinition function,
        GscCompletionPrefix prefix,
        bool hasExplicitQualifier)
    {
        if (hasExplicitQualifier || function.Location.Path == currentPath)
        {
            return new GscEditorCompletion(
                prefix.ReplacementStart,
                function.SourceName,
                function.DeclarationSignature,
                function.Location.Path.Value);
        }

        string qualifiedPath = RemoveScriptExtension(
                function.Location.Path.Value)
            .Replace('/', '\\');
        return new GscEditorCompletion(
            prefix.ReplacementStart,
            $"{qualifiedPath}::{function.SourceName}",
            $"{qualifiedPath}::{function.DeclarationSignature}",
            function.Location.Path.Value);
    }

    private static GscEditorCompletion CreateObservedCompletion(
        GscCompletionPrefix prefix,
        GscSymbolReference reference) =>
        new(
            prefix.ReplacementStart,
            reference.SourceName,
            $"{reference.SourceName}(…)",
            ObservedCallableDescription);

    private static GscEditorSignature CreateSignature(
        GscFunctionDefinition function,
        int activeParameter)
    {
        string activeParameterText = function.Parameters.Count == 0
            ? "No parameters"
            : activeParameter < function.Parameters.Count
                ? $"Parameter {activeParameter + 1} of {function.Parameters.Count}: " +
                  function.Parameters[activeParameter].SourceName
                : $"Parameter {activeParameter + 1}; function declares " +
                  function.Parameters.Count;
        return new GscEditorSignature(
            function.DeclarationSignature,
            activeParameterText);
    }

    private static GscEditorSignature CreateObservedSignature(
        string name,
        int activeParameter) =>
        new(
            $"{name}(…)",
            $"Argument {activeParameter + 1}; parameter metadata is " +
            "unavailable for this observed callable");

    private static string RemoveScriptExtension(string path) =>
        path.EndsWith(".gsc", StringComparison.Ordinal) ||
        path.EndsWith(".csc", StringComparison.Ordinal)
            ? path[..^4]
            : path;
}
