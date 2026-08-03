using IW4.Gsc.Analysis;

namespace IW4.Gsc.Workspace;

internal sealed record GscWorkspaceSourceDocument(
    GscDocumentSnapshot Snapshot,
    GscAnalysisResult Analysis,
    IReadOnlyList<GscSymbolDefinition> Definitions,
    IReadOnlyList<GscSymbolReference> References,
    IReadOnlyList<GscIncludeReference> Includes,
    IReadOnlyList<GscFunctionDefinition> Functions,
    IReadOnlyList<GscObservedField> ObservedFields,
    IReadOnlyList<GscPendingFunctionReference> FunctionReferences);

internal sealed record GscPendingFunctionReference(
    GscSourceLocation Location,
    string Name,
    string SourceName,
    GscWorkspaceReferenceKind Kind,
    GscScriptPath? QualifiedTarget);
