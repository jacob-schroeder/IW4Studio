using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

namespace IW4.Studio.Documents;

/// <summary>
/// The origin of a catalog entry. Target ownership and current provider
/// resolution are deliberately separate: a reference never becomes a target
/// definition merely because a dependency currently supplies its content.
/// </summary>
public enum WorkspaceAssetOrigin
{
    TargetOwnedDefinition,
    TargetResolvedReference,
    TargetUnresolvedReference,
    DependencyOnly,
    OffsetAliasRow,
    NullRow,
    OpaqueRow,
    UnsupportedRow
}

/// <summary>
/// Policy result for a catalog entry. This is an entitlement, not a claim that
/// an editor or compiler is implemented for the asset type.
/// </summary>
public enum WorkspaceAssetAccess
{
    Editable,
    ReadOnly,
    ContentUnavailable
}

/// <summary>
/// Describes where the catalog can obtain inspectable content without
/// changing the row's ownership or serialization classification.
/// </summary>
public enum WorkspaceAssetContentSource
{
    TargetAuthoredBaseline,
    ResolvedProvider,
    StructuralOnly,
    Unavailable
}

/// <summary>
/// Stable, workspace-local identity for dependency-only content. It never
/// participates in target serialization; target entries use
/// <see cref="TargetZoneRowIdentity"/> instead.
/// </summary>
public readonly record struct WorkspaceDependencyAssetIdentity
{
    public WorkspaceDependencyAssetIdentity(Guid documentId, XAssetProviderId providerId)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (providerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(providerId));

        DocumentId = documentId;
        ProviderId = providerId;
    }

    public Guid DocumentId { get; }

    public XAssetProviderId ProviderId { get; }
}

/// <summary>
/// Immutable scalar description of a zone that owns a runtime provider.
/// Zone names are optional for unowned compatibility registrations; the
/// handle remains the owner identity when one exists.
/// </summary>
public sealed record WorkspaceAssetProviderZone(
    DbZoneHandle Handle,
    string? LogicalZoneName);

/// <summary>
/// Copy of the current runtime provider for inspection. It retains no
/// runtime asset object, pool address, staging address, or source memory.
/// </summary>
public sealed record WorkspaceAssetResolvedProvider(
    XAssetProviderId ProviderId,
    WorkspaceAssetProviderZone Zone);

/// <summary>
/// The single authority for catalog access decisions. Editor and compiler
/// capability are supplied to entries separately and never change this
/// matrix.
/// </summary>
public static class WorkspaceAssetAccessPolicy
{
    public static WorkspaceAssetAccess Decide(
        WorkspaceAssetOrigin origin,
        bool hasResolvedProvider)
    {
        return origin switch
        {
            WorkspaceAssetOrigin.TargetOwnedDefinition =>
                WorkspaceAssetAccess.Editable,
            WorkspaceAssetOrigin.TargetResolvedReference when hasResolvedProvider =>
                WorkspaceAssetAccess.ReadOnly,
            WorkspaceAssetOrigin.TargetUnresolvedReference when !hasResolvedProvider =>
                WorkspaceAssetAccess.ContentUnavailable,
            WorkspaceAssetOrigin.DependencyOnly when hasResolvedProvider =>
                WorkspaceAssetAccess.ReadOnly,
            WorkspaceAssetOrigin.OffsetAliasRow or
            WorkspaceAssetOrigin.NullRow or
            WorkspaceAssetOrigin.OpaqueRow =>
                WorkspaceAssetAccess.ReadOnly,
            WorkspaceAssetOrigin.UnsupportedRow =>
                WorkspaceAssetAccess.ContentUnavailable,
            WorkspaceAssetOrigin.TargetResolvedReference => throw new InvalidDataException(
                "A resolved target reference must have a full-definition provider."),
            WorkspaceAssetOrigin.TargetUnresolvedReference => throw new InvalidDataException(
                "An unresolved target reference cannot expose a full-definition provider."),
            WorkspaceAssetOrigin.DependencyOnly => throw new InvalidDataException(
                "A dependency-only catalog entry must have a full-definition provider."),
            _ => throw new InvalidDataException(
                $"Catalog origin '{origin}' has contradictory provider state.")
        };
    }
}

