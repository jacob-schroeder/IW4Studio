using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public enum WorkspaceAssetOrigin
{
    TargetOwnedDefinition,
    TargetResolvedReference,
    TargetUnresolvedReference,
    DependencyOnly,
    NullRow,
    OpaqueRow
}

public enum WorkspaceAssetAccess
{
    Editable,
    ReadOnly,
    ContentUnavailable
}

public enum WorkspaceAssetContentSource
{
    TargetAuthoredBaseline,
    ResolvedProvider,
    StructuralOnly,
    Unavailable
}

public readonly record struct WorkspaceDependencyAssetIdentity(
    Guid DocumentId,
    long ProviderId);
public sealed record WorkspaceAssetProviderZone(string? LogicalZoneName);
public sealed record WorkspaceAssetResolvedProvider(
    long ProviderId,
    WorkspaceAssetProviderZone Zone);

/// <summary>Link-request-derived catalog. Root ownership is never inferred from runtime pool precedence.</summary>
public sealed class WorkspaceAssetCatalogEntry
{
    internal WorkspaceAssetCatalogEntry(
        TargetZoneRowIdentity? targetRowIdentity,
        WorkspaceAssetOrigin origin,
        WorkspaceAssetAccess access,
        WorkspaceAssetContentSource contentSource,
        XAssetType assetType,
        string? originalName,
        string? normalizedName,
        IW4.Assets.Assets.BaseAsset? definition = null,
        int? rawHeader = null,
        XAssetHeaderKind? headerKind = null,
        WorkspaceAssetResolvedProvider? resolvedProvider = null,
        WorkspaceDependencyAssetIdentity? dependencyIdentity = null)
    {
        if ((targetRowIdentity is null) == (dependencyIdentity is null))
        {
            throw new ArgumentException(
                "A catalog entry requires exactly one target-row or dependency identity.");
        }

        TargetRowIdentity = targetRowIdentity;
        Origin = origin;
        Access = access;
        ContentSource = contentSource;
        AssetType = assetType;
        OriginalName = originalName;
        NormalizedName = normalizedName;
        Definition = definition;
        RawHeader = rawHeader;
        HeaderKind = headerKind;
        ResolvedProvider = resolvedProvider;
        DependencyIdentity = dependencyIdentity;
    }
    public TargetZoneRowIdentity? TargetRowIdentity { get; }
    public WorkspaceDependencyAssetIdentity? DependencyIdentity { get; }
    public WorkspaceAssetOrigin Origin { get; }
    public WorkspaceAssetAccess Access { get; }
    public WorkspaceAssetContentSource ContentSource { get; }
    public XAssetType AssetType { get; }
    public string? OriginalName { get; }
    public string? NormalizedName { get; }
    public int? RawHeader { get; }
    public XAssetHeaderKind? HeaderKind { get; }
    public WorkspaceAssetResolvedProvider? ResolvedProvider { get; }
    public WorkspaceAssetProviderZone? ResolvedProviderZone =>
        ResolvedProvider?.Zone;
    internal IW4.Assets.Assets.BaseAsset? Definition { get; }
}

