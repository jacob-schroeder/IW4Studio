using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>Explicit editor mode derived from centralized catalog access.</summary>
public enum AssetEditorMode
{
    Editable,
    ReadOnly,
    ContentUnavailable
}

/// <summary>Severity of one field-addressable draft validation result.</summary>
public enum AssetValidationSeverity
{
    Warning,
    Error
}

/// <summary>
/// Validation result addressed to a stable logical field path rather than a
/// visual control. Paths are adapter-defined and never runtime addresses.
/// </summary>
public sealed record AssetValidationIssue
{
    public AssetValidationIssue(
        string fieldPath,
        string message,
        AssetValidationSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        FieldPath = fieldPath;
        Message = message;
        Severity = severity;
    }

    public string FieldPath { get; }

    public string Message { get; }

    public AssetValidationSeverity Severity { get; }
}

/// <summary>Immutable validation state carried by one editor session.</summary>
public sealed class AssetEditorValidationState
{
    private readonly IReadOnlyList<AssetValidationIssue> _issues;

    internal AssetEditorValidationState(bool isValidated, IEnumerable<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        AssetValidationIssue[] copied = issues.ToArray();
        if (copied.Any(issue => issue is null))
            throw new InvalidDataException("Asset validation cannot contain a null issue.");

        IsValidated = isValidated;
        _issues = Array.AsReadOnly(copied);
    }

    public bool IsValidated { get; }

    public IReadOnlyList<AssetValidationIssue> Issues => _issues;

    public bool HasErrors => _issues.Any(issue => issue.Severity == AssetValidationSeverity.Error);

    public bool IsValid => IsValidated && !HasErrors;

    internal static AssetEditorValidationState Unvalidated { get; } = new(false, []);
}

/// <summary>
/// Backend per-type extension seam. All values must be detached authored data:
/// no runtime <c>BaseAsset</c>, pool handle, staging address, or XZone memory
/// may be retained by an adapter output.
/// </summary>
public interface IAssetAuthoringAdapter
{
    XAssetType AssetType { get; }
    Type SnapshotType { get; }
    Type DraftType { get; }
    Type BuildDataType { get; }

    object ImportAuthoredSnapshot(TargetZoneRowSource source);
    object CreateDraft(object authoredSnapshot);
    object CloneDraft(object draft);
    IReadOnlyList<AssetValidationIssue> ValidateDraft(object draft);
    bool SemanticallyEquals(object baseline, object current);
    object ExportBuildData(object draft);
}

/// <summary>
/// Strongly typed convenience base for backend adapters. The registry wraps
/// every adapter with runtime type validation so malformed non-generic
/// adapters also fail closed at their boundary.
/// </summary>
public abstract class AssetAuthoringAdapter<TSnapshot, TDraft, TBuildData> : IAssetAuthoringAdapter
    where TSnapshot : notnull
    where TDraft : notnull
    where TBuildData : notnull
{
    public abstract XAssetType AssetType { get; }

    public Type SnapshotType => typeof(TSnapshot);

    public Type DraftType => typeof(TDraft);

    public Type BuildDataType => typeof(TBuildData);

    public abstract TSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source);

    public abstract TDraft CreateDraft(TSnapshot authoredSnapshot);

    public abstract TDraft CloneDraft(TDraft draft);

    public abstract IReadOnlyList<AssetValidationIssue> ValidateDraft(TDraft draft);

    public abstract bool SemanticallyEquals(TDraft baseline, TDraft current);

    public abstract TBuildData ExportBuildData(TDraft draft);

    object IAssetAuthoringAdapter.ImportAuthoredSnapshot(TargetZoneRowSource source) =>
        ImportAuthoredSnapshot(source);

    object IAssetAuthoringAdapter.CreateDraft(object authoredSnapshot) =>
        CreateDraft(RequireType<TSnapshot>(authoredSnapshot, "authored snapshot"));

    object IAssetAuthoringAdapter.CloneDraft(object draft) =>
        CloneDraft(RequireType<TDraft>(draft, "draft"));

    IReadOnlyList<AssetValidationIssue> IAssetAuthoringAdapter.ValidateDraft(object draft) =>
        ValidateDraft(RequireType<TDraft>(draft, "draft"));

    bool IAssetAuthoringAdapter.SemanticallyEquals(object baseline, object current) =>
        SemanticallyEquals(
            RequireType<TDraft>(baseline, "baseline draft"),
            RequireType<TDraft>(current, "current draft"));

    object IAssetAuthoringAdapter.ExportBuildData(object draft) =>
        ExportBuildData(RequireType<TDraft>(draft, "draft"));

    private static TValue RequireType<TValue>(object value, string role)
        where TValue : notnull
    {
        if (value is not TValue typed)
        {
            throw new InvalidDataException(
                $"Asset adapter expected {role} type '{typeof(TValue).FullName}', " +
                $"but received '{value?.GetType().FullName ?? "null"}'.");
        }

        return typed;
    }
}

