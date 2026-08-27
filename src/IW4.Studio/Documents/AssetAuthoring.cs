using IW4.Assets.Assets;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Localize;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.D3dbsp;
using IW4.AssetExchange.XModel;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents.MenuEditing;
using IW4.Unlinker.D3dbsp;

namespace IW4.Studio.Documents;

public sealed class AssetEditorValidationState
{
    internal AssetEditorValidationState(IEnumerable<AssetValidationIssue> issues) =>
        Issues = Array.AsReadOnly(issues.ToArray());

    public IReadOnlyList<AssetValidationIssue> Issues { get; }
    public bool HasErrors => Issues.Any(
        value => value.Severity == AssetValidationSeverity.Error);
}

public abstract class AssetEditorSurface
{
    protected AssetEditorSurface(WorkspaceAssetCatalogEntry entry) =>
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));

    public WorkspaceAssetCatalogEntry Entry { get; }

    public BaseAsset? Definition => Entry.Definition;
}

public sealed class StructuralAssetInspector : AssetEditorSurface
{
    private StructuralAssetInspector(WorkspaceAssetCatalogEntry entry, string reason)
        : base(entry) => Reason = reason;

    public string Reason { get; }
    public bool HasWritableEditor => false;

    public static StructuralAssetInspector Create(
        WorkspaceAssetCatalogEntry entry,
        string reason) => new(entry, reason);
}

public interface IAssetAuthoringAdapter
{
    XAssetType AssetType { get; }
    Type DraftType { get; }
    object CreateDraft(BaseAsset definition);
    object CloneDraft(object draft);
    BaseAsset CreateDefinition(object draft);
    bool SemanticallyEquals(object left, object right);
    IReadOnlyList<AssetValidationIssue> Validate(object draft);
}

public abstract class AssetAuthoringAdapter<TAsset, TDraft> : IAssetAuthoringAdapter
    where TAsset : BaseAsset
    where TDraft : notnull
{
    public abstract XAssetType AssetType { get; }
    public Type DraftType => typeof(TDraft);
    public abstract TDraft CreateDraft(TAsset definition);
    public abstract TDraft CloneDraft(TDraft draft);
    public abstract TAsset CreateDefinition(TDraft draft);
    public virtual IReadOnlyList<AssetValidationIssue> Validate(TDraft draft) => [];
    public virtual bool SemanticallyEquals(TDraft left, TDraft right) =>
        EqualityComparer<TDraft>.Default.Equals(left, right);

    object IAssetAuthoringAdapter.CreateDraft(BaseAsset definition) =>
        CreateDraft((TAsset)definition);
    object IAssetAuthoringAdapter.CloneDraft(object draft) =>
        CloneDraft((TDraft)draft);
    BaseAsset IAssetAuthoringAdapter.CreateDefinition(object draft) =>
        CreateDefinition((TDraft)draft);
    bool IAssetAuthoringAdapter.SemanticallyEquals(object left, object right) =>
        SemanticallyEquals((TDraft)left, (TDraft)right);
    IReadOnlyList<AssetValidationIssue> IAssetAuthoringAdapter.Validate(object draft) =>
        Validate((TDraft)draft);
}

public sealed class AssetAuthoringAdapterRegistry
{
    private readonly Dictionary<XAssetType, IAssetAuthoringAdapter> _adapters = [];

    public IReadOnlyList<XAssetType> AddableAssetTypes =>
        NewAssetDefinitionFactory.SupportedAssetTypes;

    public static AssetAuthoringAdapterRegistry CreateDefault()
    {
        var registry = new AssetAuthoringAdapterRegistry();
        registry.Register(new RawFileAdapter());
        registry.Register(new StringTableAdapter());
        registry.Register(new LocalizeAdapter());
        registry.Register(new MenuAdapter());
        registry.Register(new MenuFileAdapter());
        registry.Register(new MaterialAdapter());
        registry.Register(new XModelAdapter());
        registry.Register(new FontAdapter());
        registry.Register(new WeaponAdapter());
        registry.Register(new StructuredDataAdapter());
        registry.Register(new SoundAdapter());
        foreach (XAssetType assetType in D3dbspAssetTypeFacts.MultiplayerTypes)
            registry.Register(new D3dbspAssetAdapter(assetType));
        return registry;
    }
    public void Register(IAssetAuthoringAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (!_adapters.TryAdd(adapter.AssetType, adapter))
            throw new InvalidOperationException($"An adapter for {adapter.AssetType} is already registered.");
    }

