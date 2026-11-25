using IW4.Game.Assets;
using IW4.Linker.Contracts;
using IW4.Studio.Documents.AssetReferences;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    /// <summary>Indexes one applied workspace snapshot without holding the editing lock during traversal.</summary>
    public WorkspaceAssetDependencyIndex BuildAssetDependencyIndex(CancellationToken cancellationToken)
    {
        List<WorkspaceAssetCatalogEntry> entries;
        LinkAssetPool providers;
        LinkAssetPool authored;
        var activeEntries = new Dictionary<long, WorkspaceAssetCatalogEntry>();
        var providerFiles = new Dictionary<long, string>();
        lock (_gate)
        {
            ThrowIfDisposedCore();
            cancellationToken.ThrowIfCancellationRequested();
            entries = [.. Document.Rows, .. Workspace.AssetCatalog.DependencyEntries];
            providers = _revision.LinkRequest.Assets;
            authored = _authoredAssets;
            if (!Workspace.IsBlank)
                CaptureActiveEntries(entries, activeEntries, providerFiles, cancellationToken);
        }

        return WorkspaceAssetDependencyIndex.Create(entries, providers, authored, activeEntries, providerFiles,
            Workspace.IsBlank ? "New fastfile" : Path.GetFileName(Workspace.SourcePath), cancellationToken);
    }

    private void CaptureActiveEntries(List<WorkspaceAssetCatalogEntry> entries,
        Dictionary<long, WorkspaceAssetCatalogEntry> activeEntries, Dictionary<long, string> files,
        CancellationToken cancellationToken)
    {
        var zones = Workspace.LoadedZones.ToDictionary(zone => zone.LoadResult.Context.ZoneOwner);
        var byDefinition = new Dictionary<BaseAsset, WorkspaceAssetCatalogEntry>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
            if (entry.Definition is { } definition)
                byDefinition.TryAdd(definition, entry);

        foreach (var slot in Workspace.LoadedZone.Context.AssetPool.Slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = slot.ActiveProvider;
            if (provider.IsReferencePlaceholder)
                continue;
            zones.TryGetValue(provider.Owner, out var zone);
            files[provider.Id.Value] = zone is null ? "Unknown fastfile" : Path.GetFileName(zone.PhysicalPath);
            if (!byDefinition.TryGetValue(provider.Asset, out var entry))
            {
                // A target row can share a key with a different active pool provider.
                // The pool editor opens that exact provider, even if it has no separate Explorer row.
                var providerZone = new WorkspaceAssetProviderZone(zone?.LogicalZoneName, zone?.IsTarget == true);
                var key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(slot.AssetType), slot.Name);
                entry = new WorkspaceAssetCatalogEntry(null, WorkspaceAssetOrigin.DependencyOnly,
                    WorkspaceAssetAccess.ReadOnly, WorkspaceAssetContentSource.ResolvedProvider,
                    slot.AssetType, slot.Name, key.NormalizedName, provider.Asset,
                    resolvedProvider: new WorkspaceAssetResolvedProvider(provider.Id.Value, providerZone),
                    dependencyIdentity: new WorkspaceDependencyAssetIdentity(Document.DocumentId, provider.Id.Value));
                entries.Add(entry);
            }
            activeEntries.Add(provider.Id.Value, entry);
        }
    }
}
