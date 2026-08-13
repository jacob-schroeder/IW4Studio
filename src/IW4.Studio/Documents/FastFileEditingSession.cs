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
    // Generated XModelSurfs are dependency providers, not catalog rows. Their
    // ownership is revision workflow state so a later compiled definition can
    // withdraw only its own prior auxiliaries.
    private readonly Dictionary<TargetZoneRowIdentity, Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset>> _xmodelAuxiliaryProviders = [];
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
        ArgumentNullException.ThrowIfNull(assetTypes);
        var requested = new HashSet<XAssetType>(assetTypes);
        if (requested.Any(type => !Enum.IsDefined(type)))
            throw new ArgumentOutOfRangeException(nameof(assetTypes));

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
        bool raiseAppliedAssetsChanged = true)
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
                withdrawnTargetProviderKeys: withdrawnKeys,
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
        IReadOnlyList<IW4.Assets.Assets.XModel.XModelSurfsAsset> providers)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(providers);
        AssetKey[] suppliedKeys = providers.Select(AssetKey.FromDefinition).Distinct().ToArray();
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            if (RequireDraft(identity).Entry.AssetType != XAssetType.XModel)
                throw new InvalidOperationException("Only an XModel row can publish compiled XModelSurfs providers.");
            Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset> prior =
                _xmodelAuxiliaryProviders.TryGetValue(identity, out Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset>? existing)
                    ? existing : [];
            AssetKey[] nextKeys = ReferencedAuxiliaryKeys(definition, prior.Keys.Concat(suppliedKeys))
                .Concat(ReferencedAuxiliaryKeys(
                    RequireDraft(identity).CreateSavedDefinition() as IW4.Assets.Assets.XModel.XModelAsset,
                    prior.Keys))
                .Distinct().ToArray();
            foreach (AssetKey key in suppliedKeys)
            {
                bool ownedCurrent = prior.ContainsKey(key);
                bool liveCollision = _revision.LinkRequest.Assets.Providers.Any(provider => provider.Key == key);
                if (liveCollision && !ownedCurrent)
                    throw new InvalidDataException($"Generated XModelSurfs provider key '{key}' collides with an unrelated live provider.");
            }
            AssetKey[] withdrawn = prior.Keys.Where(key => !nextKeys.Contains(key)).ToArray();
            changed = PublishAppliedDefinitions(
                [(identity, definition, providers.Cast<IW4.Assets.Assets.BaseAsset>().ToArray())],
                withdrawn,
                raiseAppliedAssetsChanged: false);
            if (changed)
            {
                Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset> next = prior
                    .Where(pair => nextKeys.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (IW4.Assets.Assets.XModel.XModelSurfsAsset provider in providers)
                    next[AssetKey.FromDefinition(provider)] = provider;
                _xmodelAuxiliaryProviders[identity] = next;
            }
        }
        if (changed)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
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
            PublishDefinitionCore(state, candidate);
        }

        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
            if (state.Adapter.AssetType == XAssetType.XModel)
                RevertCompiledXModelCore(identity, state);
            else
                PublishDefinitionCore(state, state.CloneSaved());
        }

        AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RevertCompiledXModelCore(TargetZoneRowIdentity identity, DraftState state)
    {
        if (state.CreateSavedDefinition() is not IW4.Assets.Assets.XModel.XModelAsset saved)
        {
            PublishDefinitionCore(state, state.CloneSaved());
            return;
        }
        Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset> owned =
            _xmodelAuxiliaryProviders.TryGetValue(identity, out Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset>? existing)
                ? existing : [];
        AssetKey[] savedKeys = ReferencedAuxiliaryKeys(saved, owned.Keys).ToArray();
        IW4.Assets.Assets.BaseAsset[] providers = savedKeys
            .Select(key => (IW4.Assets.Assets.BaseAsset)owned[key]).ToArray();
        AssetKey[] withdrawn = owned.Keys.Where(key => !savedKeys.Contains(key)).ToArray();
        _ = PublishAppliedDefinitions(
            [(identity, saved, providers)],
            withdrawn,
            raiseAppliedAssetsChanged: false);
        _xmodelAuxiliaryProviders[identity] = savedKeys
            .ToDictionary(key => key, key => owned[key]);
    }

    private static AssetKey[] ReferencedAuxiliaryKeys(
        IW4.Assets.Assets.XModel.XModelAsset? definition,
        IEnumerable<AssetKey> ownedKeys)
    {
        if (definition is null)
            return [];
        var owned = ownedKeys.ToHashSet();
        return definition.Lods.Select(lod => lod.ModelSurfs)
            .Where(provider => provider is not null)
            .Select(provider => AssetKey.FromDefinition(provider!))
            .Where(owned.Contains).Distinct().ToArray();
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
        var withdrawn = new List<AssetKey>();
        foreach ((TargetZoneRowIdentity identity, Dictionary<AssetKey, IW4.Assets.Assets.XModel.XModelSurfsAsset> owned) in _xmodelAuxiliaryProviders)
        {
            IW4.Assets.Assets.XModel.XModelAsset? current = RequireDraft(identity).CreateCurrentDefinition() as IW4.Assets.Assets.XModel.XModelAsset;
            AssetKey[] retained = ReferencedAuxiliaryKeys(current, owned.Keys);
            withdrawn.AddRange(owned.Keys.Where(key => !retained.Contains(key)));
            _xmodelAuxiliaryProviders[identity] = retained.ToDictionary(key => key, key => owned[key]);
        }
        AssetKey[] keys = withdrawn.Distinct().ToArray();
        if (keys.Length == 0)
            return;
        LinkAssetPool authored = _authoredAssets.WithoutProviders(keys);
        Publish(authored, _revision.LinkRequest.Roots, withdrawnTargetProviderKeys: keys);
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
            previous.SelectedLanguageMask);
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
