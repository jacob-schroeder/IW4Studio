using System.Globalization;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed record StringTableColumnHeaderViewModel(
    int Column,
    string Label);

public sealed class StringTableRowEditorViewModel
{
    private readonly IReadOnlyList<StringTableCellDraft> _sourceCells;
    private readonly int _columnCount;
    private readonly bool _canEdit;
    private readonly bool _hasConfigStringFooters;
    private readonly IReadOnlyDictionary<int, string?>? _configStringValues;
    private readonly Action<int, int, string?> _applyValue;
    private IReadOnlyList<StringTableCellEditorViewModel>? _cells;

    internal StringTableRowEditorViewModel(
        int row,
        int columnCount,
        IReadOnlyList<StringTableCellDraft> sourceCells,
        bool canEdit,
        bool hasConfigStringFooters,
        IReadOnlyDictionary<int, string?>? configStringValues,
        Action<int, int, string?> applyValue)
    {
        ArgumentNullException.ThrowIfNull(sourceCells);
        ArgumentNullException.ThrowIfNull(applyValue);
        Row = row;
        Label = row.ToString();
        _columnCount = columnCount;
        _sourceCells = sourceCells;
        _canEdit = canEdit;
        _hasConfigStringFooters = hasConfigStringFooters;
        _configStringValues = configStringValues;
        _applyValue = applyValue;
    }

    public int Row { get; }
    public string Label { get; }
    public IReadOnlyList<StringTableCellEditorViewModel> Cells =>
        _cells ??= CreateCells();

    private IReadOnlyList<StringTableCellEditorViewModel> CreateCells()
    {
        var cells = new StringTableCellEditorViewModel[_columnCount];
        int rowOffset = checked(Row * _columnCount);
        string? configStringIndexValue = _columnCount == 0
            ? null
            : _sourceCells[rowOffset].Value;
        for (int column = 0; column < cells.Length; column++)
        {
            cells[column] = new StringTableCellEditorViewModel(
                Row,
                column,
                _sourceCells[rowOffset + column],
                _canEdit,
                _hasConfigStringFooters,
                _configStringValues,
                configStringIndexValue,
                _applyValue);
        }

        return Array.AsReadOnly(cells);
    }
}

/// <summary>
/// One row-major cell projection. Editing the value updates only the local
/// staged draft; the serialized hash remains an explicit, preserved source
/// value.
/// </summary>
public sealed class StringTableCellEditorViewModel : ObservableObject
{
    private readonly Action<int, int, string?> _applyValue;
    private readonly bool _hasConfigStringFooter;
    private readonly IReadOnlyDictionary<int, string?>? _configStringValues;
    private readonly string? _configStringIndexValue;
    private string _valueInput;
    private bool _isNull;

    internal StringTableCellEditorViewModel(
        int row,
        int column,
        StringTableCellDraft cell,
        bool canEdit,
        bool hasConfigStringFooter,
        IReadOnlyDictionary<int, string?>? configStringValues,
        string? configStringIndexValue,
        Action<int, int, string?> applyValue)
    {
        ArgumentNullException.ThrowIfNull(cell);
        _applyValue = applyValue
            ?? throw new ArgumentNullException(nameof(applyValue));
        Row = row;
        Column = column;
        Hash = cell.Hash;
        CanEdit = canEdit;
        _hasConfigStringFooter = hasConfigStringFooter;
        _configStringValues = configStringValues;
        _configStringIndexValue = configStringIndexValue;
        _isNull = cell.Value is null;
        _valueInput = cell.Value ?? string.Empty;
    }