public sealed class WorkspaceAssetCatalog
{
    private readonly IReadOnlyList<WorkspaceAssetCatalogEntry> _targetEntries;
    private WorkspaceAssetCatalog(
        IEnumerable<WorkspaceAssetCatalogEntry> targetEntries,
        IEnumerable<WorkspaceAssetCatalogEntry> dependencyEntries)
    {
        _targetEntries = Array.AsReadOnly(targetEntries.ToArray());
        DependencyEntries = Array.AsReadOnly(dependencyEntries.ToArray());
        Entries = Array.AsReadOnly(_targetEntries.Concat(DependencyEntries).ToArray());
    }
    public IReadOnlyList<WorkspaceAssetCatalogEntry> TargetEntries => _targetEntries;
    public IReadOnlyList<WorkspaceAssetCatalogEntry> DependencyEntries { get; }
    public IReadOnlyList<WorkspaceAssetCatalogEntry> Entries { get; }
    internal static WorkspaceAssetCatalog Create(
        FastFileDocument document,
        IReadOnlyList<WorkspaceZone> loadedZones)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(loadedZones);
        WorkspaceAssetCatalogEntry[] targetEntries = document.InitialLinkRequest.Roots
            .Select((root, index) =>
            {
                TargetZoneRowIdentity identity = new(
                    document.DocumentId,
                    index);
                string? name = root.OriginalSerializedName;
                string? normalized = root.Asset?.NormalizedName;
                if (document.IsBlank)
                    return CreateBlankEntry(root, identity, name, normalized);

                var loadedZone = document.LoadedZone;
                var sourceRow = loadedZone.XAssetList.Assets[index];
                var materialization = loadedZone.LoadedAssets[index].Materialization;
                var provider = materialization.RootProvider;
                var activePoolProvider = root.Asset is { } rootKey
                    ? loadedZone.Context.AssetPool.Slots
                        .FirstOrDefault(slot => AssetKey.FromWireName(
                            CanonicalAssetFamily.FromSerializedType(slot.AssetType),
                            slot.Name) == rootKey)
                        ?.ActiveProvider
                    : null;
                WorkspaceAssetResolvedProvider? activeProvider =
                    activePoolProvider is { IsReferencePlaceholder: false }
                        ? new WorkspaceAssetResolvedProvider(
                            activePoolProvider.Id.Value,
                            new WorkspaceAssetProviderZone(
                                ResolveProviderZoneName(
                                    loadedZones,
                                    activePoolProvider.Owner)))
                        : null;
                bool isResolvedReference = root.Intent == LinkRootIntent.External &&
                    activeProvider is not null;
                WorkspaceAssetResolvedProvider? resolvedProvider =
                    root.Intent is LinkRootIntent.Owned or LinkRootIntent.External
                        ? activeProvider
                        : null;
                IW4.Assets.Assets.BaseAsset? definition =
                    root.Intent == LinkRootIntent.External
                        ? isResolvedReference
                            ? activePoolProvider!.Asset
                            : null
                        : provider?.Asset;
                return root.Intent switch
                {
                    LinkRootIntent.Owned => new WorkspaceAssetCatalogEntry(
                        identity,
                        WorkspaceAssetOrigin.TargetOwnedDefinition,
                        WorkspaceAssetAccess.Editable,
                        WorkspaceAssetContentSource.TargetAuthoredBaseline,
                        root.SerializedType,
                        name,
                        normalized,
                        definition,
                        sourceRow.RawHeader,
                        sourceRow.HeaderKind,
                        resolvedProvider),
                    LinkRootIntent.External => new WorkspaceAssetCatalogEntry(
                        identity,
                        isResolvedReference
                            ? WorkspaceAssetOrigin.TargetResolvedReference
                            : WorkspaceAssetOrigin.TargetUnresolvedReference,
                        isResolvedReference
                            ? WorkspaceAssetAccess.ReadOnly
                            : WorkspaceAssetAccess.ContentUnavailable,
                        isResolvedReference
                            ? WorkspaceAssetContentSource.ResolvedProvider
                            : WorkspaceAssetContentSource.Unavailable,
                        root.SerializedType,
                        name,
                        normalized,
                        definition,
                        sourceRow.RawHeader,
                        sourceRow.HeaderKind,
                        resolvedProvider),
                    LinkRootIntent.Null => new WorkspaceAssetCatalogEntry(
                        identity,
                        WorkspaceAssetOrigin.NullRow,
                        WorkspaceAssetAccess.ReadOnly,
                        WorkspaceAssetContentSource.StructuralOnly,
                        root.SerializedType,
                        originalName: null,
                        normalizedName: null,
                        rawHeader: sourceRow.RawHeader,
                        headerKind: sourceRow.HeaderKind),
                    _ => new WorkspaceAssetCatalogEntry(
                        identity,
                        WorkspaceAssetOrigin.OpaqueRow,
                        WorkspaceAssetAccess.ReadOnly,
                        WorkspaceAssetContentSource.StructuralOnly,
                        root.SerializedType,
                        originalName: null,
                        normalizedName: null,
                        rawHeader: sourceRow.RawHeader,
                        headerKind: sourceRow.HeaderKind)
                };
            })
            .ToArray();

