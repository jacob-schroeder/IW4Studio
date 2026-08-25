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
        return AppendDefinitions([definition])[0];
    }

    internal IReadOnlyList<WorkspaceAssetCatalogEntry> AppendDefinitions(
        IEnumerable<IW4.Assets.Assets.BaseAsset> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        IW4.Assets.Assets.BaseAsset[] requested = definitions
            .Select(definition => definition ?? throw new ArgumentException(
                "New asset definitions cannot contain null.",
                nameof(definitions)))
            .ToArray();
        var entries = new WorkspaceAssetCatalogEntry[requested.Length];
        for (int index = 0; index < requested.Length; index++)
        {
            IW4.Assets.Assets.BaseAsset definition = requested[index];
            string name = definition.SerializedAssetName ?? throw new ArgumentException(
                "A new asset requires a serialized name.",
                nameof(definitions));
            var identity = new TargetZoneRowIdentity(
                DocumentId,
                checked(_rows.Count + index));
            entries[index] = new WorkspaceAssetCatalogEntry(
                identity,
                WorkspaceAssetOrigin.TargetOwnedDefinition,
                WorkspaceAssetAccess.Editable,
                WorkspaceAssetContentSource.TargetAuthoredBaseline,
                definition.SerializedAssetType,
                name,
                IW4.Linker.Contracts.AssetKey.FromDefinition(definition).NormalizedName,
                definition);
        }

        foreach (WorkspaceAssetCatalogEntry entry in entries)
        {
            _rows.Add(entry);
            _byIdentity.Add(entry.TargetRowIdentity!.Value, entry);
        }

        return Array.AsReadOnly(entries);
    }
}