    public int Row { get; }
    public int Column { get; }
    public int Hash { get; }
    public bool CanEdit { get; }
    public string CoordinateText => $"Row {Row}, column {Column}";
    public string HashText => $"0x{unchecked((uint)Hash):X8}";
    public string FooterText => !_hasConfigStringFooter
        ? HashText
        : Column switch
        {
            0 => IsNull
                ? "No configstring index"
                : GetConfigStringMeaning(ValueInput, _configStringValues),
            1 => GetBaselineFooterText(
                _configStringIndexValue,
                _configStringValues),
            _ => HashText
        };
    public string FooterToolTipText => !_hasConfigStringFooter || Column > 1
        ? "Preserved serialized hash"
        : $"{FooterText}{Environment.NewLine}Preserved serialized hash: {HashText}";
    public bool IsValueReadOnly => !CanEdit || IsNull;

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _valueInput, value))
                return;

            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(FooterToolTipText));
            if (IsNull)
                return;

            _applyValue(Row, Column, value);
        }
    }

    public bool IsNull
    {
        get => _isNull;
        set
        {
            if (!CanEdit || !SetProperty(ref _isNull, value))
                return;

            OnPropertyChanged(nameof(IsValueReadOnly));
            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(FooterToolTipText));
            _applyValue(Row, Column, value ? null : ValueInput);
        }
    }

    private static string GetConfigStringMeaning(
        string value,
        IReadOnlyDictionary<int, string?>? configStringValues)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int index) ||
            index is < 0 or > 4301)
        {
            return "Invalid configstring index";
        }

        return index switch
        {
            0 => "Server info",
            1 => "System info",
            2 => "CS_GAME_VERSION",
            3 => "CS_SERVERID",
            4 => "CS_MESSAGE",
            5 => "CS_SCORES1",
            6 => "CS_SCORES2",
            7 => "CS_CULLDIST",
            8 => "CS_SUNLIGHT",
            9 => "CS_SUNDIR",
            10 => "CS_FORCE_SUN_SHADOWS",
            11 => "CS_HALF_RES_PARTICLES",
            12 => "PS3 platform slot 12",
            13 => "CS_FOGVARS",
            14 => "CS_MOTD",
            15 => "CS_GAMEENDTIME",
            16 => "CS_MAPCENTER",
            17 => "CS_VOTE_TIME",
            18 => "CS_VOTE_STRING",
            19 => "CS_VOTE_YES",
            20 => "CS_VOTE_NO",
            21 => "CS_VOTE_MAPNAME",
            22 => "CS_VOTE_GAMETYPE",
            23 => "CS_MULTI_MAPWINNER",
            >= 24 and <= 223 => IndexedMeaning("CS_CODINFO", index - 24),
            >= 224 and <= 423 =>
                GetCodInfoValueMeaning(index, configStringValues),
            424 => "CS_ENEMY_CROSSHAIR",
            >= 425 and <= 496 =>
                IndexedMeaning("PS3 platform player data", index - 425),
            497 => "Session nonce",
            >= 498 and <= 529 =>
                IndexedMeaning("CS_USE_TRIG_STRINGS", index - 498),
            >= 530 and <= 1040 =>
                IndexedMeaning("CS_LOCALIZED_STRINGS", index - 530),
            1041 => "CS_AMBIENT",
            1042 => "CS_AMBIENT_AC130",
            >= 1043 and <= 1074 => IndexedMeaning("CS_RUMBLES", index - 1043),
            1075 => "CS_NORTHYAW",
            1076 => "CS_MINIMAP",
            1077 => "CS_MATERIAL_THERMALBODY",
            1078 => "CS_VISIONSET_NAKED",
            1079 => "CS_VISIONSET_NIGHT",
            1080 => "CS_VISIONSET_MISSILECAM",
            1081 => "CS_VISIONSET_THERMAL",
            1082 => "CS_VISIONSET_PAIN",
            1083 => "CS_NIGHTVISION",
            >= 1084 and <= 1086 =>
                IndexedMeaning("CS_LOC_SEL_MTLS", index - 1084),
            >= 1087 and <= 1598 => IndexedMeaning("CS_MODELS", index - 1087),
            >= 1599 and <= 1630 =>
                IndexedMeaning("CS_VEHICLE_DEFS", index - 1599),
            >= 1631 and <= 1886 =>
                IndexedMeaning("CS_SOUNDALIASES", index - 1631),
            >= 1887 and <= 2142 =>
                IndexedMeaning("CS_EFFECT_NAMES", index - 1887),
            >= 2143 and <= 2398 =>
                IndexedMeaning("CS_EFFECT_TAGS", index - 2143),
            >= 2399 and <= 2414 =>
                IndexedMeaning("CS_SHELLSHOCKS", index - 2399),
            >= 2415 and <= 2446 =>
                IndexedMeaning("CS_SCRIPT_MENUS", index - 2415),
            >= 2447 and <= 2702 =>
                IndexedMeaning("CS_SERVER_MATERIALS", index - 2447),
            >= 2703 and <= 2766 => IndexedMeaning("CS_TAGS", index - 2703),
            >= 2767 and <= 3965 =>
                IndexedMeaning("CS_WEAPONFILES", index - 2766),
            >= 3966 and <= 3973 =>
                IndexedMeaning("CS_STATUS_ICONS", index - 3966),
            >= 3974 and <= 3988 =>
                IndexedMeaning("CS_HEAD_ICONS", index - 3974),
            >= 3989 and <= 4003 =>
                IndexedMeaning("CS_MINIMAP_ICONS", index - 3989),
            >= 4004 and <= 4066 =>
                IndexedMeaning("CS_MP_ANIMS", index - 4004),
            >= 4067 and <= 4098 => IndexedMeaning("CS_TEAMFX", index - 4067),
            4099 => "CS_TIMESCALE",
            4100 => "CS_ITEMS",
            4101 => "CS_LEADERBOARDS",
            >= 4102 and <= 4301 =>
                IndexedMeaning("CS_WEAPONFILES", index - 2902),
            _ => "Unknown configstring index"
        };
    }

    private static string IndexedMeaning(string name, int index) =>
        $"{name}[{index}]";

    private static string GetCodInfoValueMeaning(
        int index,
        IReadOnlyDictionary<int, string?>? configStringValues)
    {
        string indexedMeaning = IndexedMeaning("CS_CODINFO_VALUE", index - 224);
        return configStringValues is not null &&
               configStringValues.TryGetValue(index - 200, out string? name) &&
               !string.IsNullOrWhiteSpace(name)
            ? $"{name} · {indexedMeaning}"
            : indexedMeaning;
    }

    private static string GetBaselineFooterText(
        string? indexValue,
        IReadOnlyDictionary<int, string?>? configStringValues)
    {
        return int.TryParse(
                   indexValue,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out int index) &&
               index is >= 224 and <= 423 &&
               configStringValues is not null &&
               configStringValues.TryGetValue(index - 200, out string? name) &&
               !string.IsNullOrWhiteSpace(name)
            ? $"Baseline for {name}"
            : "Baseline value";
    }
}