/// <summary>Common result of backend editor-registry resolution.</summary>
public abstract class AssetEditorSurface
{
    protected AssetEditorSurface(WorkspaceAssetCatalogEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public WorkspaceAssetCatalogEntry Entry { get; }
}

/// <summary>
/// Structural fallback for an unregistered type. It intentionally exposes
/// metadata only and never advertises a writable editor entitlement.
/// </summary>
public sealed class StructuralAssetInspector : AssetEditorSurface
{
    internal StructuralAssetInspector(WorkspaceAssetCatalogEntry entry, string reason)
        : base(entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }

    public bool HasWritableEditor => false;

    public XAssetType SerializedType => Entry.AssetType;

    public string? OriginalSerializedName => Entry.OriginalName;

    public int? RawHeader => Entry.RawHeader;

    public XAssetHeaderKind? HeaderKind => Entry.HeaderKind;

    /// <summary>
    /// Creates an explicit non-writable fallback when a backend adapter exists
    /// but a caller cannot host it (for example, no Desktop view factory).
    /// </summary>
    public static StructuralAssetInspector Create(
        WorkspaceAssetCatalogEntry entry,
        string reason) =>
        new(entry, reason);
}

/// <summary>
/// Backend editor façade for one target row. Its commands route to
/// <see cref="FastFileEditingSession"/>; it owns neither the draft nor its
/// access policy and can be recreated whenever a visual editor is reopened.
/// </summary>
public sealed class AssetEditorSession : AssetEditorSurface
{
    private readonly FastFileEditingSession _editingSession;
    private readonly RegisteredAssetAuthoringAdapter _adapter;
    private readonly TargetZoneRowIdentity? _rowIdentity;
    private AssetEditorValidationState _validation = AssetEditorValidationState.Unvalidated;

    internal AssetEditorSession(
        FastFileEditingSession editingSession,
        WorkspaceAssetCatalogEntry entry,
        RegisteredAssetAuthoringAdapter adapter)
        : base(entry)
    {
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(adapter);
        TargetZoneRowIdentity? identity = entry.TargetRowIdentity;
        if (identity is { } targetIdentity &&
            targetIdentity.DocumentId != editingSession.Document.DocumentId)
        {
            throw new InvalidDataException(
                "Asset editor registration does not match the selected target row identity.");
        }
        if (entry.AssetType != adapter.AssetType)
        {
            throw new InvalidDataException(
                "Asset editor registration does not match the selected serialized type.");
        }
        if (identity is null &&
            (entry.Origin != WorkspaceAssetOrigin.DependencyOnly ||
             entry.Access != WorkspaceAssetAccess.ReadOnly))
        {
            throw new InvalidDataException(
                "Only resolved dependency-only content may open a read-only editor session without a target row.");
        }

        _editingSession = editingSession;
        _adapter = adapter;
        _rowIdentity = identity;
        Mode = entry.Access switch
        {
            WorkspaceAssetAccess.Editable => AssetEditorMode.Editable,
            WorkspaceAssetAccess.ReadOnly => AssetEditorMode.ReadOnly,
            WorkspaceAssetAccess.ContentUnavailable => AssetEditorMode.ContentUnavailable,
            _ => throw new InvalidDataException($"Unknown workspace access value '{entry.Access}'.")
        };
    }

    /// <summary>Present only when the surface is associated with a target row.</summary>
    public TargetZoneRowIdentity? RowIdentity => _rowIdentity;

    public FastFileWorkspace Workspace => _editingSession.Workspace;

    public AssetEditorMode Mode { get; }

    public Type SnapshotType => _adapter.SnapshotType;

    public Type DraftType => _adapter.DraftType;

    public Type BuildDataType => _adapter.BuildDataType;

    public bool CanEdit => Mode == AssetEditorMode.Editable;

    public bool IsDraftOpen => _rowIdentity is { } identity && _editingSession.IsDraftOpen(identity);