        var targetKeys = new HashSet<AssetKey>(
            document.InitialLinkRequest.Roots
                .Where(root => root.Asset is not null)
                .Select(root => root.Asset!.Value));
        WorkspaceAssetCatalogEntry[] dependencyEntries = document.IsBlank
            ? []
            : document.LoadedZone.Context.AssetPool.Slots
                .Select(slot =>
                {
                    var provider = slot.ActiveProvider;
                    AssetKey key = AssetKey.FromWireName(
                        CanonicalAssetFamily.FromSerializedType(slot.AssetType),
                        slot.Name);
                    return (slot, provider, key);
                })
                .Where(value => !targetKeys.Contains(value.key))
                .Select(value => new WorkspaceAssetCatalogEntry(
                    null,
                    WorkspaceAssetOrigin.DependencyOnly,
                    value.provider.IsReferencePlaceholder
                        ? WorkspaceAssetAccess.ContentUnavailable
                        : WorkspaceAssetAccess.ReadOnly,
                    value.provider.IsReferencePlaceholder
                        ? WorkspaceAssetContentSource.Unavailable
                        : WorkspaceAssetContentSource.ResolvedProvider,
                    value.slot.AssetType,
                    value.slot.Name,
                    value.key.NormalizedName,
                    value.provider.IsReferencePlaceholder
                        ? null
                        : value.provider.Asset,
                    resolvedProvider: value.provider.IsReferencePlaceholder
                        ? null
                        : new WorkspaceAssetResolvedProvider(
                            value.provider.Id.Value,
                            new WorkspaceAssetProviderZone(
                                ResolveProviderZoneName(loadedZones, value.provider.Owner))),
                    dependencyIdentity: new WorkspaceDependencyAssetIdentity(
                        document.DocumentId,
                        value.provider.Id.Value)))
                .ToArray();
        return new WorkspaceAssetCatalog(targetEntries, dependencyEntries);
    }

    private static string? ResolveProviderZoneName(
        IEnumerable<WorkspaceZone> loadedZones,
        object? owner)
    {
        if (owner is null)
            return null;
        WorkspaceZone? zone = loadedZones.FirstOrDefault(candidate =>
            candidate.LoadResult.Context.ZoneOwner.Equals(owner));
        return zone?.LogicalZoneName ?? owner.ToString();
    }

    private static WorkspaceAssetCatalogEntry CreateBlankEntry(
        LinkRoot root,
        TargetZoneRowIdentity identity,
        string? name,
        string? normalized) => root.Intent switch
    {
        LinkRootIntent.Owned => new WorkspaceAssetCatalogEntry(
            identity,
            WorkspaceAssetOrigin.TargetOwnedDefinition,
            WorkspaceAssetAccess.Editable,
            WorkspaceAssetContentSource.TargetAuthoredBaseline,
            root.SerializedType,
            name,
            normalized),
        LinkRootIntent.External => new WorkspaceAssetCatalogEntry(
            identity,
            WorkspaceAssetOrigin.TargetUnresolvedReference,
            WorkspaceAssetAccess.ContentUnavailable,
            WorkspaceAssetContentSource.Unavailable,
            root.SerializedType,
            name,
            normalized),
        LinkRootIntent.Null => new WorkspaceAssetCatalogEntry(
            identity,
            WorkspaceAssetOrigin.NullRow,
            WorkspaceAssetAccess.ReadOnly,
            WorkspaceAssetContentSource.StructuralOnly,
            root.SerializedType,
            originalName: null,
            normalizedName: null),
        _ => new WorkspaceAssetCatalogEntry(
            identity,
            WorkspaceAssetOrigin.OpaqueRow,
            WorkspaceAssetAccess.ReadOnly,
            WorkspaceAssetContentSource.StructuralOnly,
            root.SerializedType,
            originalName: null,
            normalizedName: null)
    };
}