/// <summary>
/// Row-major StringTable editor. Cell values are mutable for target-owned
/// definitions except generated configstring baselines, while stored hashes
/// and table dimensions remain preserved.
/// </summary>
public sealed class StringTableEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState
{
    private readonly AssetEditorSession _editorSession;
    private readonly Action<int, int, string?> _stageCellValue;
    private readonly Dictionary<int, string?> _pendingOriginalValues = [];
    private StringTableDraft? _draft;
    private StringTableReadOnlySnapshot? _readOnlySnapshot;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private IReadOnlyList<StringTableColumnHeaderViewModel> _columns = [];
    private IReadOnlyList<StringTableRowEditorViewModel> _rows = [];
    private int? _readOnlyNullCellCount;

    public StringTableEditorViewModel(AssetEditorSession editorSession)
    {
        _editorSession = editorSession
            ?? throw new ArgumentNullException(nameof(editorSession));
        _stageCellValue = StageCellValue;
        if (editorSession.Entry.AssetType != XAssetType.StringTable)
        {
            throw new InvalidDataException(
                "The StringTable view model can host only StringTable editor sessions.");
        }

        switch (editorSession.Mode)
        {
            case WorkspaceAssetAccess.Editable:
                _draft = editorSession.OpenDraft<StringTableDraft>();
                _diagnostics = editorSession.Validation.Issues;
                _statusMessage = IsGeneratedConfigStringBaseline
                    ? "Generated PS3 configstring transport baseline. Edit the owning map, scripts, or assets; ordinary saves preserve this table."
                    : "Cell edits are staged until Apply. Stored hashes are preserved.";
                break;

            case WorkspaceAssetAccess.ReadOnly:
                try
                {
                    _readOnlySnapshot =
                        StringTableReadOnlySnapshot.CaptureResolvedProvider(
                            editorSession);
                    _statusMessage = IsGeneratedConfigStringBaseline
                        ? "Generated PS3 configstring transport baseline. Edit the owning map, scripts, or assets; ordinary saves preserve this table."
                        : "Detached read-only copy of the catalog-resolved provider.";
                }
                catch (InvalidDataException exception)
                {
                    _statusMessage = exception.Message;
                    _diagnostics =
                    [
                        new AssetValidationIssue(
                            "provider",
                            exception.Message,
                            AssetValidationSeverity.Error)
                    ];
                }
                break;

            case WorkspaceAssetAccess.ContentUnavailable:
                _statusMessage =
                    "StringTable content is unavailable because this reference has no resolved provider.";
                break;

            default:
                throw new InvalidDataException(
                    $"Unknown StringTable editor mode '{editorSession.Mode}'.");
        }

        RefreshTable();
    }

