using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Selection;

public sealed record WorkbenchAssetSelectionRoute(
    WorkspaceAssetCatalogEntry? CatalogEntry,
    bool OpensCatalogEditor,
    string? UnavailableReason)
{
    public bool IsResolved => CatalogEntry is not null;
}

/// <summary>
/// Resolves scalar navigator identities into the immutable workspace catalog.
/// Pool selections prefer their exact provider identity and canonicalize
/// serialized aliases only as a deterministic fallback.
/// </summary>
public sealed class WorkbenchAssetSelectionRouter
{
    private readonly IReadOnlyDictionary<TargetZoneRowIdentity, WorkspaceAssetCatalogEntry>
        _targetRows;
    private readonly IReadOnlyDictionary<XAssetProviderId, WorkspaceAssetCatalogEntry[]>
        _entriesByProvider;
    private readonly IReadOnlyDictionary<(XAssetType Type, string Name), WorkspaceAssetCatalogEntry[]>
        _entriesByCanonicalIdentity;

    public WorkbenchAssetSelectionRouter(WorkspaceAssetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _targetRows = catalog.TargetEntries
            .Where(entry => entry.TargetRowIdentity is not null)
            .ToDictionary(entry => entry.TargetRowIdentity!.Value);
        _entriesByProvider = catalog.Entries
            .Where(entry => entry.ResolvedProvider is not null)
            .GroupBy(entry => entry.ResolvedProvider!.ProviderId)
            .ToDictionary(
                group => group.Key,
                group => OrderCandidates(group).ToArray());
        _entriesByCanonicalIdentity = catalog.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.NormalizedName))
            .GroupBy(entry => (
                CanonicalType(entry.AssetType),
                entry.NormalizedName!))
            .ToDictionary(
                group => group.Key,
                group => OrderCandidates(group).ToArray());
    }

    public WorkbenchAssetSelectionRoute Resolve(
        WorkbenchAssetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Identity.TargetRowIdentity is { } targetIdentity)
        {
            return _targetRows.TryGetValue(
                targetIdentity,
                out WorkspaceAssetCatalogEntry? targetEntry)
                ? new WorkbenchAssetSelectionRoute(
                    targetEntry,
                    OpensCatalogEditor: true,
                    UnavailableReason: null)
                : Unavailable("The selected target row is not present in this workspace catalog.");
        }

        WorkspaceAssetCatalogEntry? entry = null;
        if (selection.ProviderId is { } providerId &&
            !providerId.IsNone &&
            _entriesByProvider.TryGetValue(
                providerId,
                out WorkspaceAssetCatalogEntry[]? providerEntries))
        {
            entry = ChooseForPoolSelection(providerEntries, selection);
        }

        if (entry is null)
        {
            _entriesByCanonicalIdentity.TryGetValue(
                (
                    CanonicalType(selection.AssetType),
                    selection.NormalizedName
                ),
                out WorkspaceAssetCatalogEntry[]? canonicalEntries);
            entry = ChooseForPoolSelection(canonicalEntries ?? [], selection);
        }

        if (entry is null)
        {
            return Unavailable(
                "This runtime pool slot has no matching catalog entry in the loaded workspace.");
        }

        bool opensEditor = entry.Access != WorkspaceAssetAccess.Editable;
        return new WorkbenchAssetSelectionRoute(
            entry,
            opensEditor,
            opensEditor
                ? null
                : "Runtime pool inspection is read-only. A dedicated runtime preview is not implemented for this target-owned asset yet.");
    }

    private static WorkbenchAssetSelectionRoute Unavailable(string reason) =>
        new(null, OpensCatalogEditor: false, reason);

    private static WorkspaceAssetCatalogEntry? ChooseForPoolSelection(
        IReadOnlyList<WorkspaceAssetCatalogEntry> candidates,
        WorkbenchAssetSelection selection) =>
        candidates
            .OrderBy(entry =>
                entry.AssetType == selection.AssetType ? 0 : 1)
            .ThenBy(entry =>
                entry.Access == WorkspaceAssetAccess.ReadOnly ? 0 : 1)
            .ThenBy(entry =>
                entry.Origin == WorkspaceAssetOrigin.DependencyOnly ? 0 : 1)
            .ThenBy(entry =>
                entry.TargetRowIdentity?.SerializedIndex ?? int.MaxValue)
            .FirstOrDefault();

    private static IEnumerable<WorkspaceAssetCatalogEntry> OrderCandidates(
        IEnumerable<WorkspaceAssetCatalogEntry> entries) =>
        entries
            .OrderBy(entry =>
                entry.Access == WorkspaceAssetAccess.ReadOnly ? 0 : 1)
            .ThenBy(entry =>
                entry.Origin == WorkspaceAssetOrigin.DependencyOnly ? 0 : 1)
            .ThenBy(entry =>
                entry.TargetRowIdentity?.SerializedIndex ?? int.MaxValue)
            .ThenBy(entry => entry.AssetType);

    private static XAssetType CanonicalType(XAssetType type) =>
        XAssetTypeRuntimeMetadataCatalog.TryGet(
            type,
            out XAssetTypeRuntimeMetadata? metadata)
            ? metadata!.CanonicalType
            : type;
}
