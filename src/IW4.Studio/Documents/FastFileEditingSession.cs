using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

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

    public IReadOnlyList<XAssetType> AddableAssetTypes { get; } =
        Array.AsReadOnly(
        [
            XAssetType.RawFile,
            XAssetType.StringTable,
            XAssetType.Localize,
            XAssetType.Menu,
            XAssetType.MenuFile
        ]);

    public CancellationToken CancellationToken => _cancellation.Token;

    public event EventHandler? TargetRowsChanged;

    /// <summary>Raised after a detached authored definition is published.</summary>
    public event EventHandler? AppliedAssetsChanged;

    internal void NotifyTargetRowsChanged() => TargetRowsChanged?.Invoke(this, EventArgs.Empty);

    internal WorkspaceAssetCatalogEntry AddAsset(IW4.Assets.Assets.BaseAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IAssetAuthoringAdapter adapter = RequireHostedAdapter(definition);
        IW4.Assets.Assets.BaseAsset detachedDefinition = adapter.CreateDefinition(
            adapter.CloneDraft(adapter.CreateDraft(definition)));
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
                    IW4.Assets.Assets.BaseAsset definition =
                        _drafts.TryGetValue(identity, out DraftState? draft)
                            ? draft.CreateCurrentDefinition()
                            : entry.Definition!;
                    return new AppliedAssetDefinition(identity, definition);
                })
                .ToArray();
            return new AppliedAssetDefinitionsCapture(_revision.Revision, definitions);
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
        AssetKey[] withdrawnKeys = withdrawnProviderKeys.Distinct().ToArray();

        lock (_gate)
        {
            ThrowIfDisposedCore();
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
            DraftState state = RequireDraft(identity);
            XAssetType assetType = state.Entry.AssetType;
            if (assetType is not (XAssetType.XModel or XAssetType.Font or XAssetType.Weapon) ||
                definition.SerializedAssetType != assetType)
            {
                throw new InvalidOperationException(
                    "Only a matching XModel, Font, or Weapon row can publish compiled dependency providers.");
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
                if (liveFullCollision && !ownedCurrent && !ownedElsewhere)
                    throw new InvalidDataException($"Generated provider key '{key}' collides with an unrelated live provider.");
            }
            AssetKey[] withdrawn = prior.Keys
                .Where(key => !nextKeys.Contains(key) &&
                    !IsCompiledProviderOwnedByOther(identity, key))
                .ToArray();
            changed = PublishAppliedDefinitions(
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
        }
        if (changed)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
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
        AssetKey[] keys = previouslyOwned.Except(
            _compiledAuxiliaryProviders.Values.SelectMany(owned => owned.Keys))
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