    public bool TryGetAdapter(XAssetType type, out IAssetAuthoringAdapter? adapter) =>
        _adapters.TryGetValue(type, out adapter);

    public IAssetAuthoringAdapter RequireAdapter(XAssetType type) =>
        TryGetAdapter(type, out IAssetAuthoringAdapter? adapter)
            ? adapter!
            : throw new KeyNotFoundException($"No adapter for {type}.");

    public AssetEditorSurface CreateSurface(
        FastFileEditingSession session,
        WorkspaceAssetCatalogEntry entry) =>
        TryGetAdapter(entry.AssetType, out IAssetAuthoringAdapter? adapter) &&
        entry.Definition is not null
            ? new AssetEditorSession(session, entry, adapter!)
            : StructuralAssetInspector.Create(
                entry,
                "No detached authoring adapter is available.");
    public WorkspaceAssetCatalogEntry AddAsset(FastFileEditingSession session, XAssetType type, string name)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        BaseAsset definition = NewAssetDefinitionFactory.Create(type, name);
        IAssetAuthoringAdapter adapter =
            TryGetAdapter(type, out IAssetAuthoringAdapter? registered)
                ? registered!
                : new DetachedNewAssetAdapter(definition);
        return session.AddAsset(definition, adapter);
    }

    /// <summary>
    /// Retains a detached new definition in the editing session when no rich
    /// authoring adapter exists. It is deliberately never registered and has
    /// no writable surface, so unsupported pre-existing rows remain read-only.
    /// </summary>
    private sealed class DetachedNewAssetAdapter : IAssetAuthoringAdapter
    {
        private readonly BaseAsset _definition;

        public DetachedNewAssetAdapter(BaseAsset definition)
        {
            _definition = definition ?? throw new ArgumentNullException(
                nameof(definition));
            AssetType = definition.SerializedAssetType;
        }

        public XAssetType AssetType { get; }
        public Type DraftType => _definition.GetType();

        public object CreateDraft(BaseAsset definition) =>
            RequireDefinition(definition);

        public object CloneDraft(object draft) => RequireDefinition(draft);

        public BaseAsset CreateDefinition(object draft) =>
            RequireDefinition(draft);

        public bool SemanticallyEquals(object left, object right) =>
            ReferenceEquals(RequireDefinition(left), RequireDefinition(right));

        public IReadOnlyList<AssetValidationIssue> Validate(object draft)
        {
            _ = RequireDefinition(draft);
            return [];
        }

        private BaseAsset RequireDefinition(object value)
        {
            if (!ReferenceEquals(value, _definition))
            {
                throw new InvalidDataException(
                    $"The new {AssetType} adapter received another definition.");
            }

            return _definition;
        }
    }
}

/// <summary>
/// D3DBSP assets are replaced only as a synchronized compiled group. The
/// editor never mutates an individual schema object, so its draft is the
/// detached definition produced by the D3DBSP linker.
/// </summary>
internal sealed class D3dbspAssetAdapter : IAssetAuthoringAdapter
{
    public D3dbspAssetAdapter(XAssetType assetType)
    {
        if (!D3dbspAssetTypeFacts.IsMultiplayerType(assetType))
            throw new ArgumentOutOfRangeException(nameof(assetType));

        AssetType = assetType;
    }

    public XAssetType AssetType { get; }

    public Type DraftType => typeof(BaseAsset);

    public object CreateDraft(BaseAsset definition) => RequireDefinition(definition);

    public object CloneDraft(object draft) => RequireDefinition(draft);

    public BaseAsset CreateDefinition(object draft) => RequireDefinition(draft);

