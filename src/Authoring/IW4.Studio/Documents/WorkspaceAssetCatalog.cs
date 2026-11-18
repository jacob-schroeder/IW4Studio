using IW4.Game.Assets.Physics;
using IW4.Game.Assets.Tracer;
using IW4.Formats.D3dbsp;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Loaders.Database;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

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
public sealed record WorkspaceAssetProviderZone(
    string? LogicalZoneName,
    bool IsTarget = false);
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
        IW4.Game.Assets.BaseAsset? definition = null,
        int? rawHeader = null,
        XAssetHeaderKind? headerKind = null,
        WorkspaceAssetResolvedProvider? resolvedProvider = null,
        WorkspaceAssetProviderZone? providerZone = null,
        WorkspaceDependencyAssetIdentity? dependencyIdentity = null,
        LinkAssetProvider? targetProvider = null)
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
        ProviderZone = providerZone ?? resolvedProvider?.Zone;
        DependencyIdentity = dependencyIdentity;
        TargetProvider = targetProvider;
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
    internal WorkspaceAssetProviderZone? ProviderZone { get; }
    public WorkspaceAssetProviderZone? ResolvedProviderZone =>
        ResolvedProvider?.Zone;
    public bool IsGeneratedConfigStringBaseline =>
        AssetType == XAssetType.StringTable &&
        (OriginalName ?? NormalizedName)?.Contains(
            "configstrings",
            StringComparison.OrdinalIgnoreCase) == true;
    public bool HasDefinition => Definition is not null;
    internal IW4.Game.Assets.BaseAsset? Definition { get; }
    internal LinkAssetProvider? TargetProvider { get; }
}