/// <summary>
/// One backend-neutral catalog entry. Exactly one of
/// <see cref="TargetRowIdentity"/> and <see cref="DependencyIdentity"/> is
/// populated. A target row remains represented even after its runtime zone is
/// retired or shadowed.
/// </summary>
public sealed class WorkspaceAssetCatalogEntry
{
    internal WorkspaceAssetCatalogEntry(
        TargetZoneRowSource? targetRow,
        WorkspaceDependencyAssetIdentity? dependencyIdentity,
        WorkspaceAssetOrigin origin,
        WorkspaceAssetAccess access,
        WorkspaceAssetContentSource contentSource,
        XAssetType assetType,
        string? originalName,
        string? normalizedName,
        WorkspaceAssetResolvedProvider? resolvedProvider)
    {
        if ((targetRow is null) == (dependencyIdentity is null))
        {
            throw new ArgumentException(
                "A catalog entry must identify exactly one target row or dependency provider.");
        }

        TargetRow = targetRow;
        DependencyIdentity = dependencyIdentity;
        Origin = origin;
        Access = access;
        ContentSource = contentSource;
        AssetType = assetType;
        OriginalName = originalName;
        NormalizedName = normalizedName;
        ResolvedProvider = resolvedProvider;
    }

    /// <summary>Target serialization identity, present only for target rows.</summary>
    public TargetZoneRowIdentity? TargetRowIdentity => TargetRow?.Identity;

    /// <summary>Workspace-local identity, present only for dependency content.</summary>
    public WorkspaceDependencyAssetIdentity? DependencyIdentity { get; }

    /// <summary>
    /// Detached target source row. This is the sole ownership authority for a
    /// target entry and is null for dependency-only content.
    /// </summary>
    public TargetZoneRowSource? TargetRow { get; }

    public WorkspaceAssetOrigin Origin { get; }

    public WorkspaceAssetAccess Access { get; }

    public WorkspaceAssetContentSource ContentSource { get; }

    /// <summary>
    /// Serialized target type for target rows, or the canonical provider type
    /// for dependency-only content.
    /// </summary>
    public XAssetType AssetType { get; }

    /// <summary>
    /// Original target spelling when supplied by the row; dependency entries
    /// retain their provider spelling. This is never normalized in place.
    /// </summary>
    public string? OriginalName { get; }

    /// <summary>Normalized only for deterministic lookup and projection.</summary>
    public string? NormalizedName { get; }

    /// <summary>
    /// Original serialized header classification for target rows. Dependency
    /// content has no target header and therefore returns null.
    /// </summary>
    public int? RawHeader => TargetRow?.RawHeader;

    /// <summary>
    /// Original serialized header kind for target rows. Dependency content has
    /// no target header and therefore returns null.
    /// </summary>
    public XAssetHeaderKind? HeaderKind => TargetRow?.HeaderKind;

    /// <summary>
    /// Active full-definition provider resolved after the complete load plan.
    /// It is inspection metadata only and never grants target ownership.
    /// </summary>
    public WorkspaceAssetResolvedProvider? ResolvedProvider { get; }

    public WorkspaceAssetProviderZone? ResolvedProviderZone => ResolvedProvider?.Zone;

    public DbZoneHandle? ProviderZone => ResolvedProvider?.Zone.Handle;
}

/// <summary>
/// Immutable catalog of exact target rows plus active dependency-only
/// content. The snapshot authority is never inferred from the pool's active
/// provider, and pool data is copied only as scalar view-resolution metadata.
/// </summary>
public sealed class WorkspaceAssetCatalog
{
    private readonly IReadOnlyList<WorkspaceAssetCatalogEntry> _targetEntries;
    private readonly IReadOnlyList<WorkspaceAssetCatalogEntry> _dependencyEntries;
    private readonly IReadOnlyList<WorkspaceAssetCatalogEntry> _entries;