    public bool SemanticallyEquals(object left, object right) =>
        ReferenceEquals(RequireDefinition(left), RequireDefinition(right));

    public IReadOnlyList<AssetValidationIssue> Validate(object draft)
    {
        _ = RequireDefinition(draft);
        return [];
    }

    private BaseAsset RequireDefinition(object value)
    {
        if (value is not BaseAsset definition ||
            definition.SerializedAssetType != AssetType)
        {
            throw new InvalidDataException(
                $"The {AssetType} D3DBSP adapter received a different asset type.");
        }

        return definition;
    }
}

public sealed class AssetEditorSession : AssetEditorSurface
{
    private readonly FastFileEditingSession _session;
    private readonly IAssetAuthoringAdapter _adapter;
    private readonly TargetZoneRowIdentity? _rowIdentity;
    private readonly object _readOnlyDraft;
    private bool _closed;

    internal AssetEditorSession(
        FastFileEditingSession session,
        WorkspaceAssetCatalogEntry entry,
        IAssetAuthoringAdapter adapter)
        : base(entry)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        Mode = entry.Access;
        _rowIdentity = Mode == WorkspaceAssetAccess.Editable
            ? entry.TargetRowIdentity ?? throw new InvalidDataException(
                "An editable editor requires a stable target row.")
            : null;
        object draft = _rowIdentity is { } identity
            ? _session.CloneCurrentDraft(identity, _adapter)
            : _adapter.CreateDraft(entry.Definition ?? throw new InvalidDataException(
                "A read-only editor requires a detached definition."));
        _readOnlyDraft = _adapter.CloneDraft(draft);
        Validation = new AssetEditorValidationState(_adapter.Validate(draft));
    }

    public WorkspaceAssetAccess Mode { get; }
    public bool CanEdit => Mode == WorkspaceAssetAccess.Editable;
    public AssetEditorValidationState Validation { get; private set; }
    public FastFileWorkspace Workspace => _session.Workspace;
    public TargetZoneRowIdentity? RowIdentity => _rowIdentity;
    public bool IsDraftOpen => CanEdit && !_closed;
    public bool HasUnsavedChanges =>
        !_closed && _rowIdentity is { } identity &&
        _session.IsDraftChanged(identity, _adapter);

    public T OpenDraft<T>() where T : notnull
    {
        ThrowIfClosed();
        object draft = _rowIdentity is { } identity
            ? _session.CloneCurrentDraft(identity, _adapter)
            : _adapter.CloneDraft(_readOnlyDraft);
        return (T)draft;
    }

    public T ReadDraft<T>() where T : notnull => OpenDraft<T>();

    /// <summary>Resolves a picker name only to a live, typed workspace catalog definition.</summary>
    public bool TryResolveWorkspaceDefinition<T>(string? name, out T? definition)
        where T : IW4.Assets.Assets.BaseAsset
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        WorkspaceAssetCatalogEntry[] matches = Workspace.AssetCatalog.Entries
            .Where(entry => entry.Definition is T && (string.Equals(entry.OriginalName, name, StringComparison.Ordinal) || string.Equals(entry.NormalizedName, name, StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length != 1) return false;
        definition = matches[0].Definition as T;
        return definition is not null;
    }

    /// <summary>
    /// Resolves a live material together with the only XModel inv-high value
    /// proven by loaded XModel usages of that material.
    /// </summary>
    public bool TryResolveWorkspaceXModelMaterialUsage(
        string? name,
        out MaterialAsset? material,
        out ushort invHighMipRadius)
    {
        material = null;
        invHighMipRadius = 0;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        XModelMaterialMapping[] matches = ResolveWorkspaceXModelMaterialUsages(name);
        if (matches.Length != 1)
            return false;
        material = matches[0].Material;
        invHighMipRadius = matches[0].InvHighMipRadius;
        return true;
    }

    /// <summary>Returns live workspace materials whose XModel inv-high value is unambiguous.</summary>
    public IReadOnlyList<XModelMaterialMapping> ResolveWorkspaceXModelMaterialUsages() =>
        Array.AsReadOnly(ResolveWorkspaceXModelMaterialUsages(null));

    /// <summary>Captures the owned provider closure retained by an applied XModel revision.</summary>
    public IReadOnlyList<BaseAsset> CaptureAppliedXModelProviders()
    {
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.XModel || _rowIdentity is not { } identity)
            return [];
        return _session.CaptureAppliedXModelProviders(identity);
    }

    /// <summary>Captures the owned provider closure retained by an applied Weapon revision.</summary>
    public IReadOnlyList<BaseAsset> CaptureAppliedWeaponProviders()
    {
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.Weapon || _rowIdentity is not { } identity)
            return [];
        return _session.CaptureAppliedWeaponProviders(identity);
    }

    /// <summary>Captures every asset represented by this D3DBSP group.</summary>
    public D3dbspWorkspaceAssetGroup CaptureD3dbspGroup()
    {
        ThrowIfClosed();
        return _session.CaptureD3dbspGroup(RequireD3dbspGroupName());
    }

    /// <summary>Replaces this D3DBSP group from one compiled file.</summary>
    public Task<D3dbspWorkspaceImportResult> ImportD3dbspAsync(
        string inputPath,
        bool forceFullbright,
        int fragmentProgramUploadCapacity)
    {
        ThrowIfClosed();
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        return _session.ImportD3dbspAsync(
            inputPath,
            RequireD3dbspGroupName(),
            forceFullbright,
            fragmentProgramUploadCapacity);
    }

    /// <summary>Reconstructs one compiled file from this D3DBSP group.</summary>
    public D3dbspFile CreateD3dbspFile()
    {
        ThrowIfClosed();
        return D3dbspUnlinker.Unlink(CaptureD3dbspGroup().Assets);
    }

    private string RequireD3dbspGroupName()
    {
        string? semanticName = Entry.Definition?.SerializedAssetName;
        string? groupName = semanticName;
        if (!D3dbspAssetTypeFacts.IsOwnedD3dbspGroupName(groupName))
        {
            groupName = Entry.OriginalName;
            if (groupName is { Length: > 1 } && groupName[0] == ',')
                groupName = groupName[1..];
        }
        if (!D3dbspAssetTypeFacts.IsMultiplayerType(_adapter.AssetType) ||
            !D3dbspAssetTypeFacts.IsD3dbspName(Entry.OriginalName) ||
            !D3dbspAssetTypeFacts.IsOwnedD3dbspGroupName(groupName))
        {
            throw new InvalidOperationException(
                "This editor does not represent a usable D3DBSP asset group.");
        }

        return groupName!;
    }

    private XModelMaterialMapping[] ResolveWorkspaceXModelMaterialUsages(string? requestedName)
    {
        long revision = Workspace.LoadedZone.Context.AssetPool.Revision;
        IGrouping<string, (MaterialAsset Material, ushort InvHigh)>[] usages =
            Workspace.AssetCatalog.Entries
                .Select(entry => entry.Definition)
                .OfType<XModelAsset>()
                .SelectMany(model => model.Materials
                    .Select((material, index) => (material, index))
                    .Where(row => row.material is not null &&
                        row.index < model.InvHighMipRadius.Count &&
                        !string.IsNullOrWhiteSpace(row.material.Info.Name))
                    .Select(row => (
                        Material: row.material!,
                        InvHigh: model.InvHighMipRadius[row.index])))
                .Where(row => requestedName is null || string.Equals(
                    row.Material.Info.Name,
                    requestedName,
                    StringComparison.Ordinal))
                .GroupBy(row => row.Material.Info.Name!, StringComparer.Ordinal)
                .ToArray();
        var result = new List<XModelMaterialMapping>(usages.Length);
        foreach (IGrouping<string, (MaterialAsset Material, ushort InvHigh)> usage in usages)
        {
            ushort[] values = usage.Select(row => row.InvHigh).Distinct().Take(2).ToArray();
            if (values.Length != 1 ||
                !Workspace.LoadedZone.Context.AssetPool.TryResolve(
                    XAssetType.Material,
                    usage.Key,
                    out MaterialAsset? current) ||
                current is null ||
                current.RuntimeAddress?.AssetPoolAddress is not { } address ||
                !Workspace.LoadedZone.Context.AssetPool.TryGetSlot(address, out var slot) ||
                slot is null ||
                slot.ActiveProvider.IsReferencePlaceholder)
            {
                continue;
            }
            result.Add(new XModelMaterialMapping(current, values[0]));
        }
        return Workspace.LoadedZone.Context.AssetPool.Revision == revision
            ? result.ToArray()
            : [];
    }

    /// <summary>Resolves a Material name to its current non-placeholder pool definition.</summary>
    public bool TryResolveWorkspaceMaterial(
        string? name,
        out MaterialAsset? material)
    {
        material = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        long revision = Workspace.LoadedZone.Context.AssetPool.Revision;
        if (!Workspace.LoadedZone.Context.AssetPool.TryResolve(
                XAssetType.Material,
                name,
                out MaterialAsset? current) ||
            current is null ||
            current.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !Workspace.LoadedZone.Context.AssetPool.TryGetSlot(address, out var slot) ||
            slot is null ||
            slot.ActiveProvider.IsReferencePlaceholder ||
            Workspace.LoadedZone.Context.AssetPool.Revision != revision)
        {
            return false;
        }

        material = current;
        return true;
    }

    /// <summary>Resolves a TechniqueSet name to its current non-placeholder pool definition.</summary>
    public bool TryResolveWorkspaceTechniqueSet(
        string? name,
        out MaterialTechniqueSetAsset? techniqueSet)
    {
        techniqueSet = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        long revision = Workspace.LoadedZone.Context.AssetPool.Revision;
        if (!Workspace.LoadedZone.Context.AssetPool.TryResolve(
                XAssetType.Techset,
                name,
                out MaterialTechniqueSetAsset? current) ||
            current is null ||
            current.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !Workspace.LoadedZone.Context.AssetPool.TryGetSlot(address, out var slot) ||
            slot is null ||
            slot.ActiveProvider.IsReferencePlaceholder ||
            Workspace.LoadedZone.Context.AssetPool.Revision != revision)
        {
            return false;
        }

        techniqueSet = current;
        return true;
    }

    /// <summary>Validates a local candidate without publishing it to the editing session.</summary>
    public AssetEditorValidationState ValidateCandidate<T>(T candidate) where T : notnull
    {
        ThrowIfClosed();
        return new AssetEditorValidationState(_adapter.Validate(candidate));
    }

    /// <summary>Compares a local candidate with the current session draft without publishing it.</summary>
    public bool CandidateMatchesCurrent<T>(T candidate) where T : notnull
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(candidate);
        object current = _rowIdentity is { } identity
            ? _session.CloneCurrentDraft(identity, _adapter)
            : _adapter.CloneDraft(_readOnlyDraft);
        return _adapter.SemanticallyEquals(current, candidate);
    }

    public bool Apply<T>(Action<T> mutation) where T : notnull
    {
        if (!CanEdit)
            throw new InvalidOperationException("This asset is not editable.");
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(mutation);

        TargetZoneRowIdentity identity = _rowIdentity ?? throw new InvalidOperationException(
            "An editable editor requires a stable target row.");
        object next = _session.CloneCurrentDraft(identity, _adapter);
        mutation((T)next);
        var validation = new AssetEditorValidationState(_adapter.Validate(next));
        if (validation.HasErrors)
            return false;

        bool changed = _session.ApplyDraft(
            identity,
            _adapter,
            next);
        Validation = validation;
        return changed;
    }

    /// <summary>Publishes a compiled XModel and its owned dependency closure in one revision.</summary>
    public bool ApplyCompiledXModel(
        XModelDraft candidate,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!CanEdit)
            throw new InvalidOperationException("This asset is not editable.");
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.XModel)
            throw new InvalidOperationException("This editor does not host an XModel.");
        TargetZoneRowIdentity identity = _rowIdentity ?? throw new InvalidOperationException(
            "An editable editor requires a stable target row.");
        XModelAssemblyCompileResult compiled = XModelAssemblyCompiler.Compile(candidate);
        AssetValidationIssue[] validation = _adapter.Validate(candidate)
            .Concat(compiled.Issues)
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First()).ToArray();
        issues = Array.AsReadOnly(validation);
        if (validation.Any(issue => issue.Severity == AssetValidationSeverity.Error))
            return false;
        bool changed = _session.PublishCompiledXModel(identity, compiled.Definition, compiled.Providers);
        Validation = new AssetEditorValidationState(validation);
        return changed;
    }

    /// <summary>Publishes a compiled Weapon and its isolated camo dependency closure in one revision.</summary>
    public bool ApplyCompiledWeapon(
        WeaponDraft candidate,
        IReadOnlyList<BaseAsset> providers,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(providers);
        if (!CanEdit)
            throw new InvalidOperationException("This asset is not editable.");
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.Weapon)
            throw new InvalidOperationException("This editor does not host a Weapon.");
        TargetZoneRowIdentity identity = _rowIdentity ?? throw new InvalidOperationException(
            "An editable editor requires a stable target row.");
        AssetValidationIssue[] validation = _adapter.Validate(candidate)
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray();
        issues = Array.AsReadOnly(validation);
        if (validation.Any(issue => issue.Severity == AssetValidationSeverity.Error))
            return false;
        bool changed = _session.PublishCompiledDefinition(
            identity,
            candidate.ToAsset(),
            providers);
        Validation = new AssetEditorValidationState(validation);
        return changed;
    }

    /// <summary>Publishes a compiled Font and its owned Material/Image closure in one revision.</summary>
    public bool ApplyCompiledFont(
        FontAsset definition,
        IReadOnlyList<BaseAsset> providers,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(providers);
        if (!CanEdit)
            throw new InvalidOperationException("This asset is not editable.");
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.Font)
            throw new InvalidOperationException("This editor does not host a Font.");
        TargetZoneRowIdentity identity = _rowIdentity ?? throw new InvalidOperationException(
            "An editable editor requires a stable target row.");
        FontDraft candidate = new(definition);
        AssetValidationIssue[] validation = _adapter.Validate(candidate)
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray();
        issues = Array.AsReadOnly(validation);
        if (validation.Any(issue => issue.Severity == AssetValidationSeverity.Error))
            return false;
        bool changed = _session.PublishCompiledDefinition(identity, definition, providers);
        Validation = new AssetEditorValidationState(validation);
        return changed;
    }

    /// <summary>Publishes a compiled Material and its owned Image closure in one revision.</summary>
    public bool ApplyCompiledMaterial(
        MaterialDraft candidate,
        IReadOnlyList<BaseAsset> providers,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(providers);
        if (!CanEdit)
            throw new InvalidOperationException("This asset is not editable.");
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.Material)
            throw new InvalidOperationException("This editor does not host a Material.");
        TargetZoneRowIdentity identity = _rowIdentity ?? throw new InvalidOperationException(
            "An editable editor requires a stable target row.");
        AssetValidationIssue[] validation = _adapter.Validate(candidate)
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray();
        issues = Array.AsReadOnly(validation);
        if (validation.Any(issue => issue.Severity == AssetValidationSeverity.Error))
            return false;
        bool changed = _session.PublishCompiledDefinition(
            identity,
            candidate.ToAsset(),
            providers);
        Validation = new AssetEditorValidationState(validation);
        return changed;
    }

    /// <summary>
    /// Captures the current detached LoadedSound payload when it belongs to
    /// the target zone. Shared payloads are isolated on publication.
    /// </summary>
    public bool TryCaptureEditableSoundPayload(
        int aliasIndex,
        int fileIndex,
        out LoadedSound? payload,
        out string reason)
    {
        ThrowIfClosed();
        payload = null;
        if (_adapter.AssetType != XAssetType.Sound)
        {
            reason = "This editor does not host a Sound.";
            return false;
        }
        if (!CanEdit || _rowIdentity is not { } identity)
        {
            reason = "Only Sound assets owned by the current fastfile/zone can be modified.";
            return false;
        }

        return _session.TryCaptureEditableSoundPayload(
            identity,
            aliasIndex,
            fileIndex,
            out payload,
            out reason);
    }

    /// <summary>
    /// Publishes a detached LoadedSound replacement and repoints every
    /// same-key reference within this Sound in one editing-session revision.
    /// </summary>
    public bool ApplyCompiledSound(
        int aliasIndex,
        int fileIndex,
        LoadedSound replacement,
        out IReadOnlyList<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ThrowIfClosed();
        if (_adapter.AssetType != XAssetType.Sound)
            throw new InvalidOperationException("This editor does not host a Sound.");
        if (!CanEdit || _rowIdentity is not { } identity)
        {
            issues = Array.AsReadOnly([new AssetValidationIssue(
                $"sound.aliases[{aliasIndex}].soundFiles[{fileIndex}]",
                "Only Sound assets owned by the current fastfile/zone can be modified.",
                AssetValidationSeverity.Error)]);
            Validation = new AssetEditorValidationState(issues);
            return false;
        }

        bool changed = _session.ApplyCompiledSound(
            identity,
            aliasIndex,
            fileIndex,
            replacement,
            out issues);
        Validation = new AssetEditorValidationState(issues);
        return changed;
    }

    public T ApplyAndRead<T>(Action<T> mutation) where T : notnull
    {
        _ = Apply(mutation);
        return ReadDraft<T>();
    }

    public T ApplyAndRead<T>(Action<T> mutation, out bool changed)
        where T : notnull
    {
        changed = Apply(mutation);
        return ReadDraft<T>();
    }

    public bool RevertDraft()
    {
        if (!CanEdit || _closed)
            return false;

        TargetZoneRowIdentity identity = _rowIdentity ?? throw new InvalidOperationException(
            "An editable editor requires a stable target row.");
        bool reverted = _session.RevertDraft(identity, _adapter);
        object baseline = _session.CloneCurrentDraft(identity, _adapter);
        Validation = new AssetEditorValidationState(_adapter.Validate(baseline));
        return reverted;
    }

    public bool Revert() => RevertDraft();

    public AssetEditorValidationState RefreshValidation()
    {
        ThrowIfClosed();
        object draft = _rowIdentity is { } identity
            ? _session.CloneCurrentDraft(identity, _adapter)
            : _adapter.CloneDraft(_readOnlyDraft);
        Validation = new AssetEditorValidationState(_adapter.Validate(draft));
        return Validation;
    }

    /// <summary>Releases this editor view without releasing document draft state.</summary>
    public void Close() => _closed = true;

    private void ThrowIfClosed()
    {
        if (_closed)
            throw new ObjectDisposedException(nameof(AssetEditorSession));
    }
}

