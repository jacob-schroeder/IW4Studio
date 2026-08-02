using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;

namespace IW4.Studio.MapEditor.Compilation.Validation;

/// <summary>
/// Reopens one flushed candidate and verifies the complete compiled-map
/// descriptor set against the exact isolated staging expectation. This is the
/// shared validator for single-family and combined map patches.
/// </summary>
internal sealed class CompiledMapCandidateValidator
    : ITransactionalSaveCandidateValidator
{
    private readonly FastFileWorkspace _sourceWorkspace;
    private readonly FastFileEditingSession _sourceEditingSession;
    private readonly long _expectedSourceEditingSessionRevision;
    private readonly long _expectedSourcePoolRevision;
    private readonly string _expectedBaselineDigest;
    private readonly CompiledMapBundle _baseline;
    private readonly CompiledMapCandidateExpectation _expectation;
    private readonly AssetAuthoringAdapterRegistry _adapters;
    private readonly FastFileDocumentService _documents;

    public CompiledMapCandidateValidator(
        FastFileWorkspace sourceWorkspace,
        FastFileEditingSession sourceEditingSession,
        long expectedSourceEditingSessionRevision,
        long expectedSourcePoolRevision,
        string expectedBaselineDigest,
        CompiledMapBundle baseline,
        CompiledMapCandidateExpectation expectation,
        AssetAuthoringAdapterRegistry? adapters = null,
        FastFileDocumentService? documents = null)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(sourceEditingSession);
        if (expectedSourceEditingSessionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedSourceEditingSessionRevision));
        }
        if (expectedSourcePoolRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedSourcePoolRevision));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedBaselineDigest);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(expectation);

        _sourceWorkspace = sourceWorkspace;
        _sourceEditingSession = sourceEditingSession;
        _expectedSourceEditingSessionRevision =
            expectedSourceEditingSessionRevision;
        _expectedSourcePoolRevision = expectedSourcePoolRevision;
        _expectedBaselineDigest = expectedBaselineDigest;
        _baseline = baseline;
        _expectation = expectation;
        _adapters =
            adapters ??
            AssetAuthoringAdapterRegistry.CreateDefault();
        _documents = documents ?? new FastFileDocumentService();
    }

    public IReadOnlyList<string> Validate(
        string candidatePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<string>();

        try
        {
            FastFileWorkspace reopened = _documents.Open(
                new FastFileDocumentOpenRequest(
                    candidatePath,
                    Isolated.Instance));
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSourceStillCurrent(
                diagnostics,
                cancellationToken);
            ValidateTargetShape(reopened, diagnostics);
            IReadOnlyDictionary<int, string> reopenedPayloadDigests =
                new Dictionary<int, string>();
            using (var reopenedSession =
                   new FastFileEditingSession(reopened))
            {
                ZoneBuildSnapshot reopenedSnapshot =
                    new ZoneBuildSnapshotBuilder(_adapters).Capture(
                        reopenedSession);
                if (!reopenedSnapshot.Validation.IsValid)
                {
                    foreach (ZoneBuildError error in
                             reopenedSnapshot.Validation.Errors)
                    {
                        diagnostics.Add(
                            "Reopened target semantic capture failed: " +
                            error);
                    }
                }
                else
                {
                    reopenedPayloadDigests = ValidateTargetSemantics(
                        reopenedSnapshot,
                        diagnostics,
                        cancellationToken);
                }
            }

            MapBundleResolutionResult resolution =
                new CompiledMapBundleResolver(_adapters).Resolve(
                    reopened,
                    sourceEditingSessionRevision: 0,
                    cancellationToken);
            if (!resolution.Succeeded)
            {
                diagnostics.Add(
                    "Could not resolve the reopened compiled-map bundle: " +
                    string.Join("; ", resolution.Diagnostics));
                return diagnostics;
            }

            ValidateDescriptorSet(
                resolution.Bundle!,
                reopenedPayloadDigests,
                diagnostics,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                "Could not reopen and verify the compiled-map candidate " +
                $"fastfile: {exception.Message}");
        }

        return diagnostics;
    }

    private IReadOnlyDictionary<int, string> ValidateTargetSemantics(
        ZoneBuildSnapshot reopened,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var payloadDigests = new Dictionary<int, string>();
        foreach (CompiledMapCandidateRowExpectation expected in
                 _expectation.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = expected.Index;
            if ((uint)index >= (uint)reopened.Rows.Count)
            {
                diagnostics.Add(
                    $"Reopened target has no expected staged row {index}.");
                continue;
            }
            ZoneBuildRow actual = reopened.Rows[index];
            CompiledMapCandidateRowKind? actualKind =
                Classify(actual);
            if (expected.Index != actual.Index ||
                expected.AssetType != actual.AssetType ||
                expected.RawHeader != actual.RawHeader ||
                expected.Kind != actualKind)
            {
                diagnostics.Add(
                    $"Reopened target row {index} changed serialized type, " +
                    "header, or semantic row classification.");
                continue;
            }

            if (expected.Kind !=
                    CompiledMapCandidateRowKind.OwnedDefinition)
            {
                continue;
            }
            if (actual is not OwnedDefinitionBuildRow owned ||
                expected.PayloadSemanticDigest is null)
            {
                diagnostics.Add(
                    $"Reopened owned target row {index} has no detached " +
                    "semantic payload.");
                continue;
            }

            string actualDigest =
                RelocationInvariantAssetSemanticDigest.Compute(
                    owned.BuildData,
                    cancellationToken);
            payloadDigests.Add(index, actualDigest);
            if (!string.Equals(
                    expected.PayloadSemanticDigest,
                    actualDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"Reopened target row {index} ({actual.AssetType}) does " +
                    "not match the exact staged semantic transaction.");
            }
        }
        return payloadDigests;
    }

    private static CompiledMapCandidateRowKind? Classify(
        ZoneBuildRow row) =>
        row switch
        {
            OwnedDefinitionBuildRow =>
                CompiledMapCandidateRowKind.OwnedDefinition,
            ExternalReferenceBuildRow =>
                CompiledMapCandidateRowKind.ExternalReference,
            NullBuildRow =>
                CompiledMapCandidateRowKind.Null,
            OpaqueNativeNoOpBuildRow =>
                CompiledMapCandidateRowKind.OpaqueNativeNoOp,
            _ => null
        };

    private void ValidateSourceStillCurrent(
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        long currentSessionRevision = _sourceEditingSession.Revision;
        if (currentSessionRevision !=
            _expectedSourceEditingSessionRevision)
        {
            diagnostics.Add(
                $"Source editing-session revision changed from " +
                $"{_expectedSourceEditingSessionRevision} to " +
                $"{currentSessionRevision}; the candidate omits newer " +
                "Studio edits.");
        }

        long currentPoolRevision =
            _sourceWorkspace.Runtime.AssetPool.Revision;
        if (currentPoolRevision != _expectedSourcePoolRevision)
        {
            diagnostics.Add(
                $"Source asset-pool revision changed from " +
                $"{_expectedSourcePoolRevision} to {currentPoolRevision}.");
        }

        string currentDigest =
            _baseline.ComputeCurrentBaselineDigest(cancellationToken);
        if (!string.Equals(
                currentDigest,
                _expectedBaselineDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The immutable compiled-map baseline changed while saving.");
        }
    }

    private void ValidateTargetShape(
        FastFileWorkspace reopened,
        ICollection<string> diagnostics)
    {
        IReadOnlyList<TargetZoneRowSource> sourceRows =
            _sourceWorkspace.TargetSource.Rows;
        IReadOnlyList<TargetZoneRowSource> reopenedRows =
            reopened.TargetSource.Rows;
        if (sourceRows.Count != reopenedRows.Count)
        {
            diagnostics.Add(
                $"Target-row count changed from {sourceRows.Count} to " +
                $"{reopenedRows.Count}.");
            return;
        }

        for (int index = 0; index < sourceRows.Count; index++)
        {
            TargetZoneRowSource source = sourceRows[index];
            TargetZoneRowSource candidate = reopenedRows[index];
            if (source.SerializedType != candidate.SerializedType ||
                source.State != candidate.State ||
                !string.Equals(
                    source.OriginalSerializedName,
                    candidate.OriginalSerializedName,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"Target row {index} changed serialized type, source " +
                    "form, or identity.");
            }
        }

        IReadOnlyList<TargetZoneScriptStringSource> sourceStrings =
            _sourceWorkspace.TargetSource.ScriptStrings;
        IReadOnlyList<TargetZoneScriptStringSource> reopenedStrings =
            reopened.TargetSource.ScriptStrings;
        if (sourceStrings.Count != reopenedStrings.Count)
        {
            diagnostics.Add(
                $"Script-string count changed from {sourceStrings.Count} to " +
                $"{reopenedStrings.Count}.");
            return;
        }
        for (int index = 0; index < sourceStrings.Count; index++)
        {
            if (sourceStrings[index].Index != reopenedStrings[index].Index ||
                !string.Equals(
                    sourceStrings[index].Value,
                    reopenedStrings[index].Value,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"Script string {index} was not preserved.");
            }
        }
    }

    private void ValidateDescriptorSet(
        CompiledMapBundle reopened,
        IReadOnlyDictionary<int, string> reopenedPayloadDigests,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        CompiledMapCandidateAssetExpectation[] expected =
            Order(_expectation.Assets);
        CompiledMapAssetDescriptor[] actual =
            Order(reopened.Assets);
        if (expected.Length != actual.Length)
        {
            diagnostics.Add(
                $"Reopened compiled-map asset count changed from " +
                $"{expected.Length} to {actual.Length}.");
            return;
        }

        for (int index = 0; index < expected.Length; index++)
        {
            CompiledMapCandidateAssetExpectation source =
                expected[index];
            CompiledMapAssetDescriptor candidate = actual[index];
            if (!SameIdentity(source, candidate))
            {
                diagnostics.Add(
                    $"Reopened compiled-map asset identity at preservation " +
                    $"slot {index} changed.");
                continue;
            }
            string payloadSemanticDigest;
            if (candidate.IsNested)
            {
                if (!reopened.TryGetBaseline<IXAssetBuildData>(
                        candidate.Kind,
                        out IXAssetBuildData? candidateSource) ||
                    candidateSource is null)
                {
                    diagnostics.Add(
                        $"Reopened {source.Kind} '{source.AssetName}' has no " +
                        "detached semantic source.");
                    continue;
                }
                payloadSemanticDigest =
                    RelocationInvariantAssetSemanticDigest.Compute(
                        candidateSource,
                        cancellationToken);
            }
            else if (!reopenedPayloadDigests.TryGetValue(
                         candidate.OwnerRow.SerializedIndex,
                         out payloadSemanticDigest!))
            {
                diagnostics.Add(
                    $"Reopened {source.Kind} '{source.AssetName}' owner row " +
                    "has no captured semantic payload.");
                continue;
            }
            var seed = new CompiledMapAssetDescriptorSeed(
                candidate.Kind,
                candidate.SerializedType,
                candidate.AssetName,
                candidate.OwnerRow,
                candidate.IsNested,
                candidate.SourcePath);
            string candidateDigest =
                CompiledMapRuntimeSemanticDigest.Compute(
                    reopened.MapIdentity,
                    seed,
                    payloadSemanticDigest,
                    cancellationToken);
            if (!string.Equals(
                    source.DescriptorSemanticDigest,
                    candidateDigest,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"Reopened {source.Kind} '{source.AssetName}' payload " +
                    "digest does not match the exact staged transaction.");
            }
        }
    }

    private static CompiledMapCandidateAssetExpectation[] Order(
        IEnumerable<CompiledMapCandidateAssetExpectation> assets) =>
        assets
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.OwnerRow.SerializedIndex)
            .ThenBy(value => value.SourcePath, StringComparer.Ordinal)
            .ToArray();

    private static CompiledMapAssetDescriptor[] Order(
        IEnumerable<CompiledMapAssetDescriptor> assets) =>
        assets
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.OwnerRow.SerializedIndex)
            .ThenBy(value => value.SourcePath, StringComparer.Ordinal)
            .ToArray();

    private static bool SameIdentity(
        CompiledMapCandidateAssetExpectation left,
        CompiledMapAssetDescriptor right) =>
        left.Kind == right.Kind &&
        left.SerializedType == right.SerializedType &&
        left.OwnerRow.SerializedIndex ==
            right.OwnerRow.SerializedIndex &&
        left.IsNested == right.IsNested &&
        string.Equals(
            left.AssetName,
            right.AssetName,
            StringComparison.Ordinal) &&
        string.Equals(
            left.SourcePath,
            right.SourcePath,
            StringComparison.Ordinal);
}