    private WorkspaceAssetCatalog(
        IEnumerable<WorkspaceAssetCatalogEntry> targetEntries,
        IEnumerable<WorkspaceAssetCatalogEntry> dependencyEntries)
    {
        _targetEntries = Array.AsReadOnly(targetEntries.ToArray());
        _dependencyEntries = Array.AsReadOnly(dependencyEntries.ToArray());
        _entries = Array.AsReadOnly([.. _targetEntries, .. _dependencyEntries]);
    }

    /// <summary>
    /// Creates an immutable catalog snapshot after a document's complete load
    /// plan. The pool is read only; it supplies current provider resolution
    /// and dependency-only content but never target ownership.
    /// </summary>
    public static WorkspaceAssetCatalog Create(
        TargetZoneSourceSnapshot targetSource,
        XAssetPool assetPool,
        IEnumerable<WorkspaceAssetProviderZone>? providerZones = null)
    {
        ArgumentNullException.ThrowIfNull(targetSource);
        ArgumentNullException.ThrowIfNull(assetPool);

        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> zones =
            BuildProviderZoneIndex(providerZones ?? []);
        XAssetSlot[] slots = assetPool.Slots.ToArray();
        var slotsByIdentity = new Dictionary<CanonicalAssetIdentity, XAssetSlot>();
        foreach (XAssetSlot slot in slots)
        {
            var identity = new CanonicalAssetIdentity(
                slot.AssetType,
                XAssetStableIdentity.NormalizeLookupName(slot.Name));
            if (!slotsByIdentity.TryAdd(identity, slot))
            {
                throw new InvalidDataException(
                    $"Runtime asset pool has duplicate canonical slot identity {identity.AssetType} '{identity.NormalizedName}'.");
            }
        }

        TargetZoneRowSource[] rows = targetSource.Rows.ToArray();
        ValidateTargetRows(targetSource, rows);
        var targetIdentities = new HashSet<CanonicalAssetIdentity>(
            rows.Where(row => row.StableIdentity is not null)
                .Select(row => CanonicalAssetIdentity.From(row.StableIdentity!.Value)));

        WorkspaceAssetCatalogEntry[] targetEntries = rows
            .Select(row => CreateTargetEntry(row, slotsByIdentity, zones))
            .ToArray();
        WorkspaceAssetCatalogEntry[] dependencyEntries = slots
            .Select(slot => slot.ActiveProvider)
            .Where(provider => !provider.IsReferencePlaceholder)
            .Where(provider => !targetIdentities.Contains(new CanonicalAssetIdentity(
                provider.AssetType,
                XAssetStableIdentity.NormalizeLookupName(provider.Name))))
            .Select(provider => CreateDependencyEntry(targetSource.DocumentId, provider, zones))
            .OrderBy(entry => entry.AssetType)
            .ThenBy(entry => entry.NormalizedName, StringComparer.Ordinal)
            .ThenBy(entry => entry.ResolvedProvider!.ProviderId.Value)
            .ToArray();

        return new WorkspaceAssetCatalog(targetEntries, dependencyEntries);
    }

    /// <summary>
    /// Exact target serialization rows in captured order. Search, sort, and
    /// grouping never mutate this sequence.
    /// </summary>
    public IReadOnlyList<WorkspaceAssetCatalogEntry> TargetEntries => _targetEntries;

    /// <summary>Active content that has no matching target-row identity.</summary>
    public IReadOnlyList<WorkspaceAssetCatalogEntry> DependencyEntries => _dependencyEntries;

    /// <summary>
    /// Deterministic catalog order: exact target rows first, followed by the
    /// sorted dependency-only projection.
    /// </summary>
    public IReadOnlyList<WorkspaceAssetCatalogEntry> Entries => _entries;

