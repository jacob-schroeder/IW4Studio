using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Sound;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    public int ImportAssetPackage(AssetPackageImportPreview preview,
        IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(choices);
        int changed;
        lock (_gate)
        {
            RequirePackageRevision(preview.Revision);
            if (Document.DocumentId != preview.DocumentId)
                throw new InvalidOperationException("Open the package preview in this fastfile first.");
            var package = preview.Package;
            var incoming = package.Request;
            var previous = _revision.LinkRequest;
            var roots = previous.Roots.ToList();
            var positions = new Dictionary<AssetKey, int>();
            for (int index = 0; index < roots.Count; index++)
                if (roots[index].Asset is { } key) positions.TryAdd(key, index);
            var full = previous.Assets.Providers.Where(p => !p.IsReferencePlaceholder).Select(p => p.Key).ToHashSet();
            var install = new Dictionary<AssetKey, AssetPackageImportItem>();
            var touchedRows = new Dictionary<int, AssetPackageImportItem>();
            foreach (var item in preview.Items)
            {
                var row = item.Row;
                if (row.Status == AssetPackageStatus.Blocked)
                    throw new InvalidOperationException($"Resolve {row.Type} {row.Name} before importing. {row.Detail}");
                if (row.Status == AssetPackageStatus.Conflict)
                {
                    if (row.Key is not { } conflict || !choices.TryGetValue(conflict, out var choice) || !Enum.IsDefined(choice))
                        throw new InvalidOperationException($"Choose whether to keep or replace {row.Type} {row.Name}.");
                    if (choice == AssetPackageConflictChoice.KeepExisting) continue;
                    if (!row.CanReplace) throw new InvalidOperationException($"{conflict} cannot be replaced.");
                }
                if (row.Key is not { } key)
                {
                    if (row.Status == AssetPackageStatus.Add) Append(item, item.Root!);
                    continue;
                }
                bool addProvider = item.Definition is not null && row.Status != AssetPackageStatus.Reuse;
                if (addProvider) install.Add(key, item);
                if (positions.TryGetValue(key, out int existingIndex))
                {
                    if (addProvider)
                    {
                        roots[existingIndex] = new(roots[existingIndex].EntryId, row.Type, LinkRootIntent.Owned, key,
                            item.Provider!.OriginalSerializedName, null);
                        touchedRows.Add(existingIndex, item);
                    }
                    continue;
                }
                if (addProvider && item.Root is { } owned) Append(item, owned);
                else if (item.Definition is null && !full.Contains(key) &&
                    item.Root is { Intent: LinkRootIntent.External } external)
                    Append(item, external);
            }
            void Append(AssetPackageImportItem item, LinkRoot root)
            {
                int index = roots.Count;
                roots.Add(new($"package:{Guid.NewGuid():N}", root.SerializedType, root.Intent, root.Asset, root.OriginalSerializedName, root.OpaqueHeader));
                touchedRows.Add(index, item);
                if (root.Asset is { } key) positions.Add(key, index);
            }
            changed = install.Count + touchedRows.Count(t => t.Value.Row.Key is not { } key || !install.ContainsKey(key));
            if (changed == 0) return 0;

            // Named payload offsets belong to the receiving zone. Existing numbered streams keep
            // their identities, and an imported named stream is appended after destination data.
            var streamInputs = install.Where(p => p.Value.Definition is GfxImageAsset or SoundAliasListAsset)
                .Select(p => new NativeStreamProviderInput(p.Key, p.Value.Definition!, package.Zone,
                    LoadedZoneLinkExtensions.CreateCapturedProviderSource(package.Zone, p.Value.Definition!, p.Value.Definition!,
                        p.Value.Provider!.OriginalSerializedName, LinkAssetProviderSourceDisposition.AuthoredDetached))).ToArray();
            var sidecars = GetNativeStreamPackageBase();
            var streams = NativeStreamPackageBuilder.Repackage(streamInputs, previous.LanguageMask, sidecars.ImagePackage, sidecars.SoundPackage);
            var existingProviders = previous.Assets.Providers.Select(p => p.Key).ToHashSet();
            var referenceKeys = incoming.Assets.Providers.Where(p => p.IsReferencePlaceholder && !existingProviders.Contains(p.Key))
                .Select(p => p.Key);
            var selected = incoming.Assets.ForKeys(install.Keys.Concat(referenceKeys));
            if (streams.Sources.Count != 0)
                selected = selected.WithoutProviders(streams.Sources.Select(s => AssetKey.FromDefinition(s.Definition)))
                    .WithHighestPrecedenceProviders(streams.Sources);
            var merged = previous.Assets.WithoutProviders(install.Keys).WithHighestPrecedencePool(selected);
            var candidate = ZoneLinker.ProjectDependencyRootOrder(new(merged, roots, previous.LanguageMask,
                previous.SelectedLanguageMask, previous.ScriptStrings));
            var linked = new ZoneLinker().Link(candidate);
            if (!linked.Succeeded) throw new InvalidDataException(string.Join(Environment.NewLine, linked.Errors));
            var ff = new FastFilePackager().PackageGreenfield(linked.DecodedBytes!.Value, linked.LanguageMask,
                linked.SelectedLanguageMask, linked.ImageStreamLanguageTables, headerMetadata: _revision.HeaderMetadata);
            if (!ff.Succeeded) throw new InvalidDataException(string.Join(Environment.NewLine, ff.Errors.Select(e => e.Message)));

            // Reopen the staged candidate before mutating the session. Its materialized roots
            // already bind to exactly the providers chosen by the conflict preview.
            var staged = new AssetPackageZone(ff.Bytes!.Value.ToArray(), streams.ImagePackage ?? sidecars.ImagePackage,
                streams.SoundPackage ?? sidecars.SoundPackage);
            try
            {
                var stagedRows = candidate.Roots.Zip(staged.Workspace.AssetCatalog.TargetEntries)
                    .ToDictionary(pair => pair.First.EntryId, pair => pair.Second);
                var definitions = staged.Zone.Context.AssetPool.Slots.SelectMany(s => s.Providers)
                    .Where(p => !p.IsReferencePlaceholder).GroupBy(p => PackageKey(p.AssetType, p.Name))
                    .ToDictionary(g => g.Key, g => g.First().Asset);
                if (install.Keys.Any(k => !definitions.ContainsKey(k)))
                    throw new InvalidDataException("A selected definition was lost while merging the package.");
                var adapters = AssetAuthoringAdapterRegistry.CreateDefault();
                // Refresh materialized consumers of a replaced provider, so opening an existing
                // editor before Save As sees the same bindings as reopening the saved fastfile.
                var updates = new List<(WorkspaceAssetCatalogEntry Row, DraftState? Draft, bool Direct)>();
                var updateIndices = touchedRows.Keys.ToHashSet();
                var replaced = install.Where(pair => pair.Value.Row.Status == AssetPackageStatus.Conflict)
                    .Select(pair => pair.Key).ToHashSet();
                var pending = new Queue<AssetKey>(replaced);
                while (pending.TryDequeue(out var dependency))
                {
                    foreach (var consumer in preview.Dependencies.UsedBy(dependency))
                    {
                        var key = PackageKey(consumer.AssetType, consumer.Name);
                        if (!replaced.Add(key)) continue;
                        pending.Enqueue(key);
                        if (!positions.TryGetValue(key, out int index) || roots[index].Intent != LinkRootIntent.Owned)
                            continue;
                        var identity = new TargetZoneRowIdentity(Document.DocumentId, index);
                        if (!_drafts.TryGetValue(identity, out var state) || !state.IsChanged)
                            updateIndices.Add(index);
                    }
                }
                foreach (int index in updateIndices.Order())
                {
                    var root = roots[index];
                    var source = stagedRows[root.EntryId];
                    bool direct = touchedRows.TryGetValue(index, out var item);
                    var destination = root.Intent == LinkRootIntent.External ? item?.Destination : null;
                    var local = new WorkspaceAssetCatalogEntry(new TargetZoneRowIdentity(Document.DocumentId, index),
                        destination?.HasDefinition == true ? WorkspaceAssetOrigin.TargetResolvedReference : source.Origin,
                        source.Access, destination?.HasDefinition == true ? WorkspaceAssetContentSource.ResolvedProvider : source.ContentSource,
                        source.AssetType, source.OriginalName, source.NormalizedName, destination?.Definition ?? source.Definition,
                        source.RawHeader, source.HeaderKind, destination?.ResolvedProvider, destination?.ProviderZone);
                    DraftState? draft = null;
                    if (root.Intent == LinkRootIntent.Owned && local.Definition is { } def &&
                        (adapters.TryGetAdapter(local.AssetType, out var adapter) || local.AssetType == XAssetType.Techset))
                        draft = new DraftState(local, adapter ?? adapters.AdapterForNewDefinition(def));
                    updates.Add((local, draft, direct));
                }
                var authored = _authoredAssets.WithoutProviders(install.Keys).WithHighestPrecedencePool(selected);
                Publish(authored, roots, publishedProviderKeys: install.Keys, validatePackageOrder: true);
                _hasPackageImport = true;
                SetNativeStreamPackageBase(streams);
                foreach (var (entry, draft, direct) in updates)
                {
                    var id = entry.TargetRowIdentity!.Value;
                    if (id.SerializedIndex < Document.Rows.Count)
                    {
                        Document.ReplaceRow(id, entry);
                        if (direct && !_addedRows.ContainsKey(id) && !_addedReferenceRows.ContainsKey(id))
                            _promotedReferenceRows.TryAdd(id, _revision.Revision);
                    }
                    else
                    {
                        Document.AppendPackageRow(entry);
                        _addedRows.Add(id, _revision.Revision);
                    }
                    if (draft is not null) _drafts[id] = draft;
                    else _drafts.Remove(id);
                }
                foreach (var key in install.Keys)
                {
                    _packageDefinitions[key] = definitions[key];
                    _packageDefinitionOwners[key] = staged.Zone;
                    if (!positions.ContainsKey(key)) _changedPackageProviders.Add(key);
                }
                if (install.Count != 0)
                {
                    _packageZones.Add(staged);
                    foreach (var (key, definition) in definitions)
                    {
                        if (definition is (GfxImageAsset or SoundAliasListAsset) && _packageDefinitions.ContainsKey(key))
                            _packageStreamOwners.AddOrUpdate(definition, staged.Zone);
                    }
                }
                RebuildChangeSet();
            }
            finally
            {
                if (!_packageZones.Contains(staged)) staged.Dispose();
            }
        }
        NotifyTargetRowsChanged();
        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }
}
