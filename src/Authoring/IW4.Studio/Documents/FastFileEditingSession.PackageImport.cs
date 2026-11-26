using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Sound;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Loaders.Database;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    private readonly Dictionary<AssetKey, BaseAsset> _packageDefinitions = [];
    private readonly Dictionary<AssetKey, LoadedXZone> _packageDefinitionOwners = [];
    private readonly List<AssetPackageZone> _packageZones = [];
    private readonly HashSet<AssetKey> _changedPackageProviders = [];
    private readonly ConditionalWeakTable<BaseAsset, LoadedXZone> _packageStreamOwners = new();
    private bool _hasPackageImport;

    internal LoadedXZone? FindPackageStreamOwner(BaseAsset asset) =>
        _packageStreamOwners.TryGetValue(asset, out var owner) ? owner : null;

    public AssetPackageImportPreview PreviewAssetPackageImport(Stream source, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var contents = AssetPackageArchive.Read(source);
        var package = new AssetPackageZone(contents.FastFile, contents.ImagePackage, contents.SoundPackage);
        try
        {
            long revision = Revision;
            var graph = BuildAssetDependencyIndex(token);
            var request = package.Request;
            var incoming = request.Assets.Providers.GroupBy(p => p.Key)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault(p => !p.IsReferencePlaceholder) ?? g.First());
            var declarations = contents.Members.ToDictionary(m => PackageKey(m.Type, m.WireName));
            var rootGroups = request.Roots.Where(r => r.Asset.HasValue).ToDictionary(r => r.Asset!.Value);
            foreach (var (key, provider) in incoming)
            {
                if (!declarations.TryGetValue(key, out var declaration) || declaration.Included == provider.IsReferencePlaceholder ||
                    declaration.Type != provider.SerializedType)
                    throw new InvalidDataException($"The package inventory does not match the native provider for {key}.");
                foreach (var dep in provider.CaptureDependencies(token))
                    if (!declarations.ContainsKey(PackageKey(dep.Type, dep.Name)))
                        throw new InvalidDataException($"The package does not declare {dep.Type} '{dep.Name}' used by {key}.");
            }
            var structuralCounts = request.Roots.Where(r => !r.Asset.HasValue)
                .GroupBy(r => (r.SerializedType, r.Intent, r.OpaqueHeader)).ToDictionary(g => g.Key, g => g.Count());
            var selected = new HashSet<AssetKey>();
            foreach (var root in contents.SelectedRoots)
            {
                if (root.WireName is { } name)
                {
                    var key = PackageKey(root.Type, name);
                    if (!rootGroups.TryGetValue(key, out var native) || native.SerializedType != root.Type ||
                        (root.Intent == LinkRootIntent.Owned && native.Intent != LinkRootIntent.Owned) || !selected.Add(key))
                        throw new InvalidDataException($"Selected root {key} does not match the package fastfile.");
                }
                else if (structuralCounts.TryGetValue((root.Type, root.Intent, root.OpaqueHeader), out int count) && count > 0)
                    structuralCounts[(root.Type, root.Intent, root.OpaqueHeader)] = count - 1;
                else throw new InvalidDataException("A selected structural row is absent from the package fastfile.");
            }
            if (structuralCounts.Values.Any(n => n != 0))
                throw new InvalidDataException("The package contains undeclared structural rows.");
            var definitions = package.Zone.Context.AssetPool.Slots.SelectMany(s => s.Providers)
                .Where(p => !p.IsReferencePlaceholder).GroupBy(p => PackageKey(p.AssetType, p.Name))
                .ToDictionary(g => g.Key, g => g.First().Asset);
            var items = new List<AssetPackageImportItem>();
            string? dependencyProfile = null;
            if (!Workspace.IsBlank && Workspace.ZonePlanProfileName is null && declarations.Values.Any(m => !m.Included))
            {
                try { dependencyProfile = FastFileOpenProfiles.ResolveForTarget(Workspace.SourcePath); }
                catch (NotSupportedException) { /* Custom zone names can still use definitions supplied by the user. */ }
            }
            lock (_gate)
            {
                RequirePackageRevision(revision);
                var targetRequest = _revision.LinkRequest;
                var local = targetRequest.Assets.Providers.Where(p => !p.IsReferencePlaceholder)
                    .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
                var targetRows = Document.Rows.Where(row => row.NormalizedName is not null)
                    .ToLookup(r => PackageKey(r.AssetType, r.NormalizedName!));
                foreach (var (key, member) in declarations)
                {
                    token.ThrowIfCancellationRequested();
                    incoming.TryGetValue(key, out var provider);
                    rootGroups.TryGetValue(key, out var root);
                    definitions.TryGetValue(key, out var definition);
                    if (member.Included && (definition is null || provider is null || provider.IsReferencePlaceholder))
                        throw new InvalidDataException($"The package lacks its declared content for {key}.");
                    var row = new AssetPackageRow(key, member.Type, member.WireName, AssetPackageStatus.Add,
                        root is null ? "Include with its owner" : "Add to this fastfile", []);
                    var matches = targetRows[key].Take(2).ToArray();
                    var target = matches.FirstOrDefault();
                    var active = graph.EffectiveEntry(key);
                    local.TryGetValue(key, out var previous);
                    try
                    {
                        if (matches.Length > 1)
                            throw new InvalidDataException("Multiple destination rows have this name.");
                        if (previous is not null && previous.SerializedType != member.Type)
                            throw new InvalidDataException("The destination uses a different serialized type for this asset family.");
                        if (!member.Included && previous is null && active?.HasDefinition != true)
                        {
                            row = row with
                            {
                                Status = AssetPackageStatus.Blocked,
                                IsMissingDependency = true,
                                Detail = dependencyProfile is not null
                                    ? "Choose Load dependencies to look for this asset in the destination’s fastfiles."
                                    : "Import the package that defines this asset first, or include its definition in the source package."
                            };
                        }
                        else if (!member.Included)
                        {
                            bool addReference = root?.Intent == LinkRootIntent.External && target is null && previous is null;
                            row = row with
                            {
                                Status = addReference ? AssetPackageStatus.Add : AssetPackageStatus.Reuse,
                                Detail = addReference ? "Add a reference to the existing provider" : "Required dependency resolved in destination"
                            };
                        }
                        else if (previous is not null)
                        {
                            BaseAsset? existing = ResolveAssetPackageDefinition(key, target ?? active);
                            if (existing is null) throw new InvalidDataException("The destination definition is unavailable.");
                            var owner = ResolveAssetPackageOwner(key, target ?? active, true);
                            bool same = PackageFingerprint(targetRequest.Assets, previous, existing, owner, targetRequest.LanguageMask)
                                .SequenceEqual(PackageFingerprint(request.Assets, provider!, definition!, package.Zone, targetRequest.LanguageMask));
                            bool promote = same && selected.Contains(key) && root?.Intent == LinkRootIntent.Owned && target is null;
                            row = row with
                            {
                                Status = promote ? AssetPackageStatus.Add : same ? AssetPackageStatus.Reuse : AssetPackageStatus.Conflict,
                                Detail = promote ? "Register the selected embedded asset as a root" : same ? "Matching content in destination" : "Different content in destination",
                                CanReplace = !same,
                                UsedBy = same ? [] : graph.UsedBy(key).Select(u => $"{u.AssetType} · {u.Name}").ToArray()
                            };
                        }
                        else if (target is not null)
                        {
                            row = row with
                            {
                                Status = AssetPackageStatus.Conflict,
                                CanReplace = true,
                                Detail = "The destination contains an external reference. Replace to install the definition."
                            };
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or OverflowException)
                    {
                        row = row with { Status = AssetPackageStatus.Blocked, Detail = ex.Message, CanReplace = false };
                    }
                    items.Add(new(row, root, provider, definition, target ?? active));
                }
                var counts = targetRequest.Roots.Where(r => r.Asset is null).GroupBy(r => (r.SerializedType, r.Intent, r.OpaqueHeader))
                    .ToDictionary(g => g.Key, g => g.Count());
                foreach (var r in request.Roots.Where(r => r.Asset is null))
                {
                    var id = (r.SerializedType, r.Intent, r.OpaqueHeader);
                    bool reuse = counts.GetValueOrDefault(id) > 0;
                    if (reuse) counts[id]--;
                    items.Add(new(new(null, r.SerializedType, r.OpaqueHeader is int h ? $"0x{h:X8}" : "Null row",
                        reuse ? AssetPackageStatus.Reuse : AssetPackageStatus.Add, "Preserve the serialized row", []), r, null, null, null));
                }
            }
            return new(Document.DocumentId, revision, package, items.AsReadOnly(), graph, dependencyProfile, contents.SelectedRoots);
        }
        catch { package.Dispose(); throw; }
    }

    /// <summary>Checks the destination's normal load plan for external definitions without replacing the open document.</summary>
    public IReadOnlyList<AssetPackageRow> LoadAssetPackageDependencies(AssetPackageImportPreview preview, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        string profile = preview.DependencyProfile ?? throw new InvalidOperationException(
            "This destination has no additional dependency load plan.");
        void ValidatePreview()
        {
            RequirePackageRevision(preview.Revision);
            if (preview.DocumentId != Document.DocumentId)
                throw new InvalidOperationException("Open the package preview in this fastfile first.");
            _ = preview.Package;
            token.ThrowIfCancellationRequested();
        }

        lock (_gate)
        {
            ValidatePreview();
            if (!preview.CanLoadDependencies) return preview.Rows;
        }

        using var loaded = new FastFileDocumentService(_ => token.ThrowIfCancellationRequested())
            .Open(new(Workspace.SourcePath, new ZonePlan(profile)));
        var sources = loaded.ActiveZones.Where(zone => !zone.IsTarget)
            .ToDictionary(zone => zone.LoadResult.Context.ZoneOwner, zone => Path.GetFileName(zone.PhysicalPath));
        var providers = loaded.LoadedZone.Context.AssetPool.Slots.Select(slot => slot.ActiveProvider)
            .Where(p => !p.IsReferencePlaceholder && sources.ContainsKey(p.Owner))
            .ToLookup(p => (p.AssetType, Key: PackageKey(p.AssetType, p.Name)), p => sources[p.Owner]);

        lock (_gate)
        {
            ValidatePreview();
            var items = preview.Items.ToArray();
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var row = item.Row;
                if (!row.IsMissingDependency || row.Key is not { } key) continue;
                string? source = providers[(row.Type, key)].FirstOrDefault();
                bool addReference = item.Root?.Intent == LinkRootIntent.External && item.Destination?.TargetRowIdentity is null;
                items[i] = item with { Row = source is null
                    ? row with { Detail = "The destination’s loaded fastfiles do not define this asset. Import its defining package first, or include it in the source package." }
                    : row with
                    {
                        IsMissingDependency = false,
                        Status = addReference ? AssetPackageStatus.Add : AssetPackageStatus.Reuse,
                        Detail = addReference ? $"Add a reference to {source}" : $"Supplied by {source}"
                    } };
            }
            preview.UpdateItems(items);
            return preview.Rows;
        }
    }

    private static AssetKey PackageKey(XAssetType type, string name) =>
        AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(type), name);

    private void RequirePackageRevision(long revision)
    {
        ThrowIfDisposedCore();
        if (_revision.Revision != revision)
            throw new InvalidOperationException("The target changed. Open the package preview again.");
    }

    // Emit a single native definition with symbolic external children so an image conflict,
    // for example, does not also mark every material using it as a different material.
    private byte[] PackageFingerprint(LinkAssetPool originalPool, LinkAssetProvider provider, BaseAsset definition,
        LoadedXZone? owner, uint languageMask)
    {
        var roots = provider.CaptureDependencies(default)
            .Select(d => (d.Type, Key: PackageKey(d.Type, d.Name))).Where(d => d.Key != provider.Key)
            .DistinctBy(d => d.Key).OrderBy(d => d.Type).ThenBy(d => d.Key.NormalizedName, StringComparer.Ordinal)
            .Where(d => ZoneLinker.SupportsExternalReference(d.Type))
            .Select(d => new LinkRoot($"ref-{d.Type}-{d.Key.NormalizedName}", d.Type, LinkRootIntent.External, d.Key,
                "," + d.Key.NormalizedName, null)).ToList();
        roots.Add(new("owned", provider.SerializedType, LinkRootIntent.Owned, provider.Key, provider.OriginalSerializedName, null));
        var pool = originalPool.ForKeys([provider.Key]);
        NativeStreamPackageBuildResult? streams = null;
        if (definition is GfxImageAsset or SoundAliasListAsset)
        {
            streams = NativeStreamPackageBuilder.Repackage([new(provider.Key,definition,owner,
                CreateAssetPackageSource(provider.Key, definition, owner))], languageMask);
            if (streams.Sources.Count != 0)
                pool = pool.WithoutProviders([provider.Key]).WithHighestPrecedenceProviders(streams.Sources);
        }
        var linked = new ZoneLinker().Link(ZoneLinker.ProjectDependencyRootOrder(new(pool, roots, languageMask,
            languageMask & (0u - languageMask), [])));
        if (!linked.Succeeded) throw new InvalidDataException(string.Join(Environment.NewLine, linked.Errors));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(linked.DecodedBytes!.Value.Span);
        if (streams?.ImagePackage is { } images) digest.AppendData(images);
        if (streams?.SoundPackage is { } sounds) digest.AppendData(sounds);
        return digest.GetHashAndReset();
    }
}