    private static WorkspaceAssetCatalogEntry CreateTargetEntry(
        TargetZoneRowSource row,
        IReadOnlyDictionary<CanonicalAssetIdentity, XAssetSlot> slotsByIdentity,
        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> zones)
    {
        WorkspaceAssetOrigin origin;
        WorkspaceAssetContentSource contentSource;
        WorkspaceAssetResolvedProvider? resolvedProvider = null;
        switch (row.State)
        {
            case TargetZoneRowSourceState.Definition:
                RequireAuthoredDefinition(row);
                origin = WorkspaceAssetOrigin.TargetOwnedDefinition;
                contentSource = WorkspaceAssetContentSource.TargetAuthoredBaseline;
                resolvedProvider = TryResolveFullProvider(row.StableIdentity, slotsByIdentity, zones);
                break;

            case TargetZoneRowSourceState.ResolvedReference:
            case TargetZoneRowSourceState.UnresolvedReference:
                RequireReference(row);
                resolvedProvider = TryResolveFullProvider(
                    row.ExternalReference!.Identity,
                    slotsByIdentity,
                    zones);
                origin = resolvedProvider is null
                    ? WorkspaceAssetOrigin.TargetUnresolvedReference
                    : WorkspaceAssetOrigin.TargetResolvedReference;
                contentSource = resolvedProvider is null
                    ? WorkspaceAssetContentSource.Unavailable
                    : WorkspaceAssetContentSource.ResolvedProvider;
                break;

            case TargetZoneRowSourceState.OffsetAlias:
                origin = WorkspaceAssetOrigin.OffsetAliasRow;
                contentSource = WorkspaceAssetContentSource.StructuralOnly;
                break;

            case TargetZoneRowSourceState.Null:
                origin = WorkspaceAssetOrigin.NullRow;
                contentSource = WorkspaceAssetContentSource.StructuralOnly;
                break;

            case TargetZoneRowSourceState.OpaqueNativeNoOp:
                origin = WorkspaceAssetOrigin.OpaqueRow;
                contentSource = WorkspaceAssetContentSource.StructuralOnly;
                break;

            case TargetZoneRowSourceState.Unsupported:
                origin = WorkspaceAssetOrigin.UnsupportedRow;
                contentSource = WorkspaceAssetContentSource.Unavailable;
                break;

            default:
                throw new InvalidDataException(
                    $"Target row {row.Identity.SerializedIndex} has unsupported catalog state '{row.State}'.");
        }

        WorkspaceAssetAccess access = WorkspaceAssetAccessPolicy.Decide(
            origin,
            resolvedProvider is not null);
        return new WorkspaceAssetCatalogEntry(
            row,
            dependencyIdentity: null,
            origin,
            access,
            contentSource,
            row.SerializedType,
            row.OriginalSerializedName,
            row.NormalizedKey,
            resolvedProvider);
    }

    private static WorkspaceAssetCatalogEntry CreateDependencyEntry(
        Guid documentId,
        XAssetProviderContribution provider,
        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> zones)
    {
        if (provider.IsReferencePlaceholder)
        {
            throw new InvalidDataException(
                $"Reference placeholder provider {provider.Id} cannot be cataloged as dependency content.");
        }

        WorkspaceAssetResolvedProvider resolvedProvider = CreateResolvedProvider(provider, zones);
        WorkspaceAssetAccess access = WorkspaceAssetAccessPolicy.Decide(
            WorkspaceAssetOrigin.DependencyOnly,
            hasResolvedProvider: true);
        return new WorkspaceAssetCatalogEntry(
            targetRow: null,
            new WorkspaceDependencyAssetIdentity(documentId, provider.Id),
            WorkspaceAssetOrigin.DependencyOnly,
            access,
            WorkspaceAssetContentSource.ResolvedProvider,
            provider.AssetType,
            provider.Name,
            XAssetStableIdentity.NormalizeLookupName(provider.Name),
            resolvedProvider);
    }

