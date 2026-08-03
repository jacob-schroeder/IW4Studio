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
    private readonly Action<int, int, string?> _applyValue;
    private IReadOnlyList<StringTableCellEditorViewModel>? _cells;

    internal StringTableRowEditorViewModel(
        int row,
        int columnCount,
        IReadOnlyList<StringTableCellDraft> sourceCells,
        bool canEdit,
        Action<int, int, string?> applyValue)
    {
        ArgumentNullException.ThrowIfNull(sourceCells);
        ArgumentNullException.ThrowIfNull(applyValue);
        Row = row;
        Label = row.ToString();
        _columnCount = columnCount;
        _sourceCells = sourceCells;
        _canEdit = canEdit;
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
        for (int column = 0; column < cells.Length; column++)
        {
            cells[column] = new StringTableCellEditorViewModel(
                Row,
                column,
                _sourceCells[rowOffset + column],
                _canEdit,
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
    private string _valueInput;
    private bool _isNull;

    internal StringTableCellEditorViewModel(
        int row,
        int column,
        StringTableCellDraft cell,
        bool canEdit,
        Action<int, int, string?> applyValue)
    {
        ArgumentNullException.ThrowIfNull(cell);
        _applyValue = applyValue
            ?? throw new ArgumentNullException(nameof(applyValue));
        Row = row;
        Column = column;
        Hash = cell.Hash;
        CanEdit = canEdit;
        _isNull = cell.Value is null;
        _valueInput = cell.Value ?? string.Empty;
    }

    public int Row { get; }
    public int Column { get; }
    public int Hash { get; }
    public bool CanEdit { get; }
    public string CoordinateText => $"Row {Row}, column {Column}";
    public string HashText => $"0x{unchecked((uint)Hash):X8}";
    public bool IsValueReadOnly => !CanEdit || IsNull;

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _valueInput, value) || IsNull)
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
            _applyValue(Row, Column, value ? null : ValueInput);
        }
    }
}

/// <summary>
/// Row-major StringTable editor. Cell values are mutable for target-owned
/// definitions, while stored hashes and table dimensions remain preserved.
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
            case AssetEditorMode.Editable:
                _draft = editorSession.OpenDraft<StringTableDraft>();
                _diagnostics = editorSession.Validation.Issues;
                _statusMessage =
                    "Cell edits are staged until Apply. Stored hashes are preserved.";
                break;

            case AssetEditorMode.ReadOnly:
                try
                {
                    _readOnlySnapshot =
                        StringTableReadOnlySnapshot.CaptureResolvedProvider(
                            editorSession);
                    _statusMessage =
                        "Detached read-only copy of the catalog-resolved provider.";
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

            case AssetEditorMode.ContentUnavailable:
                _statusMessage =
                    "StringTable content is unavailable because this reference has no resolved provider.";
                break;

            default:
                throw new InvalidDataException(
                    $"Unknown StringTable editor mode '{editorSession.Mode}'.");
        }

        RefreshTable();
    }

    public AssetEditorMode Mode => _editorSession.Mode;
    public bool IsEditable => Mode == AssetEditorMode.Editable;
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
    public string ModeText => Mode switch
    {
        AssetEditorMode.Editable => "EDITABLE TARGET DEFINITION",
        AssetEditorMode.ReadOnly => "READ-ONLY RESOLVED PROVIDER",
        AssetEditorMode.ContentUnavailable => "CONTENT UNAVAILABLE",
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
            StatusMessage = IsEditable
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
            StatusMessage =
                "Read-only StringTable content cannot be reverted because it has no target-owned draft.";
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
            StatusMessage =
                "This StringTable is read-only or its content is unavailable.";
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

        var rows = new StringTableRowEditorViewModel[RowCount];
        for (int row = 0; row < RowCount; row++)
        {
            rows[row] = new StringTableRowEditorViewModel(
                row,
                ColumnCount,
                cells,
                IsEditable,
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
