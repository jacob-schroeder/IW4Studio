using IW4.Runtime.Database;

namespace IW4.Studio.Documents;

/// <summary>
/// Immutable result of opening one Studio document. Runtime continues to own
/// the resolved asset graph and zone registry.
/// </summary>
public sealed record FastFileWorkspace
{
    internal FastFileWorkspace(
        FastFileDocument document,
        DbRuntime runtime,
        IReadOnlyList<WorkspaceZone> loadedZones,
        string? zonePlanProfileName,
        FastFileDependencyGraph dependencyGraph,
        WorkspaceAssetCatalog? assetCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(loadedZones);
        ArgumentNullException.ThrowIfNull(dependencyGraph);

        Document = document;
        Runtime = runtime;
        LoadedZones = Array.AsReadOnly(loadedZones.ToArray());
        ActiveZones = Array.AsReadOnly(
            LoadedZones.Where(zone => zone.IsActive).ToArray());
        ZonePlanProfileName = zonePlanProfileName;
        DependencyGraph = dependencyGraph;
        AssetCatalog = assetCatalog ?? WorkspaceAssetCatalog.Create(
            Document.TargetSource,
            Runtime.AssetPool,
            LoadedZones.Select(zone => new WorkspaceAssetProviderZone(
                zone.RuntimeZoneHandle,
                zone.LogicalZoneName)));
    }

    public FastFileDocument Document { get; }

    /// <summary>
    /// Detached target authoring authority. Runtime/LoadedZone state remains
    /// available for inspection, but is never the source for serialization.
    /// </summary>
    public TargetZoneSourceSnapshot TargetSource => Document.TargetSource;

    public DbRuntime Runtime { get; }

    public IReadOnlyList<WorkspaceZone> LoadedZones { get; }

    public IReadOnlyList<WorkspaceZone> ActiveZones { get; }

    public string? ZonePlanProfileName { get; }

    /// <summary>
    /// Physical fastfiles considered by the selected document-open mode, in
    /// dependency load order.
    /// </summary>
    public FastFileDependencyGraph DependencyGraph { get; }

    /// <summary>
    /// Immutable post-load catalog. It preserves every target serialized row
    /// and adds dependency content strictly as a read-only view projection.
    /// </summary>
    public WorkspaceAssetCatalog AssetCatalog { get; }

}
