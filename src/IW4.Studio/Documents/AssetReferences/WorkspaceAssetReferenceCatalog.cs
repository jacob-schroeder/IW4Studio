using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents.AssetReferences;

/// <summary>
/// Scalar, editor-safe description of one selectable symbolic asset
/// reference. It retains no runtime asset or pool object.
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
/// Captures picker candidates from the current target document, its single
/// workspace catalog, and the frozen linker provider pool. Captures are
/// scalar so a picker cannot retain a loader or runtime provider.
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

        LinkAssetProvider[] currentProviders = _editingSession.LinkRequest.Assets
            .Providers
            .Where(provider => provider.SerializedType == assetType)
            .ToArray();
        Dictionary<AssetKey, LinkAssetProvider> selectedFullProviders =
            currentProviders
                .Where(provider => !provider.IsReferencePlaceholder)
                .GroupBy(provider => provider.Key)
                .ToDictionary(group => group.Key, group => group.First());
        var initialProviders = new HashSet<LinkAssetProvider>(
            _editingSession.Workspace.InitialLinkRequest.Assets.Providers,
            ReferenceEqualityComparer.Instance);
        var candidates = new List<WorkspaceAssetReferenceCandidate>();
        foreach (WorkspaceAssetCatalogEntry entry in _editingSession.Document.Rows)
        {
            if (entry.AssetType == assetType &&
                TryGetKey(entry, out AssetKey targetKey) &&
                TryCreateTargetCandidate(
                    entry,
                    selectedFullProviders.TryGetValue(
                        targetKey,
                        out LinkAssetProvider? targetProvider),
                    targetProvider is not null &&
                        initialProviders.Contains(targetProvider)) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        foreach (WorkspaceAssetCatalogEntry entry in
                 _editingSession.Workspace.AssetCatalog.DependencyEntries)
        {
            if (entry.AssetType == assetType &&
                (entry.ProviderZone?.IsTarget != true ||
                    EntryRetainsInitialTargetProvider(
                        entry,
                        selectedFullProviders,
                        initialProviders)) &&
                TryCreateCatalogCandidate(entry) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        foreach (LinkAssetProvider provider in currentProviders)
            candidates.Add(CreateProviderCandidate(provider));

        return Array.AsReadOnly(candidates
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

        string normalized = Normalize(assetType, name);
        return Capture(assetType).FirstOrDefault(candidate =>
            string.Equals(candidate.NormalizedName, normalized,
                StringComparison.Ordinal));
    }

    private static WorkspaceAssetReferenceCandidate? TryCreateTargetCandidate(
        WorkspaceAssetCatalogEntry entry,
        bool hasFullProvider,
        bool retainsInitialProvider)
    {
        bool targetProviderReference = entry.Origin ==
                WorkspaceAssetOrigin.TargetResolvedReference &&
            entry.ProviderZone?.IsTarget == true;
        bool hasRequiredProvider = targetProviderReference
            ? retainsInitialProvider
            : hasFullProvider;
        bool requiresFullProvider = entry.Origin ==
                WorkspaceAssetOrigin.TargetOwnedDefinition ||
            targetProviderReference;
        WorkspaceAssetAccess access = requiresFullProvider && !hasRequiredProvider
            ? WorkspaceAssetAccess.ContentUnavailable
            : entry.Access;
        WorkspaceAssetOrigin origin = targetProviderReference &&
            !hasRequiredProvider
                ? WorkspaceAssetOrigin.TargetUnresolvedReference
                : entry.Origin;
        return TryCreateCandidate(
            entry.AssetType,
            entry.OriginalName ?? entry.NormalizedName,
            origin,
            access,
            targetProviderReference && !hasRequiredProvider
                ? null
                : entry.ResolvedProviderZone?.LogicalZoneName,
            entry.TargetRowIdentity);
    }

    private static WorkspaceAssetReferenceCandidate? TryCreateCatalogCandidate(
        WorkspaceAssetCatalogEntry entry) => TryCreateCandidate(
            entry.AssetType,
            entry.OriginalName ?? entry.NormalizedName,
            entry.Origin,
            entry.Access,
            entry.ResolvedProviderZone?.LogicalZoneName,
            entry.TargetRowIdentity);

    private static WorkspaceAssetReferenceCandidate CreateProviderCandidate(
        LinkAssetProvider provider)
    {
        return TryCreateCandidate(
            provider.SerializedType,
            provider.OriginalSerializedName,
            WorkspaceAssetOrigin.DependencyOnly,
            provider.IsReferencePlaceholder
                ? WorkspaceAssetAccess.ContentUnavailable
                : WorkspaceAssetAccess.ReadOnly,
            providerZone: null,
            targetRowIdentity: null) ?? throw new InvalidDataException(
                "The linker provider pool exposed a definition with no selectable name.");
    }

    private static WorkspaceAssetReferenceCandidate? TryCreateCandidate(
        XAssetType assetType,
        string? name,
        WorkspaceAssetOrigin origin,
        WorkspaceAssetAccess access,
        string? providerZone,
        TargetZoneRowIdentity? targetRowIdentity)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string spelling = name.Length > 0 && name[0] == ','
            ? name[1..]
            : name;
        if (string.IsNullOrWhiteSpace(spelling))
            return null;

        return new WorkspaceAssetReferenceCandidate(
            assetType,
            spelling,
            Normalize(assetType, spelling),
            origin,
            access,
            providerZone,
            targetRowIdentity);
    }

    private static int CandidatePriority(
        WorkspaceAssetReferenceCandidate candidate) => candidate switch
    {
        { IsEditableTarget: true } => 0,
        { TargetRowIdentity: not null, IsResolved: true } => 1,
        { Origin: WorkspaceAssetOrigin.DependencyOnly, IsResolved: true } => 2,
        { TargetRowIdentity: not null } => 3,
        _ => 4
    };

    private static bool TryGetKey(
        WorkspaceAssetCatalogEntry entry,
        out AssetKey key)
    {
        string? name = entry.OriginalName ?? entry.NormalizedName;
        if (string.IsNullOrWhiteSpace(name))
        {
            key = default;
            return false;
        }

        key = AssetKey.FromWireName(
            CanonicalAssetFamily.FromSerializedType(entry.AssetType),
            name);
        return true;
    }

    private static bool EntryRetainsInitialTargetProvider(
        WorkspaceAssetCatalogEntry entry,
        IReadOnlyDictionary<AssetKey, LinkAssetProvider> selectedFullProviders,
        IReadOnlySet<LinkAssetProvider> initialProviders) =>
        TryGetKey(entry, out AssetKey key) &&
        selectedFullProviders.TryGetValue(key, out LinkAssetProvider? provider) &&
        initialProviders.Contains(provider);

    private static string Normalize(XAssetType assetType, string name) =>
        AssetKey.FromWireName(
            CanonicalAssetFamily.FromSerializedType(assetType),
            name).NormalizedName;
}