public sealed record StringTableCellDraft(string? Value, int Hash);

public sealed class StringTableDraft
{
    private readonly List<StringTableCellDraft> _cells;

    public StringTableDraft(StringTableAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Name = value.Name;
        RowCount = value.RowCount;
        ColumnCount = value.ColumnCount;
        _cells = value.Cells.Select(
            cell => new StringTableCellDraft(cell.String, cell.Hash)).ToList();
    }

    private StringTableDraft(StringTableDraft value)
    {
        Name = value.Name;
        RowCount = value.RowCount;
        ColumnCount = value.ColumnCount;
        _cells = value._cells.ToList();
    }

    public string? Name { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<StringTableCellDraft> Cells => _cells;
    public int NullCellCount => _cells.Count(value => value.Value is null);

    public void SetCellValue(int row, int column, string? value)
    {
        int index = checked(row * ColumnCount + column);
        _cells[index] = _cells[index] with { Value = value };
    }

    internal StringTableDraft Clone() => new(this);

    internal StringTableAsset ToAsset() => new()
    {
        Name = Name,
        RowCount = RowCount,
        ColumnCount = ColumnCount,
        Cells = _cells.Select(value => new StringTableCell
        {
            String = value.Value,
            Hash = value.Hash
        }).ToArray()
    };
}

public sealed class StringTableReadOnlySnapshot
{
    private StringTableReadOnlySnapshot(StringTableDraft draft) => Draft = draft;
    private StringTableDraft Draft { get; }
    public string? Name => Draft.Name;
    public int RowCount => Draft.RowCount;
    public int ColumnCount => Draft.ColumnCount;
    public IReadOnlyList<StringTableCellDraft> Cells => Draft.Cells;

