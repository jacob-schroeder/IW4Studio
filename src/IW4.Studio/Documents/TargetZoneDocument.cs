using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>Live ordered root rows for one editing session.</summary>
public sealed class TargetZoneDocument
{
    private readonly List<WorkspaceAssetCatalogEntry> _rows;
    private readonly Dictionary<TargetZoneRowIdentity, WorkspaceAssetCatalogEntry> _byIdentity;

    internal TargetZoneDocument(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentId = workspace.Document.DocumentId;
        _rows = workspace.AssetCatalog.TargetEntries.ToList();
        _byIdentity = _rows.ToDictionary(value => value.TargetRowIdentity!.Value);
    }

    public Guid DocumentId { get; }

    public IReadOnlyList<WorkspaceAssetCatalogEntry> Rows => _rows;

    public bool TryGetRow(
        TargetZoneRowIdentity identity,
        out WorkspaceAssetCatalogEntry? row) =>
        _byIdentity.TryGetValue(identity, out row);

    public WorkspaceAssetCatalogEntry GetRow(TargetZoneRowIdentity identity) =>
        TryGetRow(identity, out WorkspaceAssetCatalogEntry? row)
            ? row!
            : throw new KeyNotFoundException(
                $"Unknown target row {identity.SerializedIndex}.");

    internal WorkspaceAssetCatalogEntry AppendDefinition(
        IW4.Assets.Assets.BaseAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string name = definition.SerializedAssetName ?? throw new ArgumentException(
            "A new asset requires a serialized name.",
            nameof(definition));
        var identity = new TargetZoneRowIdentity(DocumentId, _rows.Count);
        var entry = new WorkspaceAssetCatalogEntry(
            identity,
            WorkspaceAssetOrigin.TargetOwnedDefinition,
            WorkspaceAssetAccess.Editable,
            WorkspaceAssetContentSource.TargetAuthoredBaseline,
            definition.SerializedAssetType,
            name,
            IW4.Linker.Contracts.AssetKey.FromDefinition(definition).NormalizedName,
            definition);
        _rows.Add(entry);
        _byIdentity.Add(identity, entry);
        return entry;
    }
}
