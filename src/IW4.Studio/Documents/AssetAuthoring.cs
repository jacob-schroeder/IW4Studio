using IW4.Assets.Assets;
using IW4.Assets.Assets.Localize;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents.MenuEditing;

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
    public static AssetAuthoringAdapterRegistry CreateDefault()
    {
        var registry = new AssetAuthoringAdapterRegistry();
        registry.Register(new RawFileAdapter());
        registry.Register(new StringTableAdapter());
        registry.Register(new LocalizeAdapter());
        registry.Register(new MenuAdapter());
        registry.Register(new MenuFileAdapter());
        registry.Register(new XModelAdapter());
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
        BaseAsset definition = type switch
        {
            XAssetType.RawFile => new RawFileAsset { Name = name, Buffer = [0], Len = 0 },
            XAssetType.StringTable => new StringTableAsset { Name = name, RowCount = 0, ColumnCount = 0, Cells = [] },
            XAssetType.Localize => new LocalizeAsset { Name = name, Value = string.Empty },
            XAssetType.Menu => MenuAuthoringDefaults.CreateMenu(name),
            XAssetType.MenuFile => new MenuFileAsset { Name = name, MenuCount = 0, Menus = [] },
            _ => throw new NotSupportedException($"New {type} authoring is not implemented.")
        };
        if (!TryGetAdapter(type, out _))
            throw new NotSupportedException($"New {type} authoring is not implemented.");
        return session.AddAsset(definition);
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