    public bool HasUnsavedChanges =>
        _rowIdentity is { } identity &&
        _editingSession.ChangeSet.TryGetChange(identity, out _);

    public AssetEditorValidationState Validation => _validation;

    public object OpenDraft()
    {
        TargetZoneRowIdentity identity = RequireEditableRowIdentity();
        object draft = _editingSession.OpenDraft<object>(identity, _adapter);
        RefreshValidationFrom(draft);
        return draft;
    }

    public TDraft OpenDraft<TDraft>()
        where TDraft : notnull =>
        RequireDraftType<TDraft>(OpenDraft());

    public object ReadDraft()
    {
        TargetZoneRowIdentity identity = RequireEditableRowIdentity();
        return _editingSession.ReadDraft<object>(identity, _adapter);
    }

    public TDraft ReadDraft<TDraft>()
        where TDraft : notnull =>
        RequireDraftType<TDraft>(ReadDraft());

    public bool Apply(Action<object> mutation)
    {
        return ApplyCore(mutation, captureCurrent: false, out _);
    }

    public bool Apply<TDraft>(Action<TDraft> mutation)
        where TDraft : notnull
    {
        ArgumentNullException.ThrowIfNull(mutation);
        EnsureDeclaredDraftType<TDraft>();
        return Apply(draft => mutation(RequireDraftType<TDraft>(draft)));
    }

    /// <summary>
    /// Applies a mutation and returns the exact detached current draft used
    /// for post-mutation validation. Callers that retain a local projection
    /// can stay synchronized without cloning the draft a second time.
    /// </summary>
    public TDraft ApplyAndRead<TDraft>(
        Action<TDraft> mutation,
        out bool changed)
        where TDraft : notnull
    {
        ArgumentNullException.ThrowIfNull(mutation);
        EnsureDeclaredDraftType<TDraft>();
        changed = ApplyCore(
            draft => mutation(RequireDraftType<TDraft>(draft)),
            captureCurrent: true,
            out object? currentDraft);
        return RequireDraftType<TDraft>(currentDraft!);
    }

    public bool Revert()
    {
        TargetZoneRowIdentity identity = RequireEditableRowIdentity();
        bool reverted = _editingSession.RevertOne(identity);
        if (reverted)
            RefreshValidationFrom(ReadDraft());

        return reverted;
    }

    public void Close()
    {
        if (_rowIdentity is { } identity)
            _editingSession.CloseDraft(identity);
    }

    public AssetEditorValidationState RefreshValidation()
    {
        RequireEditable();
        RefreshValidationFrom(ReadDraft());
        return _validation;
    }

    public object ExportBuildData()
    {
        TargetZoneRowIdentity identity = RequireEditableRowIdentity();
        object draft = ReadDraft();
        RefreshValidationFrom(draft);
        if (_validation.HasErrors)
        {
            throw new InvalidOperationException(
                $"Target row {identity.SerializedIndex} has validation errors and cannot export build data.");
        }

        return _adapter.ExportBuildData(draft);
    }

    public TBuildData ExportBuildData<TBuildData>()
        where TBuildData : notnull =>
        RequireBuildType<TBuildData>(ExportBuildData());

    private void RefreshValidationFrom(object draft) =>
        _validation = new AssetEditorValidationState(true, _adapter.ValidateDraft(draft));

    private bool ApplyCore(
        Action<object> mutation,
        bool captureCurrent,
        out object? currentDraft)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        TargetZoneRowIdentity identity = RequireEditableRowIdentity();
        bool changed = _editingSession.MutateDraft<object>(
            identity,
            _adapter,
            mutation);
        currentDraft = changed || captureCurrent
            ? _editingSession.ReadDraft<object>(identity, _adapter)
            : null;
        if (changed)
            RefreshValidationFrom(currentDraft!);

        return changed;
    }

    private void RequireEditable()
    {
        if (CanEdit)
            return;

        throw new InvalidOperationException(
            $"{(_rowIdentity is { } identity ? $"Target row {identity.SerializedIndex}" : "Dependency content")} " +
            $"is {Mode} and cannot mutate an editor draft.");
    }

    private TargetZoneRowIdentity RequireEditableRowIdentity()
    {
        RequireEditable();
        return _rowIdentity ?? throw new InvalidOperationException(
            "Dependency-only content cannot own an editable target draft.");
    }

