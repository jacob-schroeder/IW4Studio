using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.Documents;

namespace IW4.Studio.MapEditor.Compilation.Validation;

/// <summary>
/// Final pre-commit concurrency guard for the two mutable authoring
/// authorities consumed by a compiled-map save.
/// </summary>
internal sealed class MapSaveRevisionCandidateValidator(
    EditorMapDocument document,
    long expectedDocumentRevision,
    FastFileEditingSession editingSession,
    long expectedEditingSessionRevision,
    long expectedSourcePoolRevision,
    CompiledMapBundle baseline,
    string expectedBaselineDigest)
    : ITransactionalSaveCandidateValidator
{
    private readonly EditorMapDocument _document =
        document ?? throw new ArgumentNullException(nameof(document));
    private readonly FastFileEditingSession _editingSession =
        editingSession ??
        throw new ArgumentNullException(nameof(editingSession));
    private readonly long _expectedDocumentRevision =
        expectedDocumentRevision >= 0
            ? expectedDocumentRevision
            : throw new ArgumentOutOfRangeException(
                nameof(expectedDocumentRevision));
    private readonly long _expectedEditingSessionRevision =
        expectedEditingSessionRevision >= 0
            ? expectedEditingSessionRevision
            : throw new ArgumentOutOfRangeException(
                nameof(expectedEditingSessionRevision));
    private readonly long _expectedSourcePoolRevision =
        expectedSourcePoolRevision >= 0
            ? expectedSourcePoolRevision
            : throw new ArgumentOutOfRangeException(
                nameof(expectedSourcePoolRevision));
    private readonly CompiledMapBundle _baseline =
        baseline ?? throw new ArgumentNullException(nameof(baseline));
    private readonly string _expectedBaselineDigest =
        !string.IsNullOrWhiteSpace(expectedBaselineDigest)
            ? expectedBaselineDigest
            : throw new ArgumentException(
                "A baseline digest is required.",
                nameof(expectedBaselineDigest));

    public IReadOnlyList<string> Validate(
        string candidatePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<string>(4);

        long documentRevision = _document.Revision;
        if (documentRevision != _expectedDocumentRevision)
        {
            diagnostics.Add(
                $"Map document revision changed from " +
                $"{_expectedDocumentRevision} to {documentRevision} after " +
                "candidate compilation; discard it and retry.");
        }

        long sessionRevision = _editingSession.Revision;
        if (sessionRevision != _expectedEditingSessionRevision)
        {
            diagnostics.Add(
                $"Source editing-session revision changed from " +
                $"{_expectedEditingSessionRevision} to {sessionRevision} " +
                "after candidate compilation; discard it and retry.");
        }

        long poolRevision =
            _editingSession.Workspace.Runtime.AssetPool.Revision;
        if (poolRevision != _expectedSourcePoolRevision)
        {
            diagnostics.Add(
                $"Source asset-pool revision changed from " +
                $"{_expectedSourcePoolRevision} to {poolRevision} after " +
                "candidate compilation; discard it and retry.");
        }

        string baselineDigest =
            _baseline.ComputeCurrentBaselineDigest(cancellationToken);
        if (!string.Equals(
                baselineDigest,
                _expectedBaselineDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The immutable compiled-map baseline changed after candidate " +
                "compilation; discard it and retry.");
        }

        return diagnostics;
    }
}