    public WorkspaceAssetAccess Mode => _editorSession.Mode;
    public bool IsGeneratedConfigStringBaseline =>
        _editorSession.Entry.IsGeneratedConfigStringBaseline;
    public bool IsEditable =>
        Mode == WorkspaceAssetAccess.Editable &&
        !IsGeneratedConfigStringBaseline;
    public bool CanApply =>
        IsEditable && _draft is not null && _pendingOriginalValues.Count != 0;
    public bool HasUnappliedChanges => CanApply;
    public bool CanRevert => IsEditable;
    public bool HasTable => _draft is not null || _readOnlySnapshot is not null;
    public string OriginalName =>
        _draft?.Name
        ?? _readOnlySnapshot?.Name
        ?? _editorSession.Entry.OriginalName
        ?? string.Empty;
    public int RowCount => _draft?.RowCount ?? _readOnlySnapshot?.RowCount ?? 0;
    public int ColumnCount =>
        _draft?.ColumnCount ?? _readOnlySnapshot?.ColumnCount ?? 0;
    public int CellCount =>
        _draft?.Cells.Count ?? _readOnlySnapshot?.Cells.Count ?? 0;
    public string DimensionText =>
        $"{RowCount:N0} rows × {ColumnCount:N0} columns · {CellCount:N0} cells";
    public string ModeText =>
        IsGeneratedConfigStringBaseline &&
        Mode != WorkspaceAssetAccess.ContentUnavailable
        ? "GENERATED - READ ONLY"
        : Mode switch
        {
            WorkspaceAssetAccess.Editable => "EDITABLE TARGET DEFINITION",
            WorkspaceAssetAccess.ReadOnly => "READ-ONLY RESOLVED PROVIDER",
            WorkspaceAssetAccess.ContentUnavailable => "CONTENT UNAVAILABLE",
            _ => throw new InvalidDataException(
                $"Unknown StringTable editor mode '{Mode}'.")
        };

    public IReadOnlyList<StringTableColumnHeaderViewModel> Columns
    {
        get => _columns;
        private set => SetProperty(ref _columns, value);
    }