    private void EnsureDeclaredDraftType<TDraft>()
        where TDraft : notnull
    {
        if (_adapter.DraftType != typeof(TDraft))
        {
            throw new InvalidOperationException(
                $"Asset editor for {_adapter.AssetType} declares draft type '{_adapter.DraftType.FullName}', " +
                $"not '{typeof(TDraft).FullName}'.");
        }
    }

    private TExpected RequireDraftType<TExpected>(object value)
        where TExpected : notnull
    {
        EnsureDeclaredDraftType<TExpected>();
        return RequireType<TExpected>(value, "draft");
    }

    private TExpected RequireBuildType<TExpected>(object value)
        where TExpected : notnull
    {
        if (_adapter.BuildDataType != typeof(TExpected))
        {
            throw new InvalidOperationException(
                $"Asset editor for {_adapter.AssetType} declares build-data type '{_adapter.BuildDataType.FullName}', " +
                $"not '{typeof(TExpected).FullName}'.");
        }

        return RequireType<TExpected>(value, "build data");
    }

    private static TValue RequireType<TValue>(object value, string role)
        where TValue : notnull
    {
        if (value is not TValue typed)
        {
            throw new InvalidDataException(
                $"Asset editor returned {role} type '{value?.GetType().FullName ?? "null"}', " +
                $"expected '{typeof(TValue).FullName}'.");
        }

        return typed;
    }
}

/// <summary>
/// Backend registry keyed by serialized <see cref="XAssetType"/>. It is the
/// sole lookup that determines whether a row has a backend editor adapter;
/// unregistered types resolve to <see cref="StructuralAssetInspector"/>.
/// </summary>
public sealed class AssetAuthoringAdapterRegistry
{
    private readonly Dictionary<XAssetType, RegisteredAssetAuthoringAdapter> _registrations = [];

    /// <summary>Production backend registrations available in this Studio step.</summary>
    public static AssetAuthoringAdapterRegistry CreateDefault()
    {
        var registry = new AssetAuthoringAdapterRegistry();
        registry.Register(new RawFileAuthoringAdapter());
        registry.Register(new LocalizeAuthoringAdapter());
        registry.Register(new StringTableAuthoringAdapter());
        registry.Register(new StructuredDataAuthoringAdapter());
        registry.Register(new PhysPresetAuthoringAdapter());
        registry.Register(new PhysCollmapAuthoringAdapter());
        registry.Register(new XAnimAuthoringAdapter());
        registry.Register(new XModelAuthoringAdapter());
        registry.Register(new SoundAuthoringAdapter());
        registry.Register(new FxAuthoringAdapter());
        registry.Register(new ImpactFxAuthoringAdapter());
        registry.Register(new SndCurveAuthoringAdapter());
        registry.Register(new LeaderboardAuthoringAdapter());
        registry.Register(new TracerAuthoringAdapter());
        registry.Register(new LightDefAuthoringAdapter());
        registry.Register(new ComWorldAuthoringAdapter());
        registry.Register(new GameWorldSpAuthoringAdapter());
        registry.Register(new GameWorldMpAuthoringAdapter());
        registry.Register(new FxWorldAuthoringAdapter());
        registry.Register(new ClipMapAuthoringAdapter(XAssetType.ColMapSp));
        registry.Register(new ClipMapAuthoringAdapter(XAssetType.ColMapMp));
        registry.Register(new GfxWorldAuthoringAdapter());
        registry.Register(new VehicleAuthoringAdapter());
        registry.Register(new WeaponAuthoringAdapter());
        registry.Register(new MenuAuthoringAdapter());
        registry.Register(new MenuFileAuthoringAdapter());
        registry.Register(new MapEntsAuthoringAdapter());
        registry.Register(new AddonMapEntsAuthoringAdapter());
        registry.Register(new MaterialShaderAuthoringAdapter(XAssetType.PixelShader));
        registry.Register(new MaterialShaderAuthoringAdapter(XAssetType.VertexShader));
        registry.Register(new LoadedSoundAuthoringAdapter());
        registry.Register(new GfxImageAuthoringAdapter());
        registry.Register(new FontAuthoringAdapter());
        registry.Register(new TechniqueSetAuthoringAdapter());
        registry.Register(new MaterialAuthoringAdapter());
        return registry;
    }

    public void Register(IAssetAuthoringAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateAdapterMetadata(adapter);
        if (!_registrations.TryAdd(adapter.AssetType, new RegisteredAssetAuthoringAdapter(adapter)))
        {
            throw new InvalidOperationException(
                $"An asset authoring adapter is already registered for serialized type '{adapter.AssetType}'.");
        }
    }