    private static WorkspaceAssetResolvedProvider? TryResolveFullProvider(
        XAssetStableIdentity? identity,
        IReadOnlyDictionary<CanonicalAssetIdentity, XAssetSlot> slotsByIdentity,
        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> zones)
    {
        if (identity is null ||
            !slotsByIdentity.TryGetValue(CanonicalAssetIdentity.From(identity.Value), out XAssetSlot? slot))
        {
            return null;
        }

        XAssetProviderContribution provider = slot.ActiveProvider;
        if (provider.IsReferencePlaceholder)
            return null;

        CanonicalAssetIdentity expected = CanonicalAssetIdentity.From(identity.Value);
        CanonicalAssetIdentity actual = new(
            provider.AssetType,
            XAssetStableIdentity.NormalizeLookupName(provider.Name));
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Active provider {provider.Id} contradicts canonical identity '{expected.AssetType}' '{expected.NormalizedName}'.");
        }

        return CreateResolvedProvider(provider, zones);
    }

    private static WorkspaceAssetResolvedProvider CreateResolvedProvider(
        XAssetProviderContribution provider,
        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> zones)
    {
        if (provider.Id.IsNone)
            throw new InvalidDataException("Runtime provider content has no provider identity.");

        WorkspaceAssetProviderZone zone;
        if (provider.Owner.IsNone)
        {
            zone = new WorkspaceAssetProviderZone(provider.Owner, LogicalZoneName: null);
        }
        else if (!zones.TryGetValue(provider.Owner, out WorkspaceAssetProviderZone? knownZone))
        {
            throw new InvalidDataException(
                $"Active provider {provider.Id} is owned by {provider.Owner}, " +
                "which is absent from the workspace zone set.");
        }
        else
        {
            zone = knownZone;
        }

        return new WorkspaceAssetResolvedProvider(
            provider.Id,
            zone);
    }

    private static IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> BuildProviderZoneIndex(
        IEnumerable<WorkspaceAssetProviderZone> providerZones)
    {
        var result = new Dictionary<DbZoneHandle, WorkspaceAssetProviderZone>();
        foreach (WorkspaceAssetProviderZone zone in providerZones)
        {
            ArgumentNullException.ThrowIfNull(zone);
            if (zone.Handle.IsNone)
                continue;
            if (!result.TryAdd(zone.Handle, zone))
            {
                throw new InvalidDataException(
                    $"Provider zone handle {zone.Handle} appears more than once in the workspace.");
            }
        }

        return result;
    }

    private static void ValidateTargetRows(
        TargetZoneSourceSnapshot targetSource,
        IReadOnlyList<TargetZoneRowSource> rows)
    {
        var identities = new HashSet<TargetZoneRowIdentity>();
        for (int index = 0; index < rows.Count; index++)
        {
            TargetZoneRowSource row = rows[index]
                ?? throw new InvalidDataException($"Target source row {index} is missing.");
            if (row.Identity.DocumentId != targetSource.DocumentId ||
                row.Identity.SerializedIndex != index ||
                !identities.Add(row.Identity))
            {
                throw new InvalidDataException(
                    "Target source rows do not retain unique, contiguous document row identities.");
            }
        }
    }

    private static void RequireAuthoredDefinition(TargetZoneRowSource row)
    {
        if (row.AuthoredDefinition is null ||
            row.StableIdentity is null)
        {
            throw new InvalidDataException(
                $"Target definition row {row.Identity.SerializedIndex} has incomplete authored data.");
        }
    }

    private static void RequireReference(TargetZoneRowSource row)
    {
        if (row.ExternalReference is null ||
            row.StableIdentity is null ||
            row.ExternalReference.Identity != row.StableIdentity.Value)
        {
            throw new InvalidDataException(
                $"Target reference row {row.Identity.SerializedIndex} has incomplete reference data.");
        }
    }

    private readonly record struct CanonicalAssetIdentity(
        XAssetType AssetType,
        string NormalizedName)
    {
        public static CanonicalAssetIdentity From(XAssetStableIdentity identity) =>
            new(identity.CanonicalFamily, identity.NormalizedName);
    }
}