    public IReadOnlyList<StringTableRowEditorViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public IReadOnlyList<AssetValidationIssue> Diagnostics
    {
        get => _diagnostics;
        private set
        {
            _diagnostics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsSummary));
        }
    }

    public bool HasDiagnostics => Diagnostics.Count != 0;

    public string DiagnosticsSummary => string.Join(
        Environment.NewLine,
        Diagnostics.Select(issue =>
            $"{issue.Severity}: {issue.FieldPath} — {issue.Message}"));

    public string PropertySectionName => "StringTable";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Rows", RowCount.ToString("N0")),
        new("Columns", ColumnCount.ToString("N0")),
        new("Cells", CellCount.ToString("N0")),
        new("Null values", NullCellCount.ToString("N0")),
        new("Hashes", "Preserved source values")
    ];

    public void ApplyChanges()
    {
        if (!CanApply || _draft is null)
        {
            StatusMessage = IsGeneratedConfigStringBaseline
                ? "Generated configstring baselines cannot be edited directly; edit the owning map, scripts, or assets instead."
                : IsEditable
                    ? "There are no staged StringTable changes to apply."
                    : "This StringTable is read-only or its content is unavailable.";
            return;
        }

        int columnCount = _draft.ColumnCount;
        var changes = _pendingOriginalValues.Keys
            .Order()
            .Select(index => (
                Row: index / columnCount,
                Column: index % columnCount,
                Value: _draft.Cells[index].Value))
            .ToArray();

        try
        {
            _draft = _editorSession.ApplyAndRead<StringTableDraft>(
                currentDraft =>
                {
                    foreach (var change in changes)
                    {
                        currentDraft.SetCellValue(
                            change.Row,
                            change.Column,
                            change.Value);
                    }
                },
                out _);

            _pendingOriginalValues.Clear();
            Diagnostics = _editorSession.Validation.Issues;
            StatusMessage = changes.Length == 1
                ? "Applied 1 cell change; its stored hash was preserved."
                : $"Applied {changes.Length:N0} cell changes; their stored hashes were preserved.";
            RefreshTable();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            StatusMessage = exception.Message;
        }
    }

    public void RevertDraft()
    {
        if (!CanRevert)
        {
            StatusMessage = IsGeneratedConfigStringBaseline
                ? "Generated configstring baselines cannot be reverted directly."
                : "Read-only StringTable content cannot be reverted because it has no target-owned draft.";
            return;
        }

        _ = _editorSession.Revert();
        _draft = _editorSession.ReadDraft<StringTableDraft>();
        _pendingOriginalValues.Clear();
        Diagnostics = _editorSession.Validation.Issues;
        StatusMessage =
            "Reverted the detached StringTable draft to its authored baseline.";
        RefreshTable();
    }

    private void StageCellValue(int row, int column, string? value)
    {
        if (!IsEditable || _draft is null)
        {
            StatusMessage = IsGeneratedConfigStringBaseline
                ? "Generated configstring baselines cannot be edited directly; edit the owning map, scripts, or assets instead."
                : "This StringTable is read-only or its content is unavailable.";
            return;
        }

        try
        {
            int index = CheckedCellIndex(_draft, row, column);
            string? previousValue = _draft.Cells[index].Value;
            if (string.Equals(previousValue, value, StringComparison.Ordinal))
                return;

            bool hadPendingChanges = _pendingOriginalValues.Count != 0;
            bool wasPending = _pendingOriginalValues.TryGetValue(
                index,
                out string? originalValue);
            if (!wasPending)
                originalValue = previousValue;

            _draft.SetCellValue(row, column, value);
            if (string.Equals(originalValue, value, StringComparison.Ordinal))
            {
                _pendingOriginalValues.Remove(index);
            }
            else if (!wasPending)
            {
                _pendingOriginalValues.Add(index, originalValue);
            }

            StatusMessage = _pendingOriginalValues.ContainsKey(index)
                ? $"Staged row {row}, column {column}."
                : $"Restored row {row}, column {column} to its applied value.";
            if (hadPendingChanges != (_pendingOriginalValues.Count != 0))
                NotifyStagingStateChanged();
            if ((previousValue is null) != (value is null))
                OnPropertyChanged(nameof(EditorProperties));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RefreshTable()
    {
        IReadOnlyList<StringTableCellDraft> cells = CurrentCells();
        _readOnlyNullCellCount = null;
        int expectedCellCount;
        try
        {
            expectedCellCount = checked(RowCount * ColumnCount);
        }
        catch (OverflowException)
        {
            expectedCellCount = -1;
        }

        if (RowCount < 0 ||
            ColumnCount < 0 ||
            expectedCellCount < 0 ||
            expectedCellCount != cells.Count)
        {
            StatusMessage =
                "StringTable dimensions do not match the available row-major cells.";
            Columns = [];
            Rows = [];
            return;
        }

        Columns = Array.AsReadOnly(
            Enumerable.Range(0, ColumnCount)
                .Select(column => new StringTableColumnHeaderViewModel(
                    column,
                    column.ToString()))
                .ToArray());

        bool hasConfigStringFooters = IsGeneratedConfigStringBaseline;
        IReadOnlyDictionary<int, string?>? configStringValues =
            hasConfigStringFooters
                ? CreateConfigStringValues(cells)
                : null;
        var rows = new StringTableRowEditorViewModel[RowCount];
        for (int row = 0; row < RowCount; row++)
        {
            rows[row] = new StringTableRowEditorViewModel(
                row,
                ColumnCount,
                cells,
                IsEditable,
                hasConfigStringFooters,
                configStringValues,
                _stageCellValue);
        }

        Rows = Array.AsReadOnly(rows);
        OnPropertyChanged(nameof(OriginalName));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(ColumnCount));
        OnPropertyChanged(nameof(CellCount));
        OnPropertyChanged(nameof(DimensionText));
        OnPropertyChanged(nameof(HasTable));
        NotifyStagingStateChanged();
        OnPropertyChanged(nameof(EditorProperties));
    }

    private IReadOnlyDictionary<int, string?> CreateConfigStringValues(
        IReadOnlyList<StringTableCellDraft> cells)
    {
        var values = new Dictionary<int, string?>();
        if (ColumnCount < 2)
            return values;

        for (int row = 0; row < RowCount; row++)
        {
            int offset = checked(row * ColumnCount);
            if (int.TryParse(
                    cells[offset].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int index))
            {
                if (!values.TryAdd(index, cells[offset + 1].Value))
                    values[index] = null;
            }
        }

        return values;
    }

    private void NotifyStagingStateChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(HasUnappliedChanges));
    }

    private static int CheckedCellIndex(
        StringTableDraft draft,
        int row,
        int column)
    {
        if ((uint)row >= (uint)draft.RowCount ||
            (uint)column >= (uint)draft.ColumnCount)
        {
            throw new ArgumentOutOfRangeException(
                $"Cell ({row}, {column}) is outside this {draft.RowCount}×{draft.ColumnCount} StringTable.");
        }

        return checked(row * draft.ColumnCount + column);
    }

    private IReadOnlyList<StringTableCellDraft> CurrentCells() =>
        _draft?.Cells
        ?? _readOnlySnapshot?.Cells
        ?? [];

    private int NullCellCount =>
        _draft?.NullCellCount
        ?? (_readOnlyNullCellCount ??=
            _readOnlySnapshot?.Cells.Count(cell => cell.Value is null) ?? 0);
}
