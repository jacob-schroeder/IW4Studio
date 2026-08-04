using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents.AssetReferences;

/// <summary>
/// Scalar, editor-safe description of an asset that can be selected as a
/// symbolic reference. It deliberately retains no runtime asset or pool
/// object.
/// </summary>
public sealed record WorkspaceAssetReferenceCandidate(
    XAssetType AssetType,
    string Name,
    string NormalizedName,
    WorkspaceAssetOrigin Origin,
    WorkspaceAssetAccess Access,
    string? ProviderZone,
    TargetZoneRowIdentity? TargetRowIdentity)
{
    public bool IsResolved => Access != WorkspaceAssetAccess.ContentUnavailable;

    public bool IsEditableTarget => Access == WorkspaceAssetAccess.Editable;
}

/// <summary>
/// Produces current reference-picker candidates from the mutable target
/// document plus the immutable dependency catalog. Capturing on demand keeps
/// newly added target assets visible without making the catalog itself
/// stateful or UI-aware.
/// </summary>
public sealed class WorkspaceAssetReferenceCatalog
{
    private readonly FastFileEditingSession _editingSession;

    public WorkspaceAssetReferenceCatalog(FastFileEditingSession editingSession)
    {
        _editingSession = editingSession ??
            throw new ArgumentNullException(nameof(editingSession));
    }

    public IReadOnlyList<WorkspaceAssetReferenceCandidate> Capture(
        XAssetType assetType)
    {
        if (!Enum.IsDefined(assetType))
            throw new ArgumentOutOfRangeException(nameof(assetType));

        WorkspaceAssetCatalogEntry[] entries =
        [
            .. _editingSession.Document.Rows.Where(entry =>
                entry.AssetType == assetType),
            .. _editingSession.Workspace.AssetCatalog.DependencyEntries.Where(
                entry => entry.AssetType == assetType)
        ];

        return Array.AsReadOnly(entries
            .Select(TryCreateCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => candidate.NormalizedName, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(CandidatePriority)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .First())
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray());
    }

    public WorkspaceAssetReferenceCandidate? Find(
        XAssetType assetType,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string normalized = XAssetStableIdentity.NormalizeLookupName(name);
        return Capture(assetType).FirstOrDefault(candidate =>
            string.Equals(
                candidate.NormalizedName,
                normalized,
                StringComparison.Ordinal));
    }

    private static WorkspaceAssetReferenceCandidate? TryCreateCandidate(
        WorkspaceAssetCatalogEntry entry)
    {
        string? name = string.IsNullOrWhiteSpace(entry.OriginalName)
            ? entry.NormalizedName
            : entry.OriginalName;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = XAssetStableIdentity.GetLookupSpelling(name);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new WorkspaceAssetReferenceCandidate(
            entry.AssetType,
            name,
            XAssetStableIdentity.NormalizeLookupName(name),
            entry.Origin,
            entry.Access,
            entry.ResolvedProviderZone?.LogicalZoneName,
            entry.TargetRowIdentity);
    }

    private static int CandidatePriority(
        WorkspaceAssetReferenceCandidate candidate) => candidate switch
    {
        { IsEditableTarget: true } => 0,
        { TargetRowIdentity: not null, IsResolved: true } => 1,
        { Origin: WorkspaceAssetOrigin.DependencyOnly } => 2,
        { TargetRowIdentity: not null } => 3,
        _ => 4
    };
}
