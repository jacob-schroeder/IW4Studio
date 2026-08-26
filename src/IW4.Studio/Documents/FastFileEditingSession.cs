using IW4.Assets.Assets;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Assets.D3dbsp;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.D3dbsp;

namespace IW4.Studio.Documents;

/// <summary>
/// Exclusively owns one workspace and an immutable, revisioned semantic link
/// state. Mutable schema definitions are accepted only as transient provider
/// sources and are frozen before a revision is published.
/// </summary>
public sealed class FastFileEditingSession : IDisposable
{
    private readonly object _gate = new();
    private readonly LinkAssetPool _targetBaseAssets;
    private LinkAssetPool _authoredAssets;
    private IReadOnlySet<AssetKey> _maskedTargetBaseProviderKeys =
        new HashSet<AssetKey>();
    private readonly Dictionary<TargetZoneRowIdentity, DraftState> _drafts = [];
    private readonly Dictionary<TargetZoneRowIdentity, long> _addedRows = [];
    // Each group records every provider it requires; the authored subset lets
    // the last consumer withdraw providers managed by the D3DBSP lifecycle.
    private readonly Dictionary<string, IReadOnlySet<AssetKey>>
        _d3dbspProviderKeys = new(StringComparer.Ordinal);
    private readonly HashSet<AssetKey> _d3dbspAuthoredProviderKeys = [];
    // Generated dependency closures are providers, not document rows. Their
    // ownership is revision workflow state so a later compiled root can
    // withdraw only its own prior auxiliaries.
    private readonly Dictionary<TargetZoneRowIdentity, Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>> _compiledAuxiliaryProviders = [];
    private readonly CancellationTokenSource _cancellation = new();
    private AssetChangeSet _changeSet = new([]);
    private FastFileSaveRevision _revision;
    private bool _disposed;

    public FastFileEditingSession(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.ThrowIfDisposed();
        Workspace = workspace;
        Document = new TargetZoneDocument(workspace);
        _targetBaseAssets = workspace.InitialLinkRequest.Assets;
        _authoredAssets = workspace.InitialLinkRequest.Assets.WithoutProviders(
            workspace.InitialLinkRequest.Assets.Providers.Select(provider => provider.Key));
        _revision = new FastFileSaveRevision(
            Revision: 0,
            SourcePath: workspace.Document.SourcePathOrNull,
            LinkRequest: workspace.InitialLinkRequest);
        AssetAuthoringAdapterRegistry adapters =
            AssetAuthoringAdapterRegistry.CreateDefault();
        foreach (WorkspaceAssetCatalogEntry entry in Document.Rows)
            AddInitialDraft(entry, adapters);
        workspace.ClaimEditingSession(this);
    }

    public FastFileWorkspace Workspace { get; }
    public TargetZoneDocument Document { get; }

    public CancellationToken CancellationToken => _cancellation.Token;

    public event EventHandler? TargetRowsChanged;

    /// <summary>Raised after a detached authored definition is published.</summary>
    public event EventHandler? AppliedAssetsChanged;

    internal void NotifyTargetRowsChanged() => TargetRowsChanged?.Invoke(this, EventArgs.Empty);

    internal WorkspaceAssetCatalogEntry AddAsset(
        IW4.Assets.Assets.BaseAsset definition,
        IAssetAuthoringAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.AssetType != definition.SerializedAssetType)
        {
            throw new ArgumentException(
                $"The supplied adapter handles {adapter.AssetType}, not " +
                $"{definition.SerializedAssetType}.",
                nameof(adapter));
        }

        IW4.Assets.Assets.BaseAsset detachedDefinition = adapter.CreateDefinition(
            adapter.CloneDraft(adapter.CreateDraft(definition)));
        if (detachedDefinition.SerializedAssetType != definition.SerializedAssetType)
        {
            throw new InvalidDataException(
                "The authoring adapter changed the new asset's serialized type.");
        }

