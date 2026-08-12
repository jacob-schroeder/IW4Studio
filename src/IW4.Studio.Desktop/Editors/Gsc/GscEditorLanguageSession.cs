using IW4.Gsc.Analysis;
using IW4.Gsc.BuiltIns;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using IW4.Studio.Desktop.Gsc;

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

    internal void FindDefinitionTargets(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        int sourceOffset,
        out GscWorkspaceSnapshot snapshot,
        out GscSymbolDefinition[] definitions,
        out Iw4GscBuiltInDefinition[] builtIns,
        CancellationToken cancellationToken = default)
    {
        FindDefinitions(
            assetName,
            source,
            bufferVersion,
            sourceOffset,
            out snapshot,
            out definitions,
            cancellationToken);
        builtIns = definitions.Length == 0
            ? FindBuiltIns(snapshot, assetName, sourceOffset)
            : [];
    }

    internal IReadOnlyList<GscEditorCompletion> GetCompletions(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        int caretOffset,
        bool requireAutomaticContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (GscWorkspaceSnapshot baseSnapshot, GscWorkspaceSnapshot overlay) =
            GetSnapshots(
                assetName,
                source,
                bufferVersion,
                cancellationToken);
        GscScriptPath currentPath = GscScriptPath.FromAssetName(assetName);
        GscAnalysisResult analysis = overlay.Index.GetAnalysis(currentPath);
        GscCompletionContext? context = GscCompletionContextQueries.Find(
            source.Text,
            caretOffset,
            requireAutomaticContext,
            analysis.Tokens,
            cancellationToken);
        if (context is null)
            return [];

        return context switch
        {
            GscCallableCompletionContext callable => GetCallableCompletions(
                baseSnapshot,
                overlay,
                currentPath,
                callable,
                cancellationToken),
            GscFieldCompletionContext field =>
                GscObservedFieldCompletionProvider.GetCompletions(
                    baseSnapshot,
                    overlay,
                    currentPath,
                    field,
                    HasSyntaxErrors(overlay, currentPath),
                    cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unknown GSC completion context '{context.GetType().Name}'.")
        };
    }

    private static IReadOnlyList<GscEditorCompletion> GetCallableCompletions(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath,
        GscCallableCompletionContext context,
        CancellationToken cancellationToken)
    {
        GscScriptPath? targetPath = ResolveQualifier(
            currentPath,
            context.Qualifier);
        if (context.Qualifier is not null && targetPath is null)
            return [];

        IEnumerable<GscFunctionDefinition> functions =
            overlay.Index.FindFunctions(context.Prefix, targetPath);
        if ((targetPath is null || targetPath == currentPath) &&
            HasSyntaxErrors(overlay, currentPath))
        {
            functions = functions.Concat(
                baseSnapshot.Index.FindFunctions(context.Prefix, currentPath));
        }

        GscFunctionDefinition[] orderedFunctions = functions
            .OrderBy(function =>
                function.Location.Path == currentPath ? 0 : 1)
            .ThenBy(function => function.Name, StringComparer.Ordinal)
            .ThenBy(
                function => function.Location.Path.Value,
                StringComparer.Ordinal)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<GscEditorCompletion> completions;
        if (targetPath is not null)
        {
            completions = orderedFunctions.Select(function => CreateCompletion(
                currentPath,
                function,
                context,
                hasExplicitQualifier: true));
        }
        else
        {
            completions = orderedFunctions
                .Where(function => function.Location.Path == currentPath)
                .Select(function => CreateCompletion(
                    currentPath,
                    function,
                    context,
                    hasExplicitQualifier: false))
                .Concat(Iw4GscBuiltInCatalog.Multiplayer
                    .FindCallables(context.Prefix)
                    .Select(builtIn => CreateBuiltInCompletion(
                        context,
                        builtIn)))
                .Concat(FindObservedCallables(
                        baseSnapshot,
                        overlay,
                        currentPath,
                        context.Prefix,
                        cancellationToken)
                    .Select(reference => CreateObservedCompletion(
                        context,
                        reference)))
                .Concat(orderedFunctions
                    .Where(function => function.Location.Path != currentPath)
                    .Select(function => CreateCompletion(
                        currentPath,
                        function,
                        context,
                        hasExplicitQualifier: false)));
        }

        GscEditorCompletion[] result = completions
            .DistinctBy(
                completion => completion.InsertionText,
                StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(result);
    }

    internal GscEditorSignatureHelp? GetSignatureHelp(
        string assetName,
        GscSourceText source,
        long bufferVersion,
        int caretOffset,
        CancellationToken cancellationToken = default)
    {
        GscCallSite? call = GscEditorTextQueries.FindContainingCall(
            source.Text,
            caretOffset,
            cancellationToken);
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
                bufferVersion,
                cancellationToken);
        IReadOnlyList<GscFunctionDefinition> functions =
            overlay.Index.FindFunctionSignatures(
                currentPath,
                call.Name,
                targetPath);
        cancellationToken.ThrowIfCancellationRequested();
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
            if (targetPath is null)
            {
                IReadOnlyList<Iw4GscBuiltInDefinition> builtIns =
                    Iw4GscBuiltInCatalog.Multiplayer.FindCallablesByName(
                        call.Name);
                if (builtIns.Count != 0)
                {
                    return new GscEditorSignatureHelp(
                        builtIns.Select(builtIn => CreateBuiltInSignature(
                            builtIn,
                            call.ActiveParameter)),
                        call.ActiveParameter);
                }
            }

            GscSymbolReference? observed = targetPath is null
                ? FindObservedCallable(
                    baseSnapshot,
                    overlay,
                    currentPath,
                    call.Name,
                    call.NameStart,
                    cancellationToken)
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
        cancellationToken.ThrowIfCancellationRequested();
        GscScriptPath path = GscScriptPath.FromAssetName(assetName);
        lock (_sync)
        {
            GscWorkspaceSnapshot baseSnapshot = _workspace.GetSnapshot(
                cancellationToken);
            if (_cachedSnapshot is not null &&
                ReferenceEquals(_cachedBaseSnapshot, baseSnapshot) &&
                _cachedBufferVersion == bufferVersion &&
                _cachedPath == path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (_cachedBaseSnapshot!, _cachedSnapshot);
            }

            GscWorkspaceSnapshot overlaySnapshot = baseSnapshot.WithOverlay(
                new GscWorkspaceBufferOverlay(assetName, source),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _cachedSnapshot = overlaySnapshot;
            _cachedBaseSnapshot = baseSnapshot;
            _cachedPath = path;
            _cachedBufferVersion = bufferVersion;
            return (baseSnapshot, overlaySnapshot);
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
        string namePrefix,
        CancellationToken cancellationToken) =>
        EnumerateObservedCallables(
                baseSnapshot,
                overlay,
                currentPath,
                cancellationToken)
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
        int currentCallStart,
        CancellationToken cancellationToken) =>
        EnumerateObservedCallables(
                baseSnapshot,
                overlay,
                currentPath,
                cancellationToken)
            .FirstOrDefault(
                reference =>
                    reference.Name == name &&
                    (reference.Location.Path != currentPath ||
                     reference.Location.Span.Start != currentCallStart));

    private static IEnumerable<GscSymbolReference> EnumerateObservedCallables(
        GscWorkspaceSnapshot baseSnapshot,
        GscWorkspaceSnapshot overlay,
        GscScriptPath currentPath,
        CancellationToken cancellationToken)
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

        return EnumerateWithCancellation(references, cancellationToken)
            .Where(reference =>
                reference.Kind == GscWorkspaceReferenceKind.Call &&
                reference.Targets.Count == 0 &&
                reference.QualifiedTargetPath is null)
            .DistinctBy(reference => (
                reference.Location,
                reference.Name,
                reference.Kind));
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
        GscCallableCompletionContext context,
        bool hasExplicitQualifier)
    {
        if (hasExplicitQualifier || function.Location.Path == currentPath)
        {
            return new GscEditorCompletion(
                context.ReplacementStart,
                function.SourceName,
                function.DeclarationSignature,
                function.SourceName,
                function.Location.Path.Value,
                Kind: GscEditorCompletionKind.Function,
                Priority: function.Location.Path == currentPath ? 100 : 50);
        }

        string qualifiedPath = RemoveScriptExtension(
                function.Location.Path.Value)
            .Replace('/', '\\');
        return new GscEditorCompletion(
            context.ReplacementStart,
            $"{qualifiedPath}::{function.SourceName}",
            $"{qualifiedPath}::{function.DeclarationSignature}",
            function.SourceName,
            function.Location.Path.Value,
            Kind: GscEditorCompletionKind.Function);
    }

    private static GscEditorCompletion CreateObservedCompletion(
        GscCallableCompletionContext context,
        GscSymbolReference reference) =>
        new(
            context.ReplacementStart,
            reference.SourceName,
            $"{reference.SourceName}(…)",
            reference.SourceName,
            ObservedCallableDescription,
            Kind: GscEditorCompletionKind.ObservedFunction,
            Priority: 25);

    private static GscEditorCompletion CreateBuiltInCompletion(
        GscCallableCompletionContext context,
        Iw4GscBuiltInDefinition builtIn) =>
        new(
            context.ReplacementStart,
            builtIn.Name,
            builtIn.DisplaySignature,
            builtIn.Name,
            builtIn.Description,
            Kind: GscEditorCompletionKind.BuiltIn,
            Priority: 75);

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

    private static GscEditorSignature CreateBuiltInSignature(
        Iw4GscBuiltInDefinition builtIn,
        int activeParameter) =>
        new(
            builtIn.DisplaySignature,
            $"Argument {activeParameter + 1}; native registry parameter " +
            $"metadata is unavailable · Handler: {builtIn.NativeHandler}");

    private static Iw4GscBuiltInDefinition[] FindBuiltIns(
        GscWorkspaceSnapshot snapshot,
        string assetName,
        int sourceOffset)
    {
        GscScriptPath path = GscScriptPath.FromAssetName(assetName);
        GscIndexedDocument document = snapshot.Index.GetDocument(path);
        GscSymbolReference[] references = document.References
            .Where(reference =>
                reference.Kind == GscWorkspaceReferenceKind.Call &&
                reference.Targets.Count == 0 &&
                reference.QualifiedTargetPath is null &&
                Contains(reference.Location.Span, sourceOffset))
            .ToArray();
        if (references.Length == 0 && sourceOffset > 0)
        {
            references = document.References
                .Where(reference =>
                    reference.Kind == GscWorkspaceReferenceKind.Call &&
                    reference.Targets.Count == 0 &&
                    reference.QualifiedTargetPath is null &&
                    Contains(reference.Location.Span, sourceOffset - 1))
                .ToArray();
        }

        return references
            .SelectMany(reference => Iw4GscBuiltInCatalog.Multiplayer
                .FindCallablesByName(reference.Name))
            .Distinct()
            .ToArray();
    }

    private static bool Contains(GscTextSpan span, int offset) =>
        span.Length == 0
            ? offset == span.Start
            : offset >= span.Start && offset < span.End;

    private static string RemoveScriptExtension(string path) =>
        path.EndsWith(".gsc", StringComparison.Ordinal) ||
        path.EndsWith(".csc", StringComparison.Ordinal)
            ? path[..^4]
            : path;
}
