using System.Runtime.CompilerServices;
using IW4.Game.Assets;
using IW4.Game.Assets.Menu;
using IW4.Game.Assets.XAnim;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Loaders.Database;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    private Dictionary<AssetKey, List<(BaseAsset Definition, LoadedXZone Owner)>>?
        _physicalPackageDefinitions;
    private readonly ConditionalWeakTable<LoadedXZone, Dictionary<AssetKey, BaseAsset>>
        _additionalPackageOriginals = new();
    private long _packageRowEntryRevision = -1;
    private Dictionary<AssetKey, WorkspaceAssetCatalogEntry> _packageRowEntries = [];

    /// <summary>Links selected rows and their available providers into a native subset package.</summary>
    public AssetPackageExportPreview PreviewAssetPackageExport(
        IReadOnlyList<WorkspaceAssetCatalogEntry> selections,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0)
            throw new ArgumentException("Select at least one asset row.", nameof(selections));
        return PreviewAssetPackageExport(selections, null, token);
    }

    /// <summary>Exports one resolved Menu definition as the package's selected root.</summary>
    public AssetPackageExportPreview PreviewMenuPackageExport(MenuDefAsset menu, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return PreviewAssetPackageExport([], menu, token);
    }

    private AssetPackageExportPreview PreviewAssetPackageExport(IReadOnlyList<WorkspaceAssetCatalogEntry> selections,
        MenuDefAsset? selectedMenu, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposedCore();
            var rows = new List<AssetPackageRow>();
            try
            {
                return BuildAssetPackageExport(selections, rows, token, selectedMenu);
            }
            catch (Exception exception) when (exception is
                InvalidDataException or InvalidOperationException or NotSupportedException or
                ArgumentException or OverflowException or IOException or KeyNotFoundException)
            {
                var first = selections.FirstOrDefault();
                rows.Add(new AssetPackageRow(null, selectedMenu?.SerializedAssetType ?? first?.AssetType ?? XAssetType.PhysPreset,
                    selectedMenu?.Window.Name ?? first?.OriginalName ?? first?.NormalizedName ?? "Selected rows",
                    AssetPackageStatus.Blocked, exception.Message, []));
                return new AssetPackageExportPreview(rows, bytes: null);
            }
        }
    }

    private AssetPackageExportPreview BuildAssetPackageExport(
        IReadOnlyList<WorkspaceAssetCatalogEntry> selections,
        List<AssetPackageRow> rows,
        CancellationToken token,
        MenuDefAsset? selectedMenu)
    {
        ZoneLinkRequest current = _revision.LinkRequest;
        var currentFullKeys = current.Assets.Providers
            .Where(provider => !provider.IsReferencePlaceholder)
            .Select(provider => provider.Key).ToHashSet();
        var currentFrozenProviders = current.Assets.Providers
            .Where(provider => !provider.IsReferencePlaceholder)
            .GroupBy(provider => provider.Key)
            .ToDictionary(group => group.Key, group => group.First());
        bool CanReuseCurrent(AssetKey key, BaseAsset definition,
            LoadedXZone? owner) =>
            !ReferenceEquals(definition, selectedMenu) &&
            definition.SerializedAssetType != XAssetType.XModelSurfs &&
            currentFrozenProviders.ContainsKey(key) &&
            ((owner is not null && !Workspace.IsBlank &&
              ReferenceEquals(owner, Workspace.LoadedZone)) ||
             (_packageDefinitions.TryGetValue(key, out BaseAsset? packaged) &&
              ReferenceEquals(packaged, definition) &&
              _packageDefinitionOwners.TryGetValue(key, out LoadedXZone? packagedOwner) &&
              ReferenceEquals(packagedOwner, owner)) ||
             (owner is null && _compiledAuxiliaryProviders.Values.Any(assets =>
                 assets.TryGetValue(key, out BaseAsset? auxiliary) &&
                 ReferenceEquals(auxiliary, definition))));
        var currentOwnedKeys = current.Roots
            .Where(root => root.Intent == LinkRootIntent.Owned && root.Asset.HasValue)
            .Select(root => root.Asset!.Value).ToHashSet();
        var physicalOwnedKeys = new Dictionary<LoadedXZone, HashSet<AssetKey>>(
            ReferenceEqualityComparer.Instance);
        bool HasSourceRoot(NativeStreamProviderInput input)
        {
            if (input.PhysicalOwner is null ||
                (!Workspace.IsBlank &&
                 ReferenceEquals(input.PhysicalOwner, Workspace.LoadedZone)))
                return currentOwnedKeys.Contains(input.Key);
            if (!physicalOwnedKeys.TryGetValue(input.PhysicalOwner, out var keys))
            {
                keys = input.PhysicalOwner.FreezeLinkRoots()
                    .Where(root => root.Intent == LinkRootIntent.Owned && root.Asset.HasValue)
                    .Select(root => root.Asset!.Value).ToHashSet();
                physicalOwnedKeys.Add(input.PhysicalOwner, keys);
            }
            return keys.Contains(input.Key);
        }
        var selectedRoots = new List<AssetPackageRoot>(selections.Count + (selectedMenu is null ? 0 : 1));
        var nativeRoots = new List<LinkRoot>(selectedRoots.Capacity);
        var explicitProviders = new Dictionary<AssetKey, (BaseAsset Definition, LoadedXZone? Owner)>();
        var explicitKeys = new HashSet<AssetKey>();
        var includedOwners = new HashSet<LoadedXZone>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(AssetKey Key, XAssetType Type, string Reason)>();
        int rootOrdinal = 0;
        if (selectedMenu is not null)
        {
            string name = selectedMenu.SerializedAssetName ??
                throw new InvalidDataException("The selected Menu has no serialized name.");
            if (IsReferenceProvider(selectedMenu))
                throw new InvalidDataException("Load the fastfile that defines this Menu before exporting it.");
            AssetKey key = AssetKey.FromDefinition(selectedMenu);
            LoadedXZone? owner = OwnerFor(selectedMenu, key);
            selectedRoots.Add(new AssetPackageRoot(XAssetType.Menu, name, LinkRootIntent.Owned));
            nativeRoots.Add(new LinkRoot($"package:selected:{rootOrdinal++}",
                XAssetType.Menu, LinkRootIntent.Owned, key, name, null));
            explicitKeys.Add(key);
            explicitProviders.Add(key, (selectedMenu, owner));
            if (owner is not null)
                includedOwners.Add(owner);
            else if (!Workspace.IsBlank)
                includedOwners.Add(Workspace.LoadedZone);
            queue.Enqueue((key, XAssetType.Menu, "Selected Menu"));
        }
        foreach (WorkspaceAssetCatalogEntry selection in selections)
        {
            token.ThrowIfCancellationRequested();
            if (selection is null)
                throw new ArgumentException("Selected rows cannot contain null.", nameof(selections));
            LinkRoot? documentRoot = null;
            if (selection.TargetRowIdentity is { } identity)
            {
                if (identity.DocumentId != Document.DocumentId ||
                    !Document.TryGetRow(identity, out WorkspaceAssetCatalogEntry? currentEntry) ||
                    !ReferenceEquals(selection, currentEntry) ||
                    identity.SerializedIndex >= current.Roots.Count)
                    throw new InvalidOperationException("A selected row no longer belongs to this target.");
                documentRoot = current.Roots[identity.SerializedIndex];
            }
            else if (selection.DependencyIdentity is not { DocumentId: var documentId } ||
                     documentId != Document.DocumentId)
                throw new InvalidOperationException("A selected provider belongs to another target.");

            XAssetType type = documentRoot?.SerializedType ?? selection.AssetType;
            if (documentRoot?.Intent is LinkRootIntent.Null or LinkRootIntent.OpaqueNative)
            {
                selectedRoots.Add(new AssetPackageRoot(type, null,
                    documentRoot.Intent, documentRoot.OpaqueHeader));
                nativeRoots.Add(new LinkRoot($"package:selected:{rootOrdinal++}", type,
                    documentRoot.Intent, null, null, documentRoot.OpaqueHeader));
                rows.Add(new AssetPackageRow(null, type, documentRoot.Intent.ToString(),
                    AssetPackageStatus.Included, "Selected structural row", []));
                continue;
            }

            string wireName = documentRoot?.OriginalSerializedName ??
                selection.OriginalName ?? selection.NormalizedName ??
                throw new InvalidDataException("Selected asset has no wire name.");
            LinkRootIntent selectedIntent = documentRoot?.Intent ??
                (selection.IsReferencePlaceholder ? LinkRootIntent.External : LinkRootIntent.Owned);
            if (selectedIntent == LinkRootIntent.External && !wireName.StartsWith(','))
                wireName = "," + wireName;
            if (selectedIntent == LinkRootIntent.Owned && wireName.StartsWith(','))
                wireName = wireName.TrimStart(',');
            AssetKey key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(type), wireName);
            if (!explicitKeys.Add(key))
                throw new InvalidDataException($"Selected rows repeat provider {key}.");
            selectedRoots.Add(new AssetPackageRoot(type, wireName, selectedIntent));

            bool preferTarget = selection.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition;
            (BaseAsset? definition, LoadedXZone? owner) =
                ResolveAssetPackageAsset(key, selection, preferTarget);
            if (definition is not null && !IsReferenceProvider(definition))
            {
                explicitProviders.Add(key, (definition, owner));
                if (owner is not null)
                    includedOwners.Add(owner);
                else if (!Workspace.IsBlank)
                    includedOwners.Add(Workspace.LoadedZone);
                nativeRoots.Add(new LinkRoot($"package:selected:{rootOrdinal++}",
                    definition.SerializedAssetType, LinkRootIntent.Owned, key,
                    definition.SerializedAssetName, null));
                queue.Enqueue((key, definition.SerializedAssetType, "Selected asset"));
            }
            else
            {
                if (!ZoneLinker.SupportsExternalReference(type))
                    throw new InvalidDataException($"{type} cannot be carried as an external row.");
                string externalName = wireName.StartsWith(',') ? wireName : "," + wireName;
                nativeRoots.Add(new LinkRoot($"package:selected:{rootOrdinal++}",
                    type, LinkRootIntent.External, key, externalName, null));
                queue.Enqueue((key, type, "Selected external reference"));
            }
        }

        var providers = new Dictionary<AssetKey, NativeStreamProviderInput>();
        var members = new Dictionary<AssetKey, AssetPackageMember>();
        var visited = new HashSet<AssetKey>();
        var notifyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LinkAssetPool? frozenPool = null;
        void ExpandQueue()
        {
            while (queue.Count != 0)
            {
                var batch = new List<LinkAssetProviderSource>();
                var full = new Dictionary<AssetKey, string>();
                var reuseKeys = new HashSet<AssetKey>();
                int waveCount = queue.Count;
                for (int index = 0; index < waveCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    (AssetKey key, XAssetType type, string reason) = queue.Dequeue();
                    if (!visited.Add(key))
                        continue;
                    (BaseAsset? definition, LoadedXZone? owner) =
                        explicitProviders.TryGetValue(key, out var selected)
                            ? selected
                            : ResolveAssetPackageAsset(key, null, preferTarget: true);
                    bool stagedOwned = _packageDefinitions.ContainsKey(key) &&
                        currentFullKeys.Contains(key);
                    if (definition is null || IsReferenceProvider(definition) ||
                        (owner is not null && !includedOwners.Contains(owner) &&
                         !stagedOwned))
                    {
                        if (PhysicalDefinitions().TryGetValue(key, out var references))
                        {
                            var reference = references.FirstOrDefault(item =>
                                includedOwners.Contains(item.Owner) &&
                                IsReferenceProvider(item.Definition));
                            if (reference.Definition is not null)
                            {
                                LinkAssetProviderSource source = CreateAssetPackageSource(
                                    key, reference.Definition, reference.Owner);
                                batch.Add(source);
                                providers.Add(key, new NativeStreamProviderInput(
                                    key, reference.Definition, reference.Owner, source));
                            }
                        }
                        members.Add(key, new AssetPackageMember(type,
                            "," + key.NormalizedName, false));
                        rows.Add(new AssetPackageRow(key, type, key.NormalizedName,
                            AssetPackageStatus.Required,
                            $"{reason}; provider remains external", []));
                        continue;
                    }

                    LinkAssetProviderSource included = CreateAssetPackageSource(
                        key, definition, owner);
                    batch.Add(included);
                    providers.Add(key, new NativeStreamProviderInput(
                        key, definition, owner, included));
                    full.Add(key, reason);
                    if (CanReuseCurrent(key, definition, owner))
                    {
                        batch.RemoveAt(batch.Count - 1);
                        reuseKeys.Add(key);
                    }
                }
                LinkAssetProvider[] newProviders = [];
                if (batch.Count != 0)
                {
                    frozenPool = frozenPool is null
                        ? new LinkAssetPool(batch)
                        : frozenPool.WithHighestPrecedenceProviders(batch);
                    newProviders = frozenPool.Providers.Take(batch.Count).ToArray();
                }
                if (reuseKeys.Count != 0)
                {
                    var reused = current.Assets.ForKeys(reuseKeys);
                    frozenPool = frozenPool is null
                        ? reused
                        : frozenPool.WithHighestPrecedencePool(reused);
                }
                foreach (LinkAssetProvider provider in newProviders.Concat(
                    reuseKeys.Select(key => currentFrozenProviders[key])))
                {
                    if (!full.TryGetValue(provider.Key, out string? reason))
                        continue;
                    members.Add(provider.Key, new AssetPackageMember(
                        provider.SerializedType, provider.OriginalSerializedName, true));
                    rows.Add(new AssetPackageRow(provider.Key, provider.SerializedType,
                        provider.OriginalSerializedName, AssetPackageStatus.Included,
                        reason, []));
                    foreach ((XAssetType dependencyType, string dependencyName, _) in
                             provider.CaptureDependencies(token))
                    {
                        AssetKey dependency = AssetKey.FromWireName(
                            CanonicalAssetFamily.FromSerializedType(dependencyType),
                            dependencyName);
                        queue.Enqueue((dependency, dependencyType,
                            $"Used by {provider.SerializedType} '{provider.OriginalSerializedName}'"));
                    }
                    if (providers[provider.Key].Definition is XAnimPartsAsset animation)
                        foreach (string name in animation.Notify.Select(item => item.Name.Text)
                                     .Where(name => !string.IsNullOrWhiteSpace(name))!)
                            notifyNames.Add(name);
                }
            }
        }

        // Animation notifies name sound aliases indirectly. Include only an
        // unambiguous available sound with the exact notify basename.
        var processedNotifies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            ExpandQueue();
            bool queued = false;
            foreach (string notify in notifyNames.Where(processedNotifies.Add))
            {
                AssetKey[] matches = PhysicalDefinitions().Keys
                    .Concat(_packageDefinitions.Keys)
                    .Where(key => key.Family.Type == XAssetType.Sound &&
                        key.NormalizedName.EndsWith("/" + notify,
                            StringComparison.OrdinalIgnoreCase))
                    .Distinct().Take(2).ToArray();
                if (matches.Length != 1 || visited.Contains(matches[0]))
                    continue;
                queue.Enqueue((matches[0], XAssetType.Sound,
                    $"Animation notify '{notify}'"));
                queued = true;
            }
            if (!queued)
                break;
        }

        // Preserve source root topology. Embedded providers remain embedded;
        // only roots genuinely registered by their owner become native rows.
        int closureOrdinal = 0;
        foreach (NativeStreamProviderInput input in providers.Values)
        {
            if (explicitKeys.Contains(input.Key) || IsReferenceProvider(input.Definition) ||
                !HasSourceRoot(input))
                continue;
            nativeRoots.Add(new LinkRoot($"package:closure:{closureOrdinal++}",
                input.Definition.SerializedAssetType, LinkRootIntent.Owned,
                input.Key, input.Definition.SerializedAssetName, null));
        }

        NativeStreamPackageBuildResult streams = NativeStreamPackageBuilder.Repackage(
            providers.Values.ToArray(), current.LanguageMask);
        LinkAssetPool pool = frozenPool ?? new LinkAssetPool([]);
        if (streams.Sources.Count != 0)
        {
            var changedKeys = streams.Sources.Select(source => AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(source.Definition.SerializedAssetType),
                source.SerializedName ?? source.Definition.SerializedAssetName!)).ToArray();
            pool = pool.WithoutProviders(changedKeys)
                .WithHighestPrecedenceProviders(streams.Sources);
        }
        var nativeRequirements = pool.Providers
            .Where(provider => !provider.IsReferencePlaceholder)
            .SelectMany(provider => provider.CaptureNativeDependencies(token))
            .Where(dependency => !dependency.HasExternalClaim)
            .Select(dependency => (dependency.Type, Key: AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(dependency.Type), dependency.Name)))
            .DistinctBy(dependency => dependency.Key);
        var availableProviderKeys = pool.Providers.Select(provider => provider.Key).ToHashSet();
        var publishedRootKeys = nativeRoots.Where(root => root.Asset.HasValue)
            .Select(root => root.Asset!.Value).ToHashSet();
        int externalOrdinal = 0;
        foreach (var dependency in nativeRequirements)
        {
            if (members.TryGetValue(dependency.Key, out var member) &&
                member.Included)
                continue;
            if (availableProviderKeys.Contains(dependency.Key) ||
                publishedRootKeys.Contains(dependency.Key))
                continue;
            if (!ZoneLinker.SupportsExternalReference(dependency.Type))
                throw new InvalidDataException(
                    $"{dependency.Type} '{dependency.Key.NormalizedName}' requires a full provider.");
            nativeRoots.Add(new LinkRoot($"package:external:{externalOrdinal++}",
                dependency.Type, LinkRootIntent.External, dependency.Key,
                "," + dependency.Key.NormalizedName, null));
            publishedRootKeys.Add(dependency.Key);
        }
        var promotedRoots = new HashSet<AssetKey>();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var request = ZoneLinker.ProjectDependencyRootOrder(new ZoneLinkRequest(
                pool, nativeRoots, current.LanguageMask,
                current.SelectedLanguageMask, current.ScriptStrings));
            ZoneLinkResult link = new ZoneLinker().Link(request);
            if (!link.Succeeded || link.DecodedBytes is not { } decoded)
                throw new InvalidDataException("Asset package link failed: " +
                    string.Join("; ", link.Errors));
            FastFilePackagingResult fastFile = new FastFilePackager().PackageGreenfield(
                decoded, link.LanguageMask, link.SelectedLanguageMask,
                link.ImageStreamLanguageTables,
                headerMetadata: Workspace.IsBlank
                    ? DbHeaderAuthoringMetadata.Canonical
                    : DbHeaderAuthoringMetadata.FromHeader(Workspace.LoadedZone.Header));
            if (!fastFile.Succeeded || fastFile.Bytes is not { } bytes)
                throw new InvalidDataException("Asset package container failed: " +
                    string.Join("; ", fastFile.Errors.Select(error => error.Message)));

            byte[] fastFileBytes = bytes.ToArray();
            using var reopened = new AssetPackageZone(fastFileBytes,
                streams.ImagePackage, streams.SoundPackage);
            var emitted = reopened.Request.Assets.Providers
                .GroupBy(provider => provider.Key)
                .ToDictionary(group => group.Key,
                    group => group.FirstOrDefault(provider =>
                        !provider.IsReferencePlaceholder) ?? group.First());
            foreach (var (key, provider) in emitted)
                if (!members.TryGetValue(key, out var member) ||
                    member.Included == provider.IsReferencePlaceholder ||
                    member.Type != provider.SerializedType)
                    throw new InvalidDataException(
                        $"Native package provider {key} differs from its inventory.");
            var missingIncluded = members.Where(item => item.Value.Included &&
                    (!emitted.TryGetValue(item.Key, out var provider) ||
                     provider.IsReferencePlaceholder))
                .Select(item => item.Key).ToArray();
            if (missingIncluded.Length != 0)
            {
                foreach (AssetKey key in missingIncluded)
                {
                    if (!promotedRoots.Add(key))
                        throw new InvalidDataException(
                            $"Native package did not emit required provider {key}.");
                    NativeStreamProviderInput input = providers[key];
                    nativeRoots.Add(new LinkRoot($"package:named:{promotedRoots.Count}",
                        input.Definition.SerializedAssetType, LinkRootIntent.Owned,
                        key, input.Definition.SerializedAssetName, null));
                }
                continue;
            }
            using var archive = new MemoryStream();
            AssetPackageArchive.Write(archive, new AssetPackageContents(
                fastFileBytes, streams.ImagePackage, streams.SoundPackage,
                selectedRoots, members.Values.ToArray()));
            return new AssetPackageExportPreview(rows, archive.ToArray());
        }
    }

    private LinkAssetProviderSource CreateAssetPackageSource(
        AssetKey key, BaseAsset definition, LoadedXZone? owner)
    {
        if (definition.SerializedAssetType == XAssetType.XModelSurfs)
            return new LinkAssetProviderSource(definition).AsAuthoredDetached();
        if (owner is null)
            return new LinkAssetProviderSource(definition).AsAuthoredDetached();
        BaseAsset imported = PhysicalDefinitions().TryGetValue(key, out var physical) &&
            physical.Any(item => ReferenceEquals(item.Owner, owner) &&
                ReferenceEquals(item.Definition, definition))
                ? definition
                : OriginalFor(owner, key) ?? definition;
        return LoadedZoneLinkExtensions.CreateCapturedProviderSource(
            owner, definition, imported, definition.SerializedAssetName,
            LinkAssetProviderSourceDisposition.AuthoredDetached);
    }

    private BaseAsset? OriginalFor(LoadedXZone owner, AssetKey key)
    {
        if (PhysicalDefinitions().TryGetValue(key, out var physical))
        {
            var source = physical.FirstOrDefault(item =>
                ReferenceEquals(item.Owner, owner));
            if (source.Definition is not null)
                return source.Definition;
        }
        if (!_additionalPackageOriginals.TryGetValue(owner, out var indexed))
        {
            indexed = (owner.Observation?.Events ?? [])
                .OfType<ZoneProviderRegistered>()
                .Select(registration => registration.Provider)
                .Where(asset => asset.SerializedAssetName is not null)
                .GroupBy(AssetKey.FromDefinition)
                .ToDictionary(group => group.Key, group => group.First());
            _additionalPackageOriginals.Add(owner, indexed);
        }
        return indexed.GetValueOrDefault(key);
    }

    /// <summary>
    /// Resolves current semantic content by the link request's provider authority,
    /// rather than by whichever provider happens to be active in the runtime pool.
    /// An explicit pool selection can opt into its exact physical provider.
    /// </summary>
    private BaseAsset? ResolveAssetPackageDefinition(
        AssetKey key,
        WorkspaceAssetCatalogEntry? selection = null,
        bool preferTarget = true) =>
        ResolveAssetPackageAsset(key, selection, preferTarget).Definition;

    private LoadedXZone? ResolveAssetPackageOwner(
        AssetKey key,
        WorkspaceAssetCatalogEntry? selection = null,
        bool preferTarget = true) =>
        ResolveAssetPackageAsset(key, selection, preferTarget).Owner;

    private (BaseAsset? Definition, LoadedXZone? Owner) ResolveAssetPackageAsset(
        AssetKey key,
        WorkspaceAssetCatalogEntry? selection,
        bool preferTarget)
    {
        if (selection?.TargetRowIdentity is { } row)
        {
            if (!Document.TryGetRow(row, out WorkspaceAssetCatalogEntry? current) ||
                !ReferenceEquals(current, selection))
                throw new InvalidOperationException("The selected row no longer belongs to this target.");
            if (selection.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition)
            {
                BaseAsset definition = CurrentPackageDefinitionForRow(selection);
                LoadedXZone? owner = _packageDefinitionOwners.GetValueOrDefault(key) ??
                    OwnerFor(selection.Definition ?? definition, key);
                if (owner is null && PhysicalDefinitions().TryGetValue(key, out var capturedTarget) &&
                    !Workspace.IsBlank &&
                    capturedTarget.Any(item => ReferenceEquals(item.Owner, Workspace.LoadedZone)))
                    owner = Workspace.LoadedZone;
                return (definition, owner);
            }
        }
        if (selection?.DependencyIdentity is { } dependency)
        {
            if (dependency.DocumentId != Document.DocumentId)
                throw new InvalidOperationException("The selected provider belongs to another target.");
            if (!preferTarget && selection.Definition is { } selected &&
                !IsReferenceProvider(selected))
            {
                BaseAsset definition = _providerDrafts.TryGetValue(dependency, out DraftState? draft) &&
                    IsCurrentProviderDraft(draft)
                        ? draft.CreateCurrentDefinition()
                        : selected;
                return (definition, OwnerFor(selected, key));
            }
        }

        if (CurrentPackageRowEntries().TryGetValue(key, out var authoredRow))
        {
            BaseAsset definition = CurrentPackageDefinitionForRow(authoredRow);
            LoadedXZone? owner = _packageDefinitionOwners.GetValueOrDefault(key) ??
                OwnerFor(authoredRow.Definition ?? definition, key);
            return (definition, owner);
        }

        if (_packageDefinitions.TryGetValue(key, out BaseAsset? imported))
        {
            _packageDefinitionOwners.TryGetValue(key, out LoadedXZone? owner);
            return (imported, owner);
        }
        foreach (Dictionary<AssetKey, BaseAsset> auxiliaries in _compiledAuxiliaryProviders.Values)
        {
            if (auxiliaries.TryGetValue(key, out BaseAsset? compiled) &&
                !IsReferenceProvider(compiled))
                return (compiled, OwnerFor(compiled, key));
        }

        bool currentFullProvider = _revision.LinkRequest.Assets.Providers.Any(provider =>
            provider.Key == key && !provider.IsReferencePlaceholder);
        bool targetFullProvider = !_maskedTargetBaseProviderKeys.Contains(key) &&
            _targetBaseAssets.Providers.Any(provider =>
                provider.Key == key && !provider.IsReferencePlaceholder);
        if (preferTarget && currentFullProvider && targetFullProvider &&
            PhysicalDefinitions().TryGetValue(key, out var captured))
        {
            var target = captured.FirstOrDefault(item => ReferenceEquals(
                item.Owner, Workspace.LoadedZone));
            if (target.Definition is not null)
                return target;
        }

        if (selection?.Definition is { } loaded && !IsReferenceProvider(loaded))
            return (loaded, OwnerFor(loaded, key));
        if (PhysicalDefinitions().TryGetValue(key, out var available))
        {
            var full = available.FirstOrDefault(item => !IsReferenceProvider(item.Definition));
            if (full.Definition is not null)
                return full;
        }
        return (null, null);
    }

    private LoadedXZone? OwnerFor(BaseAsset definition, AssetKey key)
    {
        if (_packageDefinitionOwners.TryGetValue(key, out LoadedXZone? importedOwner) &&
            _packageDefinitions.TryGetValue(key, out BaseAsset? imported) &&
            ReferenceEquals(imported, definition))
            return importedOwner;
        if (PhysicalDefinitions().TryGetValue(key, out var captured))
            return captured.FirstOrDefault(item => ReferenceEquals(item.Definition, definition)).Owner;
        return null;
    }

    private Dictionary<AssetKey, WorkspaceAssetCatalogEntry> CurrentPackageRowEntries()
    {
        if (_packageRowEntryRevision == _revision.Revision)
            return _packageRowEntries;
        _packageRowEntries = Document.Rows
            .Where(row => row.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                row.NormalizedName is not null && row.HasDefinition)
            .GroupBy(row => AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(row.AssetType),
                row.NormalizedName!))
            .ToDictionary(group => group.Key, group => group.First());
        _packageRowEntryRevision = _revision.Revision;
        return _packageRowEntries;
    }

    private BaseAsset CurrentPackageDefinitionForRow(WorkspaceAssetCatalogEntry entry) =>
        entry.TargetRowIdentity is { } identity &&
        _drafts.TryGetValue(identity, out DraftState? draft) && draft.IsChanged
            ? draft.CreateCurrentDefinition()
            : entry.Definition ?? throw new InvalidDataException(
                "A selected asset row has no semantic definition.");

    private Dictionary<AssetKey, List<(BaseAsset Definition, LoadedXZone Owner)>>
        PhysicalDefinitions()
    {
        if (_physicalPackageDefinitions is not null)
            return _physicalPackageDefinitions;
        var indexed = new Dictionary<AssetKey, List<(BaseAsset, LoadedXZone)>>();
        foreach (WorkspaceZone workspaceZone in Workspace.LoadedZones)
        {
            LoadedXZone zone = workspaceZone.LoadResult;
            foreach (ZoneProviderRegistered registration in
                     (zone.Observation?.Events ?? []).OfType<ZoneProviderRegistered>())
            {
                BaseAsset definition = registration.Provider;
                if (definition.SerializedAssetName is null)
                    continue;
                AssetKey key;
                try { key = AssetKey.FromDefinition(definition); }
                catch (ArgumentException) { continue; }
                if (!indexed.TryGetValue(key, out var values))
                    indexed.Add(key, values = []);
                values.Add((definition, zone));
            }
        }
        _physicalPackageDefinitions = indexed;
        return indexed;
    }
}
