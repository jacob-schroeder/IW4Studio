using IW4.Game.Assets;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents.AssetReferences;

public sealed record AssetDependencyRelation(
    WorkspaceAssetCatalogEntry? Entry,
    XAssetType AssetType,
    string Name,
    string FastFile,
    IReadOnlyList<string> ReferenceFields);

/// <summary>Both directions of the same direct, field-labelled workspace asset references.</summary>
public sealed class WorkspaceAssetDependencyIndex
{
    private readonly Dictionary<WorkspaceAssetCatalogEntry, Node> _entries = [];
    private readonly Dictionary<AssetKey, Node> _effective = [];
    private readonly IReadOnlyDictionary<long, WorkspaceAssetCatalogEntry> _activeProviders;

    private WorkspaceAssetDependencyIndex(IReadOnlyDictionary<long, WorkspaceAssetCatalogEntry> activeProviders) =>
        _activeProviders = activeProviders;

    public WorkspaceAssetCatalogEntry? FindActiveProvider(long providerId) => _activeProviders.GetValueOrDefault(providerId);

    public IReadOnlyList<AssetDependencyRelation> UsedBy(WorkspaceAssetCatalogEntry entry) =>
        _entries.TryGetValue(entry, out Node? node) ? Describe(node.Incoming) : [];

    public IReadOnlyList<AssetDependencyRelation> Uses(WorkspaceAssetCatalogEntry entry) =>
        _entries.TryGetValue(entry, out Node? node) ? Describe(node.Outgoing) : [];

    internal WorkspaceAssetCatalogEntry? EffectiveEntry(AssetKey key) =>
        _effective.GetValueOrDefault(key)?.Entry;

    internal IReadOnlyList<AssetDependencyRelation> UsedBy(AssetKey key) =>
        _effective.TryGetValue(key, out Node? node) ? Describe(node.Incoming) : [];

    private static AssetDependencyRelation[] Describe(Dictionary<Node, HashSet<string>> neighbors) =>
        neighbors.Select(pair => new AssetDependencyRelation(
            pair.Key.Entry, pair.Key.Type, pair.Key.Name, pair.Key.FastFile,
            pair.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();

    internal static WorkspaceAssetDependencyIndex Create(
        IReadOnlyList<WorkspaceAssetCatalogEntry> entries,
        LinkAssetPool providers,
        LinkAssetPool authored,
        IReadOnlyDictionary<long, WorkspaceAssetCatalogEntry> activeProviders,
        IReadOnlyDictionary<long, string> providerFiles,
        string targetFile,
        CancellationToken cancellationToken)
    {
        var index = new WorkspaceAssetDependencyIndex(activeProviders);
        var nodes = index._effective;
        var sources = new List<(Node Node, LinkAssetProvider? Frozen, BaseAsset? Loaded)>();
        var fullProviders = new Dictionary<AssetKey, LinkAssetProvider>();
        var authoredProviders = authored.Providers.ToHashSet();
        var active = activeProviders.Values.ToHashSet();
        foreach (LinkAssetProvider provider in providers.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!provider.IsReferencePlaceholder)
                fullProviders.TryAdd(provider.Key, provider);
        }

        // One asset may appear as a target reference and again as a dependency row.
        // Keep both navigation identities while counting that provider only once.
        var groups = entries.GroupBy(entry => entry.NormalizedName is { } name
            ? new AssetKey(CanonicalAssetFamily.FromSerializedType(entry.AssetType), name)
            : (AssetKey?)null);
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Key is not { } key)
                continue; // Structural rows have no DB identity.
            fullProviders.Remove(key, out LinkAssetProvider? provider);
            bool applied = provider is not null && authoredProviders.Contains(provider);
            WorkspaceAssetCatalogEntry primary = group.OrderBy(e =>
                applied && e.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition ? 0 :
                active.Contains(e) ? 1 :
                e.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition ? 2 :
                e.HasDefinition ? 3 : 4).First();
            Node node;
            if (applied && !InTarget(primary) && provider is not null)
                node = new Node(null, provider.SerializedType, provider.OriginalSerializedName, targetFile);
            else
                node = CatalogNode(primary);
            nodes.Add(key, node);
            bool useFrozen = node.Entry is null || InTarget(primary);
            sources.Add((node, useFrozen ? provider : null, primary.Definition));
            var seen = new Dictionary<BaseAsset, Node>(ReferenceEqualityComparer.Instance);
            if (node.Entry is not null && primary.Definition is { } definition)
                seen.Add(definition, node);
            foreach (WorkspaceAssetCatalogEntry entry in group)
            {
                // A shadowed definition remains selectable by its own row and fastfile.
                // References to the logical name resolve to the effective node above.
                if (entry.Definition is not { } loaded)
                {
                    index._entries.Add(entry, node);
                    continue;
                }
                if (!seen.TryGetValue(loaded, out Node? exact))
                {
                    exact = CatalogNode(entry);
                    seen.Add(loaded, exact);
                    sources.Add((exact, null, loaded));
                }
                index._entries.Add(entry, exact);
            }
        }

        // Imported models, materials, fonts, and other authored roots can contribute embedded
        // providers without adding navigator rows. They still participate in both directions.
        foreach ((AssetKey key, LinkAssetProvider provider) in fullProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = new Node(null, provider.SerializedType, provider.OriginalSerializedName, targetFile);
            nodes.Add(key, node);
            sources.Add((node, provider, null));
        }

        foreach ((Node source, LinkAssetProvider? frozen, BaseAsset? loaded) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<(XAssetType Type, string Name, string Field)> references = frozen is not null
                ? frozen.CaptureDependencies(cancellationToken)
                : loaded is not null
                    ? WorkspaceAssetDependencyEnumerator.Enumerate(loaded, cancellationToken)
                    : [];
            foreach ((XAssetType type, string name, string field) in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name);
                if (!nodes.TryGetValue(key, out Node? destination))
                {
                    destination = new Node(null, type, name, "Not loaded");
                    nodes.Add(key, destination);
                }

                if (!source.Outgoing.TryGetValue(destination, out HashSet<string>? fields))
                {
                    fields = new HashSet<string>(StringComparer.Ordinal);
                    source.Outgoing.Add(destination, fields);
                    destination.Incoming.Add(source, fields);
                }
                fields.Add(field);
            }
        }

        return index;

        static bool InTarget(WorkspaceAssetCatalogEntry entry) =>
            entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition || entry.ProviderZone?.IsTarget == true;

        Node CatalogNode(WorkspaceAssetCatalogEntry entry)
        {
            string file = InTarget(entry) ? targetFile :
                entry.ResolvedProvider is { } resolved ? providerFiles.GetValueOrDefault(resolved.ProviderId, "Unknown fastfile") :
                "Not loaded";
            return new Node(entry, entry.AssetType, entry.OriginalName ?? entry.NormalizedName ?? string.Empty, file);
        }
    }

    private sealed class Node(WorkspaceAssetCatalogEntry? entry, XAssetType type, string name, string fastFile)
    {
        public WorkspaceAssetCatalogEntry? Entry { get; } = entry;
        public XAssetType Type { get; } = type;
        public string Name { get; } = name;
        public string FastFile { get; } = fastFile;
        public Dictionary<Node, HashSet<string>> Incoming { get; } = [];
        public Dictionary<Node, HashSet<string>> Outgoing { get; } = [];
    }
}