        WorkspaceAssetCatalogEntry entry;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            string? nameError = ValidateNewAssetName(
                detachedDefinition.SerializedAssetType,
                detachedDefinition.SerializedAssetName);
            if (nameError is not null)
                throw new ArgumentException(nameError, nameof(definition));
            IW4.Linker.Contracts.AssetKey key = IW4.Linker.Contracts.AssetKey.FromDefinition(detachedDefinition);
            var source = new LinkAssetProviderSource(detachedDefinition).AsAuthoredDetached();
            LinkRoot[] roots = [.. _revision.LinkRequest.Roots, new LinkRoot(
                $"authored:{Guid.NewGuid():N}",
                detachedDefinition.SerializedAssetType,
                LinkRootIntent.Owned,
                key,
                detachedDefinition.SerializedAssetName,
                opaqueHeader: null)];
            LinkAssetPool authoredAssets = _authoredAssets
                .WithoutProviders([key])
                .WithHighestPrecedenceProviders([source]);
            Publish(authoredAssets, roots, publishedProviderKeys: [key]);
            entry = Document.AppendDefinition(detachedDefinition);
            _drafts.Add(entry.TargetRowIdentity!.Value, new DraftState(entry, adapter));
            _addedRows.Add(entry.TargetRowIdentity.Value, _revision.Revision);
            RebuildChangeSet();
        }
        NotifyTargetRowsChanged();
        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    /// <summary>
    /// Publishes all assets compiled from one D3DBSP in one revision. Existing
    /// same-name map rows are replaced in place and missing top-level map rows
    /// are appended; MapEnts remains a nested provider unless the document
    /// already exposes it as a row.
    /// </summary>
    public async Task<D3dbspWorkspaceImportResult> ImportD3dbspAsync(
        string inputPath,
        string assetName,
        bool forceFullbright,
        int fragmentProgramUploadCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        IReadOnlyList<XModelAsset> availableXModels =
            CaptureAvailableD3dbspXModels();
        D3dbspLinkResult linked = await Task.Run(() =>
            D3dbspAssetLinker.Link(new D3dbspLinkRequest(
                inputPath,
                assetName,
                forceFullbright,
                fragmentProgramUploadCapacity,
                availableXModels)));
        return ImportD3dbsp(linked);
    }

    private IReadOnlyList<XModelAsset> CaptureAvailableD3dbspXModels()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            var modelsByKey = new Dictionary<AssetKey, XModelAsset>();
            foreach (WorkspaceAssetCatalogEntry entry in Document.Rows.Where(entry =>
                         entry.AssetType == XAssetType.XModel &&
                         entry.Definition is XModelAsset))
            {
                if (CurrentDefinitionForEntry(entry) is XModelAsset model)
                    modelsByKey.TryAdd(AssetKey.FromDefinition(model), model);
            }

            if (!Workspace.IsBlank)
            {
                foreach (var slot in Workspace.LoadedZone.Context.AssetPool.Slots.Where(slot =>
                             slot.AssetType == XAssetType.XModel &&
                             !slot.ActiveProvider.IsReferencePlaceholder))
                {
                    if (slot.ActiveProvider.Asset is XModelAsset model)
                        modelsByKey.TryAdd(AssetKey.FromDefinition(model), model);
                }
            }

            return Array.AsReadOnly(modelsByKey.Values.ToArray());
        }
    }

    private D3dbspWorkspaceImportResult ImportD3dbsp(
        D3dbspLinkResult linked)
    {
        ArgumentNullException.ThrowIfNull(linked);
        Dictionary<XAssetType, BaseAsset> definitionsByType =
            RequireD3dbspDefinitions(linked);
        string assetName = definitionsByType[XAssetType.GfxMap]
            .SerializedAssetName!;
        string normalizedName = AssetKey.FromDefinition(
            definitionsByType[XAssetType.GfxMap]).NormalizedName;
        StringTableAsset generatedConfigStringBaseline =
            D3dbspAssetLinker.CreatePs3DmConfigStringBaseline(
                assetName,
                linked.Checksum);
        AssetKey configStringKey = AssetKey.FromDefinition(
            generatedConfigStringBaseline);

        WorkspaceAssetCatalogEntry[] importedRows;
        int addedRowCount;
        int replacedRowCount;
        long revision;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            WorkspaceAssetCatalogEntry[] existingRows = Document.Rows
                .Where(entry =>
                    D3dbspAssetTypeFacts.IsMultiplayerType(entry.AssetType) &&
                    string.Equals(
                        entry.NormalizedName,
                        normalizedName,
                        StringComparison.Ordinal))
                .ToArray();
            if (existingRows.Any(entry =>
                    entry.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition ||
                    entry.Access != WorkspaceAssetAccess.Editable ||
                    entry.Definition is null))
            {
                throw new InvalidOperationException(
                    $"The D3DBSP group '{assetName}' collides with a target row that is not an editable owned definition.");
            }
            IGrouping<XAssetType, WorkspaceAssetCatalogEntry>? duplicateRows =
                existingRows.GroupBy(entry => entry.AssetType)
                    .FirstOrDefault(group => group.Count() != 1);
            if (duplicateRows is not null)
            {
                throw new InvalidDataException(
                    $"The D3DBSP group '{assetName}' has multiple {duplicateRows.Key} target rows.");
            }
            WorkspaceAssetCatalogEntry? spellingMismatch = existingRows
                .FirstOrDefault(entry => !string.Equals(
                    entry.OriginalName,
                    assetName,
                    StringComparison.Ordinal));
            if (spellingMismatch is not null)
            {
                throw new InvalidOperationException(
                    $"The existing group uses wire name '{spellingMismatch.OriginalName}'. Import with that exact spelling to replace it.");
            }

            WorkspaceAssetCatalogEntry[] configStringRows = Document.Rows
                .Where(entry =>
                    entry.AssetType == XAssetType.StringTable &&
                    string.Equals(
                        entry.NormalizedName,
                        configStringKey.NormalizedName,
                        StringComparison.Ordinal))
                .ToArray();
            if (configStringRows.Length > 1)
            {
                throw new InvalidDataException(
                    $"The D3DBSP group '{assetName}' has multiple PS3 deathmatch configstring baseline rows.");
            }
            WorkspaceAssetCatalogEntry? configStringRow =
                configStringRows.SingleOrDefault();
            if (configStringRow is not null &&
                (configStringRow.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition ||
                 configStringRow.Access != WorkspaceAssetAccess.Editable ||
                 configStringRow.Definition is null))
            {
                throw new InvalidOperationException(
                    $"The PS3 deathmatch configstring baseline for '{assetName}' collides with a target row that is not an editable owned definition.");
            }

            Dictionary<XAssetType, WorkspaceAssetCatalogEntry> existingByType =
                existingRows.ToDictionary(entry => entry.AssetType);
            BaseAsset[] addedMapDefinitions = D3dbspAssetTypeFacts.MultiplayerTypes
                .Where(assetType =>
                    assetType != XAssetType.MapEnts &&
                    !existingByType.ContainsKey(assetType))
                .Select(assetType => definitionsByType[assetType])
                .ToArray();
            var pending = new List<(DraftState State, object Candidate)>();
            foreach ((XAssetType assetType, WorkspaceAssetCatalogEntry entry) in
                     existingByType)
            {
                DraftState state = RequireDraft(entry.TargetRowIdentity!.Value);
                pending.Add((
                    state,
                    state.Adapter.CreateDraft(definitionsByType[assetType])));
            }
            StringTableAsset configStringDefinition;
            if (configStringRow is null)
            {
                configStringDefinition = generatedConfigStringBaseline;
            }
            else
            {
                StringTableAsset currentConfigString =
                    CurrentDefinitionForEntry(configStringRow) as StringTableAsset
                    ?? throw new InvalidDataException(
                        $"The configstring row for '{assetName}' is not a StringTable definition.");
                configStringDefinition =
                    D3dbspAssetLinker.RefreshPs3DmConfigStringBaseline(
                        currentConfigString,
                        assetName,
                        linked.Checksum);
                DraftState state = RequireDraft(
                    configStringRow.TargetRowIdentity!.Value);
                pending.Add((
                    state,
                    state.Adapter.CreateDraft(configStringDefinition)));
            }
            BaseAsset[] addedDefinitions = configStringRow is null
                ? [.. addedMapDefinitions, configStringDefinition]
                : addedMapDefinitions;

            IReadOnlySet<AssetKey> priorProviderKeys =
                _d3dbspProviderKeys.TryGetValue(
                    normalizedName,
                    out IReadOnlySet<AssetKey>? prior)
                    ? prior
                    : new HashSet<AssetKey>();
            BaseAsset[] ownedProviders = definitionsByType.Values
                .Concat(linked.NestedAssets.Where(asset =>
                    !D3dbspAssetTypeFacts.IsMultiplayerType(
                        asset.SerializedAssetType)))
                .Append(configStringDefinition)
                .GroupBy(AssetKey.FromDefinition)
                .Select(group => group.First())
                .ToArray();
            AssetKey[] groupProviderKeys = ownedProviders
                .Select(AssetKey.FromDefinition)
                .Concat(linked.DependencyReferences.Select(
                    AssetKey.FromDefinition))
                .Distinct()
                .ToArray();
            HashSet<AssetKey> otherGroupProviderKeys = _d3dbspProviderKeys
                .Where(pair => !string.Equals(
                    pair.Key,
                    normalizedName,
                    StringComparison.Ordinal))
                .SelectMany(pair => pair.Value)
                .ToHashSet();
            AssetKey[] withdrawablePriorKeys = priorProviderKeys
                .Where(key =>
                    _d3dbspAuthoredProviderKeys.Contains(key) &&
                    !groupProviderKeys.Contains(key) &&
                    !otherGroupProviderKeys.Contains(key) &&
                    !IsCompiledProviderOwned(key))
                .ToArray();
            var availableKeys = _revision.LinkRequest.Assets.Providers
                .Select(provider => provider.Key)
                .Where(key => !withdrawablePriorKeys.Contains(key))
                .ToHashSet();
            availableKeys.UnionWith(
                ownedProviders.Select(AssetKey.FromDefinition));
            BaseAsset[] dependencyFallbacks = linked.DependencyReferences
                .Where(asset => availableKeys.Add(AssetKey.FromDefinition(asset)))
                .ToArray();
            BaseAsset[] publishedDefinitions = ownedProviders
                .Concat(dependencyFallbacks)
                .GroupBy(AssetKey.FromDefinition)
                .Select(group => group.First())
                .ToArray();
            AssetKey[] publishedKeys = publishedDefinitions
                .Select(AssetKey.FromDefinition)
                .ToArray();
            AssetKey[] replacedProviderKeys = withdrawablePriorKeys
                .Concat(ownedProviders.Select(AssetKey.FromDefinition))
                .Distinct()
                .ToArray();
            LinkAssetPool authoredAssets = _authoredAssets
                .WithoutProviders(replacedProviderKeys)
                .WithHighestPrecedenceProviders(publishedDefinitions.Select(
                    definition => new LinkAssetProviderSource(definition)
                        .AsAuthoredDetached()));
            LinkRoot[] roots =
            [
                .. _revision.LinkRequest.Roots,
                .. addedDefinitions.Select(definition => new LinkRoot(
                    $"d3dbsp:{Guid.NewGuid():N}:{definition.SerializedAssetType}",
                    definition.SerializedAssetType,
                    LinkRootIntent.Owned,
                    AssetKey.FromDefinition(definition),
                    definition.SerializedAssetName,
                    opaqueHeader: null))
            ];

            Publish(
                authoredAssets,
                roots,
                publishedProviderKeys: publishedKeys);
            foreach ((DraftState state, object candidate) in pending)
                state.SetCurrent(candidate, _revision.Revision);

            IReadOnlyList<WorkspaceAssetCatalogEntry> addedEntries =
                Document.AppendDefinitions(addedDefinitions);
            foreach (WorkspaceAssetCatalogEntry entry in addedEntries)
            {
                IAssetAuthoringAdapter adapter = RequireHostedAdapter(
                    entry.Definition!);
                _drafts.Add(
                    entry.TargetRowIdentity!.Value,
                    new DraftState(entry, adapter));
                _addedRows.Add(
                    entry.TargetRowIdentity.Value,
                    _revision.Revision);
            }

            _d3dbspProviderKeys[normalizedName] =
                new HashSet<AssetKey>(groupProviderKeys);
            _d3dbspAuthoredProviderKeys.ExceptWith(withdrawablePriorKeys);
            _d3dbspAuthoredProviderKeys.UnionWith(publishedKeys);
            HashSet<AssetKey> requiredD3dbspProviderKeys = _d3dbspProviderKeys
                .SelectMany(pair => pair.Value)
                .ToHashSet();
            _d3dbspAuthoredProviderKeys.IntersectWith(
                requiredD3dbspProviderKeys);
            RebuildChangeSet();
            importedRows = existingRows
                .Concat(configStringRow is null
                    ? []
                    : [configStringRow])
                .Concat(addedEntries)
                .OrderBy(entry => entry.TargetRowIdentity!.Value.SerializedIndex)
                .ToArray();
            addedRowCount = addedEntries.Count;
            replacedRowCount = existingRows.Length +
                (configStringRow is null ? 0 : 1);
            revision = _revision.Revision;
        }

        if (addedRowCount != 0)
            NotifyTargetRowsChanged();
        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return new D3dbspWorkspaceImportResult(
            revision,
            assetName,
            importedRows,
            addedRowCount,
            replacedRowCount,
            linked.DiscardedLightByteCount);
    }

    public void SelectRow(TargetZoneRowIdentity? identity)
    {
        if (identity is { } row)
            _ = Document.GetRow(row);
    }

    public long Revision
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedCore();
                return _revision.Revision;
            }
        }
    }

    public AssetChangeSet ChangeSet
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedCore();
                return _changeSet;
            }
        }
    }

    public bool IsDirty => !ChangeSet.IsEmpty;

    /// <summary>Validates one new hosted-definition name against live rows and the imported pool.</summary>
    public string? ValidateNewAssetName(XAssetType assetType, string? name)
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (!Enum.IsDefined(assetType))
                throw new ArgumentOutOfRangeException(nameof(assetType));
            if (string.IsNullOrWhiteSpace(name))
                return "Name is required.";
            if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
                return "Name cannot contain leading or trailing whitespace.";
            if (name[0] == ',')
                return "Name cannot begin with a comma because that spelling denotes an external reference.";
            if (name.Any(character => character == '\0' || character > byte.MaxValue))
                return "Name must be a Latin-1 string without embedded null characters.";
            if (name.Replace('\\', '/').Split('/').Any(segment => segment.Length == 0))
                return "Name cannot contain empty path segments.";

            CanonicalAssetFamily family =
                CanonicalAssetFamily.FromSerializedType(assetType);
            string normalized = AssetKey.FromWireName(family, name).NormalizedName;
            bool targetCollision = Document.Rows.Any(entry =>
                CanonicalAssetFamily.FromSerializedType(entry.AssetType) == family &&
                string.Equals(entry.NormalizedName, normalized, StringComparison.Ordinal));
            bool poolCollision = _revision.LinkRequest.Assets.Providers
                .Any(provider => provider.Key.Family == family && string.Equals(
                    provider.Key.NormalizedName,
                    normalized,
                    StringComparison.Ordinal));
            bool dependencyCollision = Workspace.AssetCatalog.DependencyEntries
                .Any(entry =>
                    entry.ProviderZone?.IsTarget != true &&
                    CanonicalAssetFamily.FromSerializedType(entry.AssetType) == family &&
                    string.Equals(entry.NormalizedName, normalized, StringComparison.Ordinal));
            return targetCollision || poolCollision || dependencyCollision
                ? $"An asset named '{name}' already exists in the workspace."
                : null;
        }
    }

    public AppliedAssetDefinitionsCapture CaptureAppliedAssets(
        IEnumerable<XAssetType> assetTypes)
    {
        HashSet<XAssetType> requested = ValidateCapturedAssetTypes(assetTypes);

        lock (_gate)
        {
            ThrowIfDisposedCore();
            AppliedAssetDefinition[] definitions = Document.Rows
                .Where(entry => entry.TargetRowIdentity is not null &&
                    requested.Contains(entry.AssetType) &&
                    _drafts.ContainsKey(entry.TargetRowIdentity.Value))
                .Select(entry =>
                {
                    TargetZoneRowIdentity identity = entry.TargetRowIdentity!.Value;
                    return new AppliedAssetDefinition(
                        identity,
                        _drafts[identity].CreateCurrentDefinition());
                })
                .ToArray();
            return new AppliedAssetDefinitionsCapture(_revision.Revision, definitions);
        }
    }

    public AppliedAssetDefinitionsCapture CaptureCurrentTargetAssets(
        IEnumerable<XAssetType> assetTypes)
    {
        HashSet<XAssetType> requested = ValidateCapturedAssetTypes(assetTypes);

        lock (_gate)
        {
            ThrowIfDisposedCore();
            AppliedAssetDefinition[] definitions = Document.Rows
                .Where(entry => entry.TargetRowIdentity is not null &&
                    entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                    entry.Access == WorkspaceAssetAccess.Editable &&
                    requested.Contains(entry.AssetType) &&
                    entry.Definition is not null)
                .Select(entry =>
                {
                    TargetZoneRowIdentity identity = entry.TargetRowIdentity!.Value;
                    return new AppliedAssetDefinition(
                        identity,
                        CurrentDefinitionForEntry(entry));
                })
                .ToArray();
            return new AppliedAssetDefinitionsCapture(_revision.Revision, definitions);
        }
    }

    /// <summary>
    /// Captures the synchronized assets represented by one owned D3DBSP wire
    /// name. MapEnts may be supplied by the ClipMap instead of a target row.
    /// </summary>
    public D3dbspWorkspaceAssetGroup CaptureD3dbspGroup(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        if (!D3dbspAssetTypeFacts.IsOwnedD3dbspGroupName(assetName))
        {
            throw new ArgumentException(
                "A D3DBSP group requires an owned wire name containing .d3dbsp.",
                nameof(assetName));
        }

        string normalizedName = AssetKey.FromWireName(
            CanonicalAssetFamily.FromSerializedType(XAssetType.GfxMap),
            assetName).NormalizedName;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            var definitions = new List<BaseAsset>();
            foreach (WorkspaceAssetCatalogEntry entry in Document.Rows.Where(entry =>
                         entry.TargetRowIdentity is not null &&
                         D3dbspAssetTypeFacts.IsMultiplayerType(entry.AssetType) &&
                         string.Equals(
                             entry.NormalizedName,
                             normalizedName,
                             StringComparison.Ordinal) &&
                         entry.Definition is not null))
            {
                definitions.Add(CurrentDefinitionForEntry(entry));
            }

            HashSet<XAssetType> capturedTypes = definitions
                .Select(definition => definition.SerializedAssetType)
                .ToHashSet();
            ClipMapAsset[] clipMaps = definitions
                .OfType<ClipMapAsset>()
                .Where(clipMap =>
                    clipMap.SerializedAssetType == XAssetType.ColMapMp)
                .ToArray();
            if (!capturedTypes.Contains(XAssetType.MapEnts) &&
                clipMaps.Length == 1 &&
                clipMaps[0].MapEnts is { } nestedMapEnts)
            {
                definitions.Add(nestedMapEnts);
                capturedTypes.Add(XAssetType.MapEnts);
            }
            definitions.AddRange(Workspace.AssetCatalog.DependencyEntries
                .Where(entry =>
                    D3dbspAssetTypeFacts.IsMultiplayerType(entry.AssetType) &&
                    !capturedTypes.Contains(entry.AssetType) &&
                    string.Equals(
                        entry.NormalizedName,
                        normalizedName,
                        StringComparison.Ordinal) &&
                    entry.Definition is not null)
                .Select(entry => entry.Definition!));

            return new D3dbspWorkspaceAssetGroup(
                _revision.Revision,
                assetName,
                definitions);
        }
    }

    /// <summary>
    /// Captures active full shader providers owned by the selected target that
    /// are not represented by serialized target rows.
    /// </summary>
    public IReadOnlyList<MaterialShaderAsset> CaptureCurrentTargetShaderProviders()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            MaterialShaderAsset[] shaders = Workspace.AssetCatalog.DependencyEntries
                .Where(entry =>
                    entry.Origin == WorkspaceAssetOrigin.DependencyOnly &&
                    entry.Access == WorkspaceAssetAccess.ReadOnly &&
                    entry.ContentSource == WorkspaceAssetContentSource.ResolvedProvider &&
                    entry.ProviderZone?.IsTarget == true &&
                    entry.AssetType is XAssetType.PixelShader or XAssetType.VertexShader &&
                    entry.Definition is MaterialShaderAsset)
                .Select(entry => (MaterialShaderAsset)entry.Definition!)
                .ToArray();
            return Array.AsReadOnly(shaders);
        }
    }

    /// <summary>
    /// Captures active full image providers owned by the selected target that
    /// are not represented by serialized target rows.
    /// </summary>
    public IReadOnlyList<GfxImageAsset> CaptureCurrentTargetImageProviders()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            GfxImageAsset[] images = Workspace.AssetCatalog.DependencyEntries
                .Where(entry =>
                    entry.Origin == WorkspaceAssetOrigin.DependencyOnly &&
                    entry.Access == WorkspaceAssetAccess.ReadOnly &&
                    entry.ContentSource == WorkspaceAssetContentSource.ResolvedProvider &&
                    entry.ProviderZone?.IsTarget == true &&
                    entry.AssetType == XAssetType.Image &&
                    entry.Definition is GfxImageAsset)
                .Select(entry => (GfxImageAsset)entry.Definition!)
                .ToArray();
            return Array.AsReadOnly(images);
        }
    }

    private static Dictionary<XAssetType, BaseAsset> RequireD3dbspDefinitions(
        D3dbspLinkResult linked)
    {
        BaseAsset[] definitions = linked.Roots
            .Concat(linked.NestedAssets)
            .Where(definition => D3dbspAssetTypeFacts.IsMultiplayerType(
                definition.SerializedAssetType))
            .ToArray();
        var byType = new Dictionary<XAssetType, BaseAsset>();
        foreach (XAssetType assetType in D3dbspAssetTypeFacts.MultiplayerTypes)
        {
            BaseAsset[] matches = definitions
                .Where(definition => definition.SerializedAssetType == assetType)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"A linked D3DBSP must contain exactly one {assetType} definition; found {matches.Length}.");
            }

            byType.Add(assetType, matches[0]);
        }

        string? assetName = byType[XAssetType.GfxMap].SerializedAssetName;
        if (!D3dbspAssetTypeFacts.IsOwnedD3dbspName(assetName))
        {
            throw new InvalidDataException(
                "A linked D3DBSP must use one owned .d3dbsp wire name.");
        }
        BaseAsset? mismatched = byType.Values.FirstOrDefault(definition =>
            !string.Equals(
                definition.SerializedAssetName,
                assetName,
                StringComparison.Ordinal));
        if (mismatched is not null)
        {
            throw new InvalidDataException(
                $"The linked {mismatched.SerializedAssetType} definition does not use the D3DBSP wire name '{assetName}'.");
        }

        return byType;
    }

    private BaseAsset CurrentDefinitionForEntry(
        WorkspaceAssetCatalogEntry entry)
    {
        TargetZoneRowIdentity identity = entry.TargetRowIdentity ??
            throw new InvalidDataException(
                "A current target definition requires a target-row identity.");
        return _drafts.TryGetValue(identity, out DraftState? draft)
            ? draft.CreateCurrentDefinition()
            : entry.Definition ?? throw new InvalidDataException(
                "A current target definition requires semantic content.");
    }

    private static HashSet<XAssetType> ValidateCapturedAssetTypes(
        IEnumerable<XAssetType> assetTypes)
    {
        ArgumentNullException.ThrowIfNull(assetTypes);
        var requested = new HashSet<XAssetType>(assetTypes);
        if (requested.Any(type => !Enum.IsDefined(type)))
            throw new ArgumentOutOfRangeException(nameof(assetTypes));

        return requested;
    }

    /// <summary>The current immutable request snapshot.</summary>
    public ZoneLinkRequest LinkRequest
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedCore();
                return _revision.LinkRequest;
            }
        }
    }

    internal FastFileSaveRevision CaptureRevision()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            Workspace.ThrowIfDisposed();
            return _revision;
        }
    }

    /// <summary>
    /// Publishes a candidate from a hosted editor. Provider publication occurs
    /// before this method commits the detached current draft and change state.
    /// </summary>
    internal bool PublishAppliedDefinition(
        TargetZoneRowIdentity identity,
        IW4.Assets.Assets.BaseAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            DraftState state = RequireDraft(identity);
            if (definition.SerializedAssetType != state.Entry.AssetType)
            {
                throw new InvalidDataException(
                    "An editor cannot publish a definition with a different asset type.");
            }

            object candidate = state.Adapter.CreateDraft(definition);
            if (state.SemanticallyEqualsCurrent(candidate))
                return false;
            PublishDefinitionCore(state, candidate);
        }

        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Publishes a synchronized hosted-definition set and every supplied
    /// linker provider in one revision before committing any draft state.
    /// </summary>
    internal bool PublishAppliedDefinitions(
        IEnumerable<(TargetZoneRowIdentity Identity,
            IW4.Assets.Assets.BaseAsset Definition,
            IReadOnlyList<IW4.Assets.Assets.BaseAsset> Providers)> publications,
        IEnumerable<AssetKey> withdrawnProviderKeys,
        bool raiseAppliedAssetsChanged = true,
        bool restoreTargetProvidersOnWithdrawal = false)
    {
        ArgumentNullException.ThrowIfNull(publications);
        ArgumentNullException.ThrowIfNull(withdrawnProviderKeys);
        (TargetZoneRowIdentity Identity,
            IW4.Assets.Assets.BaseAsset Definition,
            IReadOnlyList<IW4.Assets.Assets.BaseAsset> Providers)[] requested =
            publications.ToArray();
        if (requested.Length == 0)
            throw new ArgumentException("At least one definition is required.", nameof(publications));
        if (requested.Select(publication => publication.Identity).Distinct().Count() !=
            requested.Length)
        {
            throw new InvalidDataException(
                "A synchronized publication cannot contain the same target row twice.");
        }
        AssetKey[] requestedWithdrawnKeys = withdrawnProviderKeys
            .Distinct()
            .ToArray();

        lock (_gate)
        {
            ThrowIfDisposedCore();
            AssetKey[] withdrawnKeys = requestedWithdrawnKeys
                .Where(key => !IsD3dbspProviderRequired(key))
                .ToArray();
            AssetKey[] transferredD3dbspKeys = requestedWithdrawnKeys
                .Where(IsD3dbspProviderRequired)
                .ToArray();
            var pending = new List<(DraftState State, object Candidate)>();
            foreach ((TargetZoneRowIdentity identity,
                IW4.Assets.Assets.BaseAsset definition,
                IReadOnlyList<IW4.Assets.Assets.BaseAsset> providerDefinitions) in requested)
            {
                ArgumentNullException.ThrowIfNull(definition);
                ArgumentNullException.ThrowIfNull(providerDefinitions);
                if (providerDefinitions.Any(provider => provider is null))
                {
                    throw new ArgumentException(
                        "Provider definitions cannot contain null.",
                        nameof(publications));
                }

                DraftState state = RequireDraft(identity);
                if (definition.SerializedAssetType != state.Entry.AssetType)
                {
                    throw new InvalidDataException(
                        "An editor cannot publish a definition with a different asset type.");
                }

                object candidate = state.Adapter.CreateDraft(definition);
                AssetKey currentKey = AssetKey.FromDefinition(
                    state.CreateCurrentDefinition());
                AssetKey candidateKey = AssetKey.FromDefinition(
                    state.Adapter.CreateDefinition(candidate));
                if (candidateKey != currentKey)
                {
                    throw new InvalidDataException(
                        "An editor cannot change a hosted asset's stable identity.");
                }

                pending.Add((state, candidate));
            }

            if (withdrawnKeys.Length == 0 && pending.All(value =>
                    value.State.SemanticallyEqualsCurrent(value.Candidate)))
                return false;

            LinkAssetProviderSource[] providers = [
                .. requested.SelectMany(publication => new[]
                    { publication.Definition }.Concat(publication.Providers))
                    .Select(provider => new LinkAssetProviderSource(provider)
                        .AsAuthoredDetached())
                    .GroupBy(provider => AssetKey.FromDefinition(provider.Definition))
                    .Select(group => group.First())
            ];
            AssetKey[] replacedProviderKeys = providers
                .Select(provider => AssetKey.FromDefinition(provider.Definition))
                .Concat(withdrawnKeys)
                .Distinct()
                .ToArray();
            LinkAssetPool authoredAssets = _authoredAssets
                .WithoutProviders(replacedProviderKeys)
                .WithHighestPrecedenceProviders(providers);
            Publish(
                authoredAssets,
                _revision.LinkRequest.Roots,
                withdrawnTargetProviderKeys: restoreTargetProvidersOnWithdrawal
                    ? withdrawnKeys.Where(key => !_targetBaseAssets.Providers.Any(
                        provider => provider.Key == key))
                    : withdrawnKeys,
                publishedProviderKeys: providers
                    .Select(provider => AssetKey.FromDefinition(provider.Definition)));
            foreach ((DraftState state, object candidate) in pending)
                state.SetCurrent(candidate, _revision.Revision);
            _d3dbspAuthoredProviderKeys.UnionWith(transferredD3dbspKeys);
            RebuildChangeSet();
        }

        if (raiseAppliedAssetsChanged)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal bool PublishCompiledXModel(
        TargetZoneRowIdentity identity,
        IW4.Assets.Assets.XModel.XModelAsset definition,
        IReadOnlyList<IW4.Assets.Assets.BaseAsset> providers) =>
        PublishCompiledDefinition(identity, definition, providers);

    internal bool TryCaptureEditableSoundPayload(
        TargetZoneRowIdentity identity,
        int aliasIndex,
        int fileIndex,
        out LoadedSound? payload,
        out string reason)
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (!TryResolveEditableSoundPayloadCore(
                    identity,
                    aliasIndex,
                    fileIndex,
                    out _,
                    out LoadedSound? current,
                    out _,
                    out _,
                    out reason))
            {
                payload = null;
                return false;
            }

            payload = SoundDraft.Copy(current!);
            return true;
        }
    }

    internal bool ApplyCompiledSound(
        TargetZoneRowIdentity identity,
        int aliasIndex,
        int fileIndex,
        LoadedSound replacement,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        bool changed;
        AssetValidationIssue[] validation;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            string fieldPath =
                $"sound.aliases[{aliasIndex}].soundFiles[{fileIndex}]";
            if (!TryResolveEditableSoundPayloadCore(
                    identity,
                    aliasIndex,
                    fileIndex,
                    out SoundAliasListAsset? current,
                    out _,
                    out AssetKey loadedSoundKey,
                    out bool referencedByAnotherTargetSound,
                    out string reason))
            {
                validation = [new AssetValidationIssue(
                    fieldPath,
                    reason,
                    AssetValidationSeverity.Error)];
                issues = Array.AsReadOnly(validation);
                return false;
            }

            var found = new List<AssetValidationIssue>();
            AssetKey replacementKey = default;
            try
            {
                replacementKey = AssetKey.FromDefinition(replacement);
                if (replacement.Name?.StartsWith(",", StringComparison.Ordinal) == true)
                {
                    found.Add(new AssetValidationIssue(
                        $"{fieldPath}.loadedSound.name",
                        "An imported payload must remain a full LoadedSound definition, not a reference placeholder.",
                        AssetValidationSeverity.Error));
                }
                if (replacementKey != loadedSoundKey)
                {
                    found.Add(new AssetValidationIssue(
                        $"{fieldPath}.loadedSound.name",
                        "An imported payload cannot change the referenced LoadedSound identity.",
                        AssetValidationSeverity.Error));
                }
            }
            catch (ArgumentException exception)
            {
                found.Add(new AssetValidationIssue(
                    $"{fieldPath}.loadedSound.name",
                    exception.Message,
                    AssetValidationSeverity.Error));
            }

            if (replacement.PhysicalData is null)
            {
                found.Add(new AssetValidationIssue(
                    $"{fieldPath}.loadedSound.physicalData",
                    "An imported LoadedSound requires materialized MPEG payload bytes.",
                    AssetValidationSeverity.Error));
            }
            else if (replacement.PhysicalDataByteCount !=
                     replacement.PhysicalData.Length)
            {
                found.Add(new AssetValidationIssue(
                    $"{fieldPath}.loadedSound.physicalDataByteCount",
                    $"The LoadedSound declares {replacement.PhysicalDataByteCount} byte(s), but the imported payload contains {replacement.PhysicalData.Length}.",
                    AssetValidationSeverity.Error));
            }

            int expectedSeekTableByteCount = checked(
                replacement.SeekTableCount * sizeof(uint));
            if (replacement.SeekTable is null)
            {
                if (replacement.SeekTableCount != 0)
                {
                    found.Add(new AssetValidationIssue(
                        $"{fieldPath}.loadedSound.seekTable",
                        "The LoadedSound declares seek entries without materialized seek-table bytes.",
                        AssetValidationSeverity.Error));
                }
            }
            else if (replacement.SeekTable.Length != expectedSeekTableByteCount)
            {
                found.Add(new AssetValidationIssue(
                    $"{fieldPath}.loadedSound.seekTable",
                    $"The LoadedSound declares {replacement.SeekTableCount} seek entries, but the imported seek table contains {replacement.SeekTable.Length} byte(s).",
                    AssetValidationSeverity.Error));
            }

            validation = found.ToArray();
            issues = Array.AsReadOnly(validation);
            if (validation.Any(issue =>
                    issue.Severity == AssetValidationSeverity.Error))
            {
                return false;
            }

            var candidate = new SoundDraft(current!);
            candidate.ReplaceLoadedSound(loadedSoundKey, replacement);
            if (candidate.SemanticallyEquals(new SoundDraft(current!)))
                return false;

            LoadedSound detachedReplacement =
                referencedByAnotherTargetSound
                    ? SoundDraft.CopyWithName(
                        replacement,
                        CreateCopyOnWriteLoadedSoundName(
                            identity,
                            aliasIndex,
                            fileIndex))
                    : SoundDraft.Copy(replacement);
            if (referencedByAnotherTargetSound)
            {
                candidate = new SoundDraft(current!);
                candidate.ReplaceLoadedSound(
                    loadedSoundKey,
                    detachedReplacement);
            }
            changed = PublishCompiledDefinitionCore(
                identity,
                candidate.ToAsset(),
                [detachedReplacement],
                allowedTargetProviderCollision: loadedSoundKey);
        }

        if (changed)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    internal bool PublishCompiledDefinition(
        TargetZoneRowIdentity identity,
        IW4.Assets.Assets.BaseAsset definition,
        IReadOnlyList<IW4.Assets.Assets.BaseAsset> providers)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(providers);
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            changed = PublishCompiledDefinitionCore(
                identity,
                definition,
                providers,
                allowedTargetProviderCollision: null);
        }
        if (changed)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private bool PublishCompiledDefinitionCore(
        TargetZoneRowIdentity identity,
        IW4.Assets.Assets.BaseAsset definition,
        IReadOnlyList<IW4.Assets.Assets.BaseAsset> providers,
        AssetKey? allowedTargetProviderCollision)
    {
        DraftState state = RequireDraft(identity);
        XAssetType assetType = state.Entry.AssetType;
        bool supportedSoundPublication = assetType == XAssetType.Sound &&
            allowedTargetProviderCollision is not null;
        if ((!supportedSoundPublication &&
             assetType is not (XAssetType.XModel or XAssetType.Font or XAssetType.Weapon)) ||
            definition.SerializedAssetType != assetType)
        {
            throw new InvalidOperationException(
                "Only a matching XModel, Font, Weapon, or validated Sound row can publish compiled dependency providers.");
        }
        object candidate = state.Adapter.CreateDraft(definition);
        if (state.SemanticallyEqualsCurrent(candidate))
            return false;
        Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> prior =
            _compiledAuxiliaryProviders.TryGetValue(identity, out Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>? existing)
                ? existing : [];
        IW4.Assets.Assets.BaseAsset[] suppliedProviders = providers
                .GroupBy(AssetKey.FromDefinition)
                .Select(group => group.FirstOrDefault(provider =>
                        !IsReferenceProvider(provider)) ?? group.First())
                .Where(provider =>
                {
                    AssetKey key = AssetKey.FromDefinition(provider);
                    if (prior.ContainsKey(key))
                        return true;
                    if (IsCompiledProviderOwnedByOther(identity, key))
                        return true;
                    LinkAssetProvider[] liveProviders = _revision.LinkRequest.Assets
                        .Providers.Where(live => live.Key == key).ToArray();
                    if (IsReferenceProvider(provider))
                        return liveProviders.Length == 0;
                    return provider.SerializedAssetType != XAssetType.Techset ||
                        liveProviders.All(live => live.IsReferencePlaceholder);
                })
                .ToArray();
        AssetKey[] suppliedKeys = suppliedProviders
            .Select(AssetKey.FromDefinition)
            .ToArray();
        var available = prior.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (IW4.Assets.Assets.BaseAsset provider in suppliedProviders)
            available[AssetKey.FromDefinition(provider)] = provider;
        AssetKey[] currentKeys = ReferencedAuxiliaryKeys(
            definition,
            available);
        if (suppliedKeys.Any(key => !currentKeys.Contains(key)))
        {
            throw new InvalidDataException(
                "A compiled dependency closure contains a provider that is not reachable from its root definition.");
        }
        AssetKey[] nextKeys = currentKeys
            .Concat(ReferencedAuxiliaryKeys(
                RequireDraft(identity).CreateSavedDefinition(),
                available))
            .Distinct().ToArray();
        foreach (IW4.Assets.Assets.BaseAsset provider in suppliedProviders)
        {
            AssetKey key = AssetKey.FromDefinition(provider);
            bool ownedCurrent = prior.ContainsKey(key);
            bool ownedElsewhere = IsCompiledProviderOwnedByOther(identity, key);
            bool liveFullCollision = _revision.LinkRequest.Assets.Providers.Any(live =>
                live.Key == key && !live.IsReferencePlaceholder);
            if (liveFullCollision &&
                !ownedCurrent &&
                !ownedElsewhere &&
                key != allowedTargetProviderCollision)
            {
                throw new InvalidDataException(
                    $"Generated provider key '{key}' collides with an unrelated live provider.");
            }
        }
        AssetKey[] withdrawn = prior.Keys
            .Where(key => !nextKeys.Contains(key) &&
                !IsCompiledProviderOwnedByOther(identity, key))
            .ToArray();
        bool changed = PublishAppliedDefinitions(
            [(identity, definition, suppliedProviders)],
            withdrawn,
            raiseAppliedAssetsChanged: false,
            restoreTargetProvidersOnWithdrawal: true);
        if (changed)
        {
            Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> next = prior
                .Where(pair => nextKeys.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (IW4.Assets.Assets.BaseAsset provider in suppliedProviders)
                next[AssetKey.FromDefinition(provider)] = provider;
            _compiledAuxiliaryProviders[identity] = next;
        }
        return changed;
    }

    private static bool IsReferenceProvider(
        IW4.Assets.Assets.BaseAsset provider) =>
        provider.SerializedAssetName?.StartsWith(',') == true;

    private bool IsCompiledProviderOwnedByOther(
        TargetZoneRowIdentity identity,
        AssetKey key) =>
        _compiledAuxiliaryProviders.Any(pair =>
            pair.Key != identity && pair.Value.ContainsKey(key));

    private bool IsCompiledProviderOwned(AssetKey key) =>
        _compiledAuxiliaryProviders.Values.Any(owned => owned.ContainsKey(key));

    private bool IsD3dbspProviderRequired(AssetKey key) =>
        _d3dbspProviderKeys.Values.Any(required => required.Contains(key));

    private bool TryResolveEditableSoundPayloadCore(
        TargetZoneRowIdentity identity,
        int aliasIndex,
        int fileIndex,
        out SoundAliasListAsset? sound,
        out LoadedSound? loadedSound,
        out AssetKey loadedSoundKey,
        out bool referencedByAnotherTargetSound,
        out string reason)
    {
        sound = null;
        loadedSound = null;
        loadedSoundKey = default;
        referencedByAnotherTargetSound = false;
        DraftState state;
        try
        {
            state = RequireDraft(identity);
        }
        catch (KeyNotFoundException)
        {
            reason = "Only Sound assets owned by the current fastfile/zone can be modified.";
            return false;
        }

        if (state.Entry.AssetType != XAssetType.Sound ||
            state.Entry.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition ||
            state.Entry.Access != WorkspaceAssetAccess.Editable ||
            state.Entry.TargetRowIdentity != identity)
        {
            reason = "Only Sound assets owned by the current fastfile/zone can be modified.";
            return false;
        }
        if (state.CreateCurrentDefinition() is not SoundAliasListAsset current)
        {
            reason = "The hosted target row does not contain a materialized Sound definition.";
            return false;
        }
        sound = current;
        if ((uint)aliasIndex >= (uint)current.Aliases.Count)
        {
            reason = "The selected Sound alias is no longer present.";
            return false;
        }
        SndAlias alias = current.Aliases[aliasIndex];
        if ((uint)fileIndex >= (uint)alias.SoundFiles.Count)
        {
            reason = "The selected SoundFile is no longer present.";
            return false;
        }
        SoundFile file = alias.SoundFiles[fileIndex];
        if (file.Exists == 0)
        {
            reason = "The selected SoundFile is marked as not present.";
            return false;
        }
        if (file.Type != SndAliasType.Loaded)
        {
            reason = "Streamed Sound payloads from packfileN.pak are read-only.";
            return false;
        }
        if (file.Payload is not LoadedSoundFile { LoadedSound: { } selected })
        {
            reason = "The selected SoundFile has no materialized LoadedSound definition.";
            return false;
        }
        if (selected.PhysicalData is null ||
            selected.PhysicalData.Length != selected.PhysicalDataByteCount ||
            (selected.SeekTableCount != 0 && selected.SeekTable is null) ||
            (selected.SeekTable is not null &&
             selected.SeekTable.Length != selected.SeekTableByteCount))
        {
            reason = "The selected LoadedSound payload is not fully materialized.";
            return false;
        }

        try
        {
            loadedSoundKey = AssetKey.FromDefinition(selected);
        }
        catch (ArgumentException)
        {
            reason = "The selected LoadedSound has no usable asset identity.";
            return false;
        }
        AssetKey selectedLoadedSoundKey = loadedSoundKey;
        bool targetOwned = _targetBaseAssets.Providers.Any(provider =>
                provider.Key == selectedLoadedSoundKey &&
                provider.SerializedType == XAssetType.LoadedSound &&
                !provider.IsReferencePlaceholder);
        bool authoredForCurrentSound =
            _compiledAuxiliaryProviders.TryGetValue(
                identity,
                out Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>? owned) &&
            owned.TryGetValue(
                selectedLoadedSoundKey,
                out IW4.Assets.Assets.BaseAsset? ownedProvider) &&
            ownedProvider is LoadedSound &&
            !IsReferenceProvider(ownedProvider);
        if (!targetOwned && !authoredForCurrentSound)
        {
            reason = "The selected LoadedSound is not owned by the current fastfile/zone.";
            return false;
        }

        var draftedSoundKeys = new HashSet<AssetKey>();
        foreach (WorkspaceAssetCatalogEntry entry in Document.Rows
            .Where(entry =>
                entry.AssetType == XAssetType.Sound &&
                entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
                entry.Access == WorkspaceAssetAccess.Editable &&
                entry.TargetRowIdentity is not null))
        {
            if (CurrentDefinitionForEntry(entry) is not SoundAliasListAsset other)
                continue;
            draftedSoundKeys.Add(AssetKey.FromDefinition(other));
            if (entry.TargetRowIdentity == identity)
                continue;
            if (!SoundDraft.LoadedSounds(other).Any(candidate =>
                    AssetKey.FromDefinition(candidate) == selectedLoadedSoundKey))
            {
                continue;
            }

            referencedByAnotherTargetSound = true;
            break;
        }

        if (!referencedByAnotherTargetSound && !Workspace.IsBlank)
        {
            IW4.Runtime.Database.DbZoneHandle targetOwner =
                Workspace.LoadedZone.Context.ZoneOwner;
            foreach (IW4.Runtime.Assets.XAssetProviderContribution provider in
                     Workspace.LoadedZone.Context.AssetPool.Slots
                         .Where(slot => slot.AssetType == XAssetType.Sound)
                         .SelectMany(slot => slot.Providers)
                         .Where(provider =>
                             provider.Owner == targetOwner &&
                             !provider.IsReferencePlaceholder &&
                             provider.Asset is SoundAliasListAsset))
            {
                var other = (SoundAliasListAsset)provider.Asset;
                AssetKey otherKey = AssetKey.FromDefinition(other);
                if (draftedSoundKeys.Contains(otherKey) ||
                    !SoundDraft.LoadedSounds(other).Any(candidate =>
                        AssetKey.FromDefinition(candidate) ==
                        selectedLoadedSoundKey))
                {
                    continue;
                }

                referencedByAnotherTargetSound = true;
                break;
            }
        }

        loadedSound = selected;
        reason = string.Empty;
        return true;
    }

    private string CreateCopyOnWriteLoadedSoundName(
        TargetZoneRowIdentity identity,
        int aliasIndex,
        int fileIndex)
    {
        string stem = $"iw4studio/sound_{identity.SerializedIndex}_" +
            $"{aliasIndex}_{fileIndex}";
        for (int suffix = 0; ; suffix = checked(suffix + 1))
        {
            string candidateName = suffix == 0
                ? stem
                : $"{stem}_{suffix}";
            if (ValidateNewAssetName(
                    XAssetType.LoadedSound,
                    candidateName) is null)
                return candidateName;
        }
    }

    internal object CloneCurrentDraft(
        TargetZoneRowIdentity identity,
        IAssetAuthoringAdapter adapter)
    {
        lock (_gate)
        {
            DraftState state = RequireDraft(identity, adapter);
            return state.CloneCurrent();
        }
    }

    internal IReadOnlyList<IW4.Assets.Assets.BaseAsset> CaptureAppliedXModelProviders(
        TargetZoneRowIdentity identity) =>
        CaptureAppliedCompiledProviders(identity, XAssetType.XModel);

    internal IReadOnlyList<IW4.Assets.Assets.BaseAsset> CaptureAppliedWeaponProviders(
        TargetZoneRowIdentity identity) =>
        CaptureAppliedCompiledProviders(identity, XAssetType.Weapon);

    private IReadOnlyList<IW4.Assets.Assets.BaseAsset> CaptureAppliedCompiledProviders(
        TargetZoneRowIdentity identity,
        XAssetType expectedType)
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (RequireDraft(identity).Entry.AssetType != expectedType)
                return [];
            if (!_compiledAuxiliaryProviders.TryGetValue(
                    identity,
                    out Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>? owned))
            {
                return [];
            }
            IW4.Assets.Assets.BaseAsset current =
                RequireDraft(identity).CreateCurrentDefinition();
            return Array.AsReadOnly(ReferencedAuxiliaryKeys(current, owned)
                .Select(key => owned[key])
                .ToArray());
        }
    }

    internal bool IsDraftChanged(
        TargetZoneRowIdentity identity,
        IAssetAuthoringAdapter adapter)
    {
        lock (_gate)
            return RequireDraft(identity, adapter).IsChanged;
    }

    internal bool ApplyDraft(
        TargetZoneRowIdentity identity,
        IAssetAuthoringAdapter adapter,
        object candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            DraftState state = RequireDraft(identity, adapter);
            if (state.SemanticallyEqualsCurrent(candidate))
                return false;
            if (_compiledAuxiliaryProviders.TryGetValue(
                    identity,
                    out Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>? owned))
            {
                PublishDefinitionWithAuxiliaryReconciliationCore(
                    identity,
                    state,
                    candidate,
                    owned);
            }
            else
            {
                PublishDefinitionCore(state, candidate);
            }
        }

        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void PublishDefinitionWithAuxiliaryReconciliationCore(
        TargetZoneRowIdentity identity,
        DraftState state,
        object candidate,
        IReadOnlyDictionary<AssetKey, IW4.Assets.Assets.BaseAsset> owned)
    {
        object detachedCandidate = state.Adapter.CloneDraft(candidate);
        IW4.Assets.Assets.BaseAsset definition = state.Adapter.CreateDefinition(
            detachedCandidate);
        AssetKey[] retainedKeys = ReferencedAuxiliaryKeys(definition, owned)
            .Concat(ReferencedAuxiliaryKeys(state.CreateSavedDefinition(), owned))
            .Distinct()
            .ToArray();
        IW4.Assets.Assets.BaseAsset[] retainedProviders = retainedKeys
            .Select(key => owned[key])
            .ToArray();
        AssetKey[] withdrawnKeys = owned.Keys
            .Where(key => !retainedKeys.Contains(key) &&
                !IsCompiledProviderOwnedByOther(identity, key))
            .ToArray();
        _ = PublishAppliedDefinitions(
            [(identity, definition, retainedProviders)],
            withdrawnKeys,
            raiseAppliedAssetsChanged: false,
            restoreTargetProvidersOnWithdrawal: true);
        _compiledAuxiliaryProviders[identity] = retainedKeys
            .ToDictionary(key => key, key => owned[key]);
    }

    internal bool RevertDraft(
        TargetZoneRowIdentity identity,
        IAssetAuthoringAdapter adapter)
    {
        lock (_gate)
        {
            DraftState state = RequireDraft(identity, adapter);
            if (!state.IsChanged)
                return false;
            if (_compiledAuxiliaryProviders.ContainsKey(identity))
                RevertCompiledDefinitionCore(identity, state);
            else
                PublishDefinitionCore(state, state.CloneSaved());
        }

        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RevertCompiledDefinitionCore(TargetZoneRowIdentity identity, DraftState state)
    {
        if (state.Entry.AssetType == XAssetType.Sound)
        {
            RevertCompiledSoundDefinitionCore(identity, state);
            return;
        }

        IW4.Assets.Assets.BaseAsset saved = state.CreateSavedDefinition();
        Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> owned =
            _compiledAuxiliaryProviders.TryGetValue(identity, out Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>? existing)
                ? existing : [];
        AssetKey[] savedKeys = ReferencedAuxiliaryKeys(saved, owned).ToArray();
        IW4.Assets.Assets.BaseAsset[] providers = savedKeys
            .Select(key => (IW4.Assets.Assets.BaseAsset)owned[key]).ToArray();
        AssetKey[] withdrawn = owned.Keys
            .Where(key => !savedKeys.Contains(key) &&
                !IsCompiledProviderOwnedByOther(identity, key))
            .ToArray();
        _ = PublishAppliedDefinitions(
            [(identity, saved, providers)],
            withdrawn,
            raiseAppliedAssetsChanged: false,
            restoreTargetProvidersOnWithdrawal: true);
        _compiledAuxiliaryProviders[identity] = savedKeys
            .ToDictionary(key => key, key => owned[key]);
    }

    private void RevertCompiledSoundDefinitionCore(
        TargetZoneRowIdentity identity,
        DraftState state)
    {
        SoundAliasListAsset saved = state.CreateSavedDefinition()
            as SoundAliasListAsset ?? throw new InvalidDataException(
                "A Sound editor's saved draft is not a Sound definition.");
        Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> owned =
            _compiledAuxiliaryProviders.TryGetValue(
                identity,
                out Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>? existing)
                ? existing
                : [];
        Dictionary<AssetKey, LoadedSound> savedLoadedSounds = SoundDraft
            .LoadedSounds(saved)
            .GroupBy(AssetKey.FromDefinition)
            .ToDictionary(
                group => group.Key,
                group => SoundDraft.Copy(group.First()));
        Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> restored = owned.Keys
            .Where(savedLoadedSounds.ContainsKey)
            .ToDictionary(
                key => key,
                key => (IW4.Assets.Assets.BaseAsset)savedLoadedSounds[key]);
        AssetKey[] withdrawn = owned.Keys
            .Where(key => !restored.ContainsKey(key) &&
                !IsCompiledProviderOwnedByOther(identity, key))
            .ToArray();
        _ = PublishAppliedDefinitions(
            [(identity, saved, restored.Values.ToArray())],
            withdrawn,
            raiseAppliedAssetsChanged: false,
            restoreTargetProvidersOnWithdrawal: true);
        _compiledAuxiliaryProviders[identity] = restored;
    }

    private static AssetKey[] ReferencedAuxiliaryKeys(
        IW4.Assets.Assets.BaseAsset? definition,
        IReadOnlyDictionary<AssetKey, IW4.Assets.Assets.BaseAsset> ownedProviders)
    {
        if (definition is null)
            return [];
        var referenced = new HashSet<AssetKey>();
        var pending = new Queue<IW4.Assets.Assets.BaseAsset>();
        EnqueueDependencies(definition);
        while (pending.TryDequeue(out IW4.Assets.Assets.BaseAsset? dependency))
        {
            AssetKey key = AssetKey.FromDefinition(dependency);
            if (!ownedProviders.TryGetValue(
                    key,
                    out IW4.Assets.Assets.BaseAsset? ownedProvider) ||
                !referenced.Add(key))
            {
                continue;
            }
            EnqueueDependencies(ownedProvider);
        }
        return referenced.ToArray();

        void EnqueueDependencies(IW4.Assets.Assets.BaseAsset provider)
        {
            switch (provider)
            {
                case SoundAliasListAsset sound:
                    foreach (LoadedSound loadedSound in
                             SoundDraft.LoadedSounds(sound))
                    {
                        pending.Enqueue(loadedSound);
                    }
                    break;
                case IW4.Assets.Assets.Weapon.WeaponAsset weapon
                    when weapon.Definition is { } weaponDefinition:
                    foreach (IW4.Assets.Assets.XModel.XModelAsset model in
                             weaponDefinition.GunModels
                                 .Concat(weaponDefinition.WorldGunModels)
                                 .Concat([
                                     weaponDefinition.HandModel,
                                     weaponDefinition.WorldClipModel,
                                     weaponDefinition.RocketModel,
                                     weaponDefinition.KnifeModel,
                                     weaponDefinition.WorldKnifeModel,
                                     weaponDefinition.Projectile.Model
                                 ])
                                 .OfType<IW4.Assets.Assets.XModel.XModelAsset>())
                    {
                        pending.Enqueue(model);
                    }
                    break;
                case IW4.Assets.Assets.XModel.XModelAsset model:
                    foreach (IW4.Assets.Assets.XModel.XModelSurfsAsset modelSurfs in
                             model.Lods.Select(lod => lod.ModelSurfs)
                                 .OfType<IW4.Assets.Assets.XModel.XModelSurfsAsset>())
                    {
                        pending.Enqueue(modelSurfs);
                    }
                    foreach (IW4.Assets.Assets.Material.MaterialAsset material in
                             model.Materials.OfType<IW4.Assets.Assets.Material.MaterialAsset>())
                    {
                        pending.Enqueue(material);
                    }
                    if (model.PhysPreset is not null)
                        pending.Enqueue(model.PhysPreset);
                    if (model.PhysCollmap is not null)
                        pending.Enqueue(model.PhysCollmap);
                    break;
                case IW4.Assets.Assets.Font.FontAsset font:
                    if (font.Material is not null)
                        pending.Enqueue(font.Material);
                    if (font.GlowMaterial is not null)
                        pending.Enqueue(font.GlowMaterial);
                    break;
                case IW4.Assets.Assets.Material.MaterialAsset material:
                    if (material.TechniqueSet is not null)
                        pending.Enqueue(material.TechniqueSet);
                    foreach (IW4.Assets.Assets.Image.GfxImageAsset image in
                             material.Textures.Select(texture => texture.Image)
                                 .OfType<IW4.Assets.Assets.Image.GfxImageAsset>())
                    {
                        pending.Enqueue(image);
                    }
                    break;
                case IW4.Assets.Assets.TechniqueSet.MaterialTechniqueSetAsset techniqueSet:
                    foreach (IW4.Assets.Assets.TechniqueSet.MaterialShaderAsset shader in
                             techniqueSet.TechniqueSlots
                                 .Where(slot => slot.Technique is not null)
                                 .SelectMany(slot => slot.Technique!.Passes)
                                 .SelectMany(pass => new[]
                                     { pass.VertexShader, pass.PixelShader })
                                 .OfType<IW4.Assets.Assets.TechniqueSet.MaterialShaderAsset>())
                    {
                        pending.Enqueue(shader);
                    }
                    break;
            }
        }
    }

    internal IW4.Assets.Assets.BaseAsset CaptureSavedDefinition(
        TargetZoneRowIdentity identity)
    {
        lock (_gate)
            return RequireDraft(identity).CreateSavedDefinition();
    }

    internal IW4.Assets.Assets.BaseAsset CaptureCurrentDefinition(
        TargetZoneRowIdentity identity)
    {
        lock (_gate)
            return RequireDraft(identity).CreateCurrentDefinition();
    }

    internal bool CommitSaveIfCurrentRevision(long revision, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            Workspace.ThrowIfDisposed();
            if (_revision.Revision != revision)
                return false;

            commit();
            foreach (DraftState state in _drafts.Values)
                state.AcknowledgeSaved();
            WithdrawSavedAuxiliaries();
            _addedRows.Clear();
            RebuildChangeSet();
            return true;
        }
    }

    private void WithdrawSavedAuxiliaries()
    {
        AssetKey[] previouslyOwned = _compiledAuxiliaryProviders.Values
            .SelectMany(owned => owned.Keys)
            .Distinct()
            .ToArray();
        var retainedByIdentity = new Dictionary<TargetZoneRowIdentity,
            Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset>>();
        foreach ((TargetZoneRowIdentity identity, Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> owned) in _compiledAuxiliaryProviders)
        {
            IW4.Assets.Assets.BaseAsset current = RequireDraft(identity).CreateCurrentDefinition();
            AssetKey[] retained = ReferencedAuxiliaryKeys(current, owned);
            retainedByIdentity[identity] = retained.ToDictionary(
                key => key,
                key => owned[key]);
        }
        foreach ((TargetZoneRowIdentity identity,
            Dictionary<AssetKey, IW4.Assets.Assets.BaseAsset> retained) in retainedByIdentity)
        {
            _compiledAuxiliaryProviders[identity] = retained;
        }
        AssetKey[] releasedKeys = previouslyOwned.Except(
                _compiledAuxiliaryProviders.Values.SelectMany(owned => owned.Keys))
            .ToArray();
        _d3dbspAuthoredProviderKeys.UnionWith(
            releasedKeys.Where(IsD3dbspProviderRequired));
        AssetKey[] keys = releasedKeys
            .Where(key => !IsD3dbspProviderRequired(key))
            .ToArray();
        if (keys.Length == 0)
            return;
        LinkAssetPool authored = _authoredAssets.WithoutProviders(keys);
        Publish(
            authored,
            _revision.LinkRequest.Roots,
            withdrawnTargetProviderKeys: keys.Where(key =>
                !_targetBaseAssets.Providers.Any(provider => provider.Key == key)));
    }

    internal void ThrowIfDisposed()
    {
        lock (_gate)
            ThrowIfDisposedCore();
    }

    public void Dispose()
    {
        bool disposeWorkspace;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            disposeWorkspace = true;
        }

        try
        {
            _cancellation.Cancel();
        }
        finally
        {
            try
            {
                if (disposeWorkspace)
                    Workspace.DisposeEditingSession(this);
            }
            finally
            {
                _cancellation.Dispose();
            }
        }
    }

    private void Publish(
        LinkAssetPool authoredAssets,
        IEnumerable<LinkRoot> roots,
        IEnumerable<AssetKey>? withdrawnTargetProviderKeys = null,
        IEnumerable<AssetKey>? publishedProviderKeys = null)
    {
        ArgumentNullException.ThrowIfNull(authoredAssets);
        var maskedTargetBaseProviderKeys = new HashSet<AssetKey>(
            _maskedTargetBaseProviderKeys);
        if (withdrawnTargetProviderKeys is not null)
            maskedTargetBaseProviderKeys.UnionWith(withdrawnTargetProviderKeys);
        if (publishedProviderKeys is not null)
            maskedTargetBaseProviderKeys.ExceptWith(publishedProviderKeys);

        ZoneLinkRequest previous = _revision.LinkRequest;
        LinkAssetPool targetBaseAssets = _targetBaseAssets.WithoutProviders(
            maskedTargetBaseProviderKeys);
        LinkAssetPool effectiveAuthoredAssets = authoredAssets.WithoutProviders(
            maskedTargetBaseProviderKeys);
        LinkAssetPool assets = targetBaseAssets.WithHighestPrecedencePool(
            effectiveAuthoredAssets);
        var request = new ZoneLinkRequest(
            assets,
            roots,
            previous.LanguageMask,
            previous.SelectedLanguageMask,
            previous.ScriptStrings);
        var revision = new FastFileSaveRevision(
            checked(_revision.Revision + 1),
            _revision.SourcePath,
            request);
        _authoredAssets = effectiveAuthoredAssets;
        _maskedTargetBaseProviderKeys = maskedTargetBaseProviderKeys;
        _revision = revision;
    }

    private void PublishDefinitionCore(DraftState state, object candidate)
    {
        object detachedCandidate = state.Adapter.CloneDraft(candidate);
        IW4.Assets.Assets.BaseAsset definition = state.Adapter.CreateDefinition(
            detachedCandidate);
        AssetKey currentKey = AssetKey.FromDefinition(state.CreateCurrentDefinition());
        AssetKey candidateKey = AssetKey.FromDefinition(definition);
        if (candidateKey != currentKey)
        {
            throw new InvalidDataException(
                "An editor cannot change a hosted asset's stable identity.");
        }

        LinkAssetPool authoredAssets = _authoredAssets
            .WithoutProviders([candidateKey])
            .WithHighestPrecedenceProviders(
                [new LinkAssetProviderSource(definition).AsAuthoredDetached()]);
        Publish(
            authoredAssets,
            _revision.LinkRequest.Roots,
            publishedProviderKeys: [candidateKey]);
        state.SetCurrent(detachedCandidate, _revision.Revision);
        RebuildChangeSet();
    }

    private void AddInitialDraft(
        WorkspaceAssetCatalogEntry entry,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (entry.TargetRowIdentity is not { } identity ||
            entry.Access != WorkspaceAssetAccess.Editable ||
            entry.Definition is null ||
            !adapters.TryGetAdapter(
                entry.AssetType,
                out IAssetAuthoringAdapter? adapter))
        {
            return;
        }

        _drafts.Add(identity, new DraftState(entry, adapter!));
    }

    private IAssetAuthoringAdapter RequireHostedAdapter(
        IW4.Assets.Assets.BaseAsset definition)
    {
        AssetAuthoringAdapterRegistry registry =
            AssetAuthoringAdapterRegistry.CreateDefault();
        return registry.RequireAdapter(definition.SerializedAssetType);
    }

    private DraftState RequireDraft(TargetZoneRowIdentity identity) =>
        _drafts.TryGetValue(identity, out DraftState? state)
            ? state
            : throw new KeyNotFoundException(
                $"Target row {identity.SerializedIndex} has no hosted editor state.");

    private DraftState RequireDraft(
        TargetZoneRowIdentity identity,
        IAssetAuthoringAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        DraftState state = RequireDraft(identity);
        if (state.Adapter.AssetType != adapter.AssetType ||
            state.Adapter.DraftType != adapter.DraftType)
        {
            throw new InvalidOperationException(
                "The editor adapter does not own this target row.");
        }

        return state;
    }

    private void RebuildChangeSet()
    {
        IEnumerable<AssetRowChange> modified = _drafts.Values
            .Where(state => state.IsChanged && !_addedRows.ContainsKey(state.Identity))
            .Select(state => state.CreateChange());
        IEnumerable<AssetRowChange> added = _addedRows.Select(value =>
        {
            DraftState state = RequireDraft(value.Key);
            return state.CreateAddedChange(value.Value);
        });
        _changeSet = new AssetChangeSet(modified.Concat(added));
    }

    private sealed class DraftState
    {
        private object _saved;
        private object _current;
        private long? _firstChangedRevision;
        private long _lastChangedRevision;

        public DraftState(WorkspaceAssetCatalogEntry entry, IAssetAuthoringAdapter adapter)
        {
            Entry = entry;
            Adapter = adapter;
            Identity = entry.TargetRowIdentity ?? throw new InvalidDataException(
                "A hosted editor state requires a target row.");
            _saved = adapter.CreateDraft(entry.Definition ?? throw new InvalidDataException(
                "A hosted editor state requires a detached definition."));
            // Stored drafts are immutable snapshots: every outward edit and
            // definition capture clones before use, while SetCurrent replaces
            // rather than mutates. Share the identical initial state until the
            // first authored replacement is published.
            _current = _saved;
        }

        public WorkspaceAssetCatalogEntry Entry { get; }
        public IAssetAuthoringAdapter Adapter { get; }
        public TargetZoneRowIdentity Identity { get; }
        public bool IsChanged => !ReferenceEquals(_saved, _current) &&
            !Adapter.SemanticallyEquals(_saved, _current);

        public object CloneCurrent() => Adapter.CloneDraft(_current);
        public object CloneSaved() => Adapter.CloneDraft(_saved);
        public bool SemanticallyEqualsCurrent(object value) =>
            Adapter.SemanticallyEquals(_current, value);
        public IW4.Assets.Assets.BaseAsset CreateCurrentDefinition() =>
            Adapter.CreateDefinition(Adapter.CloneDraft(_current));
        public IW4.Assets.Assets.BaseAsset CreateSavedDefinition() =>
            Adapter.CreateDefinition(Adapter.CloneDraft(_saved));

        public void SetCurrent(object value, long revision)
        {
            _current = Adapter.CloneDraft(value);
            if (IsChanged)
            {
                _firstChangedRevision ??= revision;
                _lastChangedRevision = revision;
            }
            else
            {
                _firstChangedRevision = null;
                _lastChangedRevision = 0;
            }
        }

        public void AcknowledgeSaved()
        {
            _saved = _current;
            _firstChangedRevision = null;
            _lastChangedRevision = 0;
        }

        public AssetRowChange CreateChange()
        {
            if (_firstChangedRevision is not { } firstChanged)
                throw new InvalidOperationException("The row has no pending change.");
            return new AssetRowChange(
                Identity,
                Entry.AssetType,
                Entry.OriginalName,
                Entry.Origin,
                firstChanged,
                _lastChangedRevision);
        }

        public AssetRowChange CreateAddedChange(long firstAddedRevision) =>
            new(
                Identity,
                Entry.AssetType,
                Entry.OriginalName,
                Entry.Origin,
                firstAddedRevision,
                Math.Max(firstAddedRevision, _lastChangedRevision),
                AssetRowChangeKind.Added);
    }

    private void ThrowIfDisposedCore()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastFileEditingSession));
    }
}

/// <summary>One immutable canonical-link revision captured by Save As.</summary>
internal sealed record FastFileSaveRevision(
    long Revision,
    string? SourcePath,
    ZoneLinkRequest LinkRequest);
