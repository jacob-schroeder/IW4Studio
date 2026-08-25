using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

public enum AssetValidationSeverity
{
    Warning,
    Error
}

public sealed record AssetValidationIssue
{
    public AssetValidationIssue(
        string fieldPath,
        string message,
        AssetValidationSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        FieldPath = fieldPath;
        Message = message;
        Severity = severity;
    }

    public string FieldPath { get; }
    public string Message { get; }
    public AssetValidationSeverity Severity { get; }
}

/// <summary>
/// Stable workspace-local coordinate for one authored root occurrence.
/// It is intentionally independent from loader and linker allocation state.
/// </summary>
public readonly record struct TargetZoneRowIdentity
{
    public TargetZoneRowIdentity(Guid documentId, int serializedIndex)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(DocumentId));
        ArgumentOutOfRangeException.ThrowIfNegative(serializedIndex);
        DocumentId = documentId;
        SerializedIndex = serializedIndex;
    }

    public Guid DocumentId { get; }
    public int SerializedIndex { get; }
}

public enum AssetRowChangeKind
{
    Modified,
    Added
}

/// <summary>One deterministic unsaved change in the target-zone document.</summary>
public sealed record AssetRowChange(
    TargetZoneRowIdentity RowIdentity,
    XAssetType SerializedType,
    string? OriginalSerializedName,
    WorkspaceAssetOrigin Origin,
    long FirstChangedRevision,
    long LastChangedRevision,
    AssetRowChangeKind Kind = AssetRowChangeKind.Modified);

/// <summary>Immutable, row-ordered change state for destructive navigation.</summary>
public sealed class AssetChangeSet
{
    private readonly IReadOnlyList<AssetRowChange> _changes;
    private readonly IReadOnlyDictionary<TargetZoneRowIdentity, AssetRowChange>
        _byRow;

    internal AssetChangeSet(IEnumerable<AssetRowChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        AssetRowChange[] ordered = changes
            .OrderBy(change => change.RowIdentity.SerializedIndex)
            .ToArray();
        var byRow = new Dictionary<TargetZoneRowIdentity, AssetRowChange>();
        foreach (AssetRowChange change in ordered)
        {
            if (change.RowIdentity.DocumentId == Guid.Empty ||
                change.FirstChangedRevision <= 0 ||
                change.LastChangedRevision < change.FirstChangedRevision ||
                !byRow.TryAdd(change.RowIdentity, change))
            {
                throw new InvalidDataException(
                    "Asset changes must have unique rows and monotonic revisions.");
            }
        }

        _changes = Array.AsReadOnly(ordered);
        _byRow = byRow;
    }

    public bool IsEmpty => _changes.Count == 0;
    public int ChangedRowCount => _changes.Count;
    public IReadOnlyList<AssetRowChange> Changes => _changes;

    public bool TryGetChange(
        TargetZoneRowIdentity identity,
        out AssetRowChange? change) => _byRow.TryGetValue(identity, out change);
}

/// <summary>One detached authored definition from the current editing view.</summary>
public sealed record AppliedAssetDefinition(
    TargetZoneRowIdentity RowIdentity,
    IW4.Assets.Assets.BaseAsset Definition);

/// <summary>
/// Point-in-time authoring capture for live UI consumers. It is deliberately
/// separate from the canonical Save As link request.
/// </summary>
public sealed class AppliedAssetDefinitionsCapture
{
    internal AppliedAssetDefinitionsCapture(
        long revision,
        IEnumerable<AppliedAssetDefinition> definitions)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(definitions);
        Revision = revision;
        Definitions = Array.AsReadOnly(definitions.ToArray());
    }

    public long Revision { get; }
    public IReadOnlyList<AppliedAssetDefinition> Definitions { get; }
}

/// <summary>
/// Result of publishing one compiled D3DBSP asset group into the target
/// document as a single editing-session revision.
/// </summary>
public sealed class D3dbspWorkspaceImportResult
{
    internal D3dbspWorkspaceImportResult(
        long revision,
        string assetName,
        IEnumerable<WorkspaceAssetCatalogEntry> targetRows,
        int addedRowCount,
        int replacedRowCount,
        int discardedLightByteCount)
    {
        Revision = revision;
        AssetName = assetName;
        TargetRows = Array.AsReadOnly(targetRows.ToArray());
        AddedRowCount = addedRowCount;
        ReplacedRowCount = replacedRowCount;
        DiscardedLightByteCount = discardedLightByteCount;
    }

    public long Revision { get; }
    public string AssetName { get; }
    public IReadOnlyList<WorkspaceAssetCatalogEntry> TargetRows { get; }
    public int AddedRowCount { get; }
    public int ReplacedRowCount { get; }
    public int DiscardedLightByteCount { get; }
}

/// <summary>A detached, revision-consistent D3DBSP asset group.</summary>
public sealed class D3dbspWorkspaceAssetGroup
{
    internal D3dbspWorkspaceAssetGroup(
        long revision,
        string assetName,
        IEnumerable<IW4.Assets.Assets.BaseAsset> assets)
    {
        Revision = revision;
        AssetName = assetName;
        Assets = Array.AsReadOnly(assets.ToArray());
    }

    public long Revision { get; }
    public string AssetName { get; }
    public IReadOnlyList<IW4.Assets.Assets.BaseAsset> Assets { get; }
}