    public bool TryGetAdapter(XAssetType assetType, out IAssetAuthoringAdapter? adapter)
    {
        if (_registrations.TryGetValue(assetType, out RegisteredAssetAuthoringAdapter? registration))
        {
            adapter = registration.Adapter;
            return true;
        }

        adapter = null;
        return false;
    }

    public IAssetAuthoringAdapter RequireAdapter(XAssetType assetType) =>
        TryGetAdapter(assetType, out IAssetAuthoringAdapter? adapter)
            ? adapter!
            : throw new KeyNotFoundException(
                $"No backend asset authoring adapter is registered for serialized type '{assetType}'. " +
                "Use the structural inspector fallback or register a detached adapter.");

    public AssetEditorSurface CreateSurface(
        FastFileEditingSession editingSession,
        WorkspaceAssetCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(entry);
        if (!_registrations.TryGetValue(entry.AssetType, out RegisteredAssetAuthoringAdapter? registration))
        {
            return new StructuralAssetInspector(
                entry,
                $"No backend authoring adapter is registered for serialized type '{entry.AssetType}'.");
        }

        return new AssetEditorSession(editingSession, entry, registration);
    }

    private static void ValidateAdapterMetadata(IAssetAuthoringAdapter adapter)
    {
        if (!Enum.IsDefined(adapter.AssetType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(adapter),
                $"Asset adapter type '{adapter.AssetType}' is not a defined serialized XAssetType.");
        }

        ValidateValueType(adapter.SnapshotType, "snapshot");
        ValidateValueType(adapter.DraftType, "draft");
        ValidateValueType(adapter.BuildDataType, "build-data");
    }

    private static void ValidateValueType(Type? type, string role)
    {
        if (type is null || type == typeof(void) || type.IsByRef || type.IsPointer || type.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"Asset adapter declares an incompatible {role} type '{type?.FullName ?? "null"}'.");
        }
    }
}

/// <summary>
/// Registry-owned validation bridge. This is the stable adapter instance
/// supplied to Step 06, so recreated visual sessions reopen the same retained
/// draft rather than owning a control-local copy.
/// </summary>
internal interface IDeclaredDraftTypeAdapter
{
    Type DraftType { get; }
}

internal sealed class RegisteredAssetAuthoringAdapter :
    ITargetZoneDraftAdapter<object>,
    IDeclaredDraftTypeAdapter
{
    public RegisteredAssetAuthoringAdapter(IAssetAuthoringAdapter adapter)
    {
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public IAssetAuthoringAdapter Adapter { get; }

    public XAssetType AssetType => Adapter.AssetType;

    public Type SnapshotType => Adapter.SnapshotType;

    public Type DraftType => Adapter.DraftType;

    public Type BuildDataType => Adapter.BuildDataType;

    public object CreateBaseline(TargetZoneRowSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        object snapshot = RequireType(
            Adapter.ImportAuthoredSnapshot(source),
            SnapshotType,
            "imported authored snapshot");
        object draft = RequireType(
            Adapter.CreateDraft(snapshot),
            DraftType,
            "created draft");
        return Clone(draft);
    }

    public object Clone(object draft) =>
        RequireType(Adapter.CloneDraft(RequireType(draft, DraftType, "draft")), DraftType, "cloned draft");

    public bool SemanticallyEquals(object baseline, object current) =>
        Adapter.SemanticallyEquals(
            RequireType(baseline, DraftType, "baseline draft"),
            RequireType(current, DraftType, "current draft"));

    public IReadOnlyList<AssetValidationIssue> ValidateDraft(object draft)
    {
        IReadOnlyList<AssetValidationIssue> issues = Adapter.ValidateDraft(
            RequireType(draft, DraftType, "draft"));
        if (issues is null || issues.Any(issue => issue is null))
            throw new InvalidDataException("Asset adapter returned invalid validation results.");

        return Array.AsReadOnly(issues.ToArray());
    }

    public object ExportBuildData(object draft) =>
        RequireType(
            Adapter.ExportBuildData(RequireType(draft, DraftType, "draft")),
            BuildDataType,
            "exported build data");

    private static object RequireType(object? value, Type expectedType, string role)
    {
        if (value is null || !expectedType.IsInstanceOfType(value))
        {
            throw new InvalidDataException(
                $"Asset adapter returned {role} type '{value?.GetType().FullName ?? "null"}', " +
                $"expected '{expectedType.FullName}'.");
        }

        return value;
    }
}