    public static StringTableReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        if (editorSession.Entry.Definition is not StringTableAsset definition)
            throw new InvalidDataException("The selected provider is not a StringTable definition.");
        return new StringTableReadOnlySnapshot(new StringTableDraft(definition));
    }
}

public sealed class LocalizeDraft
{
    public LocalizeDraft(LocalizeAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Name = value.Name;
        Value = value.Value;
    }

    private LocalizeDraft(LocalizeDraft value)
    {
        Name = value.Name;
        Value = value.Value;
    }

    public string? Name { get; }
    public string? Value { get; private set; }

    public void SetValue(string? value) => Value = value;
    internal LocalizeDraft Clone() => new(this);
    internal LocalizeAsset ToAsset() => new() { Name = Name, Value = Value };
}

public sealed class LocalizeReadOnlySnapshot
{
    private LocalizeReadOnlySnapshot(LocalizeDraft draft) => Draft = draft;
    private LocalizeDraft Draft { get; }
    public string? Name => Draft.Name;
    public string? Value => Draft.Value;

    public static LocalizeReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        if (editorSession.Entry.Definition is not LocalizeAsset definition)
            throw new InvalidDataException("The selected provider is not a Localize definition.");
        return new LocalizeReadOnlySnapshot(new LocalizeDraft(definition));
    }
}
internal sealed class RawFileAdapter : AssetAuthoringAdapter<RawFileAsset, RawFileDraft>
{
    public override XAssetType AssetType => XAssetType.RawFile;
    public override RawFileDraft CreateDraft(RawFileAsset value) => new(value);
    public override RawFileDraft CloneDraft(RawFileDraft value) => value.Clone();
    public override RawFileAsset CreateDefinition(RawFileDraft value) => value.ToAsset();
    public override bool SemanticallyEquals(RawFileDraft left, RawFileDraft right) =>
        string.Equals(left.OriginalName, right.OriginalName, StringComparison.Ordinal) &&
        left.Mode == right.Mode &&
        left.HasBuffer == right.HasBuffer &&
        left.CompressedLength == right.CompressedLength &&
        left.UncompressedLength == right.UncompressedLength &&
        left.GetSerializedPayloadCopy().AsSpan().SequenceEqual(
            right.GetSerializedPayloadCopy());
}