public sealed class WorkspaceAssetCatalog
{
    private readonly IReadOnlyList<WorkspaceAssetCatalogEntry> _targetEntries;
    private readonly Dictionary<long, WorkspaceAssetCatalogEntry> _editablePoolEntries;
    private WorkspaceAssetCatalog(
        FastFileDocument document,
        IEnumerable<WorkspaceAssetCatalogEntry> targetEntries,
        IEnumerable<WorkspaceAssetCatalogEntry> dependencyEntries)
    {
        _targetEntries = Array.AsReadOnly(targetEntries.ToArray());
        DependencyEntries = Array.AsReadOnly(dependencyEntries.ToArray());
        Entries = Array.AsReadOnly(_targetEntries.Concat(DependencyEntries).ToArray());
        var adapters = AssetAuthoringAdapterRegistry.CreateDefault();
        _editablePoolEntries = Entries.Where(entry =>
                entry.Access == WorkspaceAssetAccess.Editable &&
                entry.ResolvedProviderZone?.IsTarget == true &&
                !entry.IsGeneratedConfigStringBaseline &&
                !D3dbspAssetTypeFacts.IsMultiplayerType(entry.AssetType) &&
                adapters.TryGetAdapter(entry.AssetType, out _))
            .GroupBy(entry => entry.ResolvedProvider!.ProviderId)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            .Where(entry => entry.TargetRowIdentity is not { } row ||
                document.LoadedZone.LoadedAssets[row.SerializedIndex].Materialization.RootProvider?.ProviderId.Value
                    == entry.ResolvedProvider!.ProviderId)
            .ToDictionary(entry => entry.ResolvedProvider!.ProviderId);
    }
    public WorkspaceAssetCatalogEntry? FindEditablePoolEntry(XAssetProviderId providerId) =>
        _editablePoolEntries.GetValueOrDefault(providerId.Value);
    public IReadOnlyList<WorkspaceAssetCatalogEntry> TargetEntries => _targetEntries;
    public IReadOnlyList<WorkspaceAssetCatalogEntry> DependencyEntries { get; }
    public IReadOnlyList<WorkspaceAssetCatalogEntry> Entries { get; }
    internal static WorkspaceAssetCatalog Create(
        FastFileDocument document,
        IReadOnlyList<WorkspaceZone> loadedZones)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(loadedZones);
        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone>
            providerZones = loadedZones.ToDictionary(
                zone => zone.LoadResult.Context.ZoneOwner,
                zone => new WorkspaceAssetProviderZone(
                    zone.LogicalZoneName,
                    zone.IsTarget));
        IReadOnlyCollection<XAssetSlot> activeSlots = document.IsBlank
            ? []
            : document.LoadedZone.Context.AssetPool.Slots;
        var activeSlotsByKey = new Dictionary<AssetKey, XAssetSlot>();
        foreach (XAssetSlot slot in activeSlots)
        {
            AssetKey key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(slot.AssetType),
                slot.Name);
            activeSlotsByKey.TryAdd(key, slot);
        }

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
                var activePoolProvider = root.Asset is { } rootKey &&
                    activeSlotsByKey.TryGetValue(rootKey, out XAssetSlot? activeSlot)
                        ? activeSlot.ActiveProvider
                        : null;
                WorkspaceAssetProviderZone? activeProviderZone =
                    activePoolProvider is null
                        ? null
                        : ResolveProviderZone(
                            providerZones,
                            activePoolProvider.Owner);
                WorkspaceAssetResolvedProvider? activeProvider =
                    activePoolProvider is { IsReferencePlaceholder: false }
                        ? new WorkspaceAssetResolvedProvider(
                            activePoolProvider.Id.Value,
                            activeProviderZone!)
                        : null;
                bool isResolvedReference = root.Intent == LinkRootIntent.External &&
                    activeProvider is not null;
                WorkspaceAssetResolvedProvider? resolvedProvider =
                    root.Intent is LinkRootIntent.Owned or LinkRootIntent.External
                        ? activeProvider
                        : null;
                IW4.Game.Assets.BaseAsset? definition =
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
                        resolvedProvider,
                        activeProviderZone),
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
                        resolvedProvider,
                        activeProviderZone),
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
        ILookup<AssetKey, LinkAssetProvider> targetNestedProviders =
            document.InitialLinkRequest.Assets.Providers
                .Where(provider =>
                    provider.SerializedType is (XAssetType.PhysPreset or XAssetType.Tracer) &&
                    !provider.IsReferencePlaceholder)
                .ToLookup(provider => provider.Key);
        WorkspaceAssetCatalogEntry[] dependencyEntries = document.IsBlank
            ? []
            : activeSlots
                .Select(slot =>
                {
                    var provider = slot.ActiveProvider;
                    AssetKey key = AssetKey.FromWireName(
                        CanonicalAssetFamily.FromSerializedType(slot.AssetType),
                        slot.Name);
                    WorkspaceAssetProviderZone providerZone = ResolveProviderZone(
                        providerZones,
                        provider.Owner);
                    return (slot, provider, providerZone, key);
                })
                .Where(value => !targetKeys.Contains(value.key))
                .Select(value =>
                {
                    LinkAssetProvider? targetProvider = FindEditableNestedProvider(
                        value.slot, value.providerZone, targetNestedProviders[value.key]);
                    return new WorkspaceAssetCatalogEntry(
                        null,
                        WorkspaceAssetOrigin.DependencyOnly,
                        value.provider.IsReferencePlaceholder
                            ? WorkspaceAssetAccess.ContentUnavailable
                            : targetProvider is null
                                ? WorkspaceAssetAccess.ReadOnly
                                : WorkspaceAssetAccess.Editable,
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
                                value.providerZone),
                        providerZone: value.providerZone,
                        dependencyIdentity: new WorkspaceDependencyAssetIdentity(
                            document.DocumentId,
                            value.provider.Id.Value),
                        targetProvider: targetProvider);
                })
                .ToArray();
        return new WorkspaceAssetCatalog(document, targetEntries, dependencyEntries);
    }

    private static LinkAssetProvider? FindEditableNestedProvider(
        XAssetSlot slot,
        WorkspaceAssetProviderZone zone,
        IEnumerable<LinkAssetProvider> targetProviders)
    {
        XAssetProviderContribution active = slot.ActiveProvider;
        if (!zone.IsTarget ||
            active.IsReferencePlaceholder ||
            active.Asset is not (PhysPresetAsset or TracerDefAsset) ||
            slot.Providers.Count(provider => !provider.IsReferencePlaceholder) != 1)
        {
            return null;
        }

        LinkAssetProvider[] matches = targetProviders.Take(2).ToArray();
        return matches.Length == 1 && string.Equals(
            matches[0].OriginalSerializedName,
            DbLoadExecutionContext.NormalizeLoadedAssetName(active.Name),
            StringComparison.Ordinal)
            ? matches[0]
            : null;
    }

    private static WorkspaceAssetProviderZone ResolveProviderZone(
        IReadOnlyDictionary<DbZoneHandle, WorkspaceAssetProviderZone> providerZones,
        DbZoneHandle owner)
    {
        if (owner.IsNone)
            return new WorkspaceAssetProviderZone(null, IsTarget: false);
        return providerZones.TryGetValue(
                owner,
                out WorkspaceAssetProviderZone? zone)
            ? zone
            : new WorkspaceAssetProviderZone(owner.ToString(), IsTarget: false);
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