internal sealed class StringTableAdapter : AssetAuthoringAdapter<StringTableAsset, StringTableDraft>
{
    public override XAssetType AssetType => XAssetType.StringTable;
    public override StringTableDraft CreateDraft(StringTableAsset value) => new(value);
    public override StringTableDraft CloneDraft(StringTableDraft value) => value.Clone();
    public override StringTableAsset CreateDefinition(StringTableDraft value) => value.ToAsset();
    public override bool SemanticallyEquals(StringTableDraft left, StringTableDraft right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.RowCount == right.RowCount &&
        left.ColumnCount == right.ColumnCount &&
        left.Cells.SequenceEqual(right.Cells);
}

internal sealed class LocalizeAdapter : AssetAuthoringAdapter<LocalizeAsset, LocalizeDraft>
{
    public override XAssetType AssetType => XAssetType.Localize;
    public override LocalizeDraft CreateDraft(LocalizeAsset value) => new(value);
    public override LocalizeDraft CloneDraft(LocalizeDraft value) => value.Clone();
    public override LocalizeAsset CreateDefinition(LocalizeDraft value) => value.ToAsset();
    public override bool SemanticallyEquals(LocalizeDraft left, LocalizeDraft right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        string.Equals(left.Value, right.Value, StringComparison.Ordinal);
}
