using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed record StringTableColumnHeaderViewModel(
    int Column,
    string Label);

public sealed class StringTableRowEditorViewModel
{
    internal StringTableRowEditorViewModel(
        int row,
        IReadOnlyList<StringTableCellEditorViewModel> cells)
    {
        Row = row;
        Label = row.ToString();
        Cells = cells;
    }

    public int Row { get; }
    public string Label { get; }
    public IReadOnlyList<StringTableCellEditorViewModel> Cells { get; }
}

/// <summary>
/// One row-major cell projection. Editing the value updates only the detached
/// draft; the serialized hash remains an explicit, preserved source value.
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
    : ObservableObject, IAssetEditorProperties, IAssetEditorDiagnostics
{
    private readonly AssetEditorSession _editorSession;
    private StringTableDraft? _draft;
    private StringTableReadOnlySnapshot? _readOnlySnapshot;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private IReadOnlyList<StringTableColumnHeaderViewModel> _columns = [];
    private IReadOnlyList<StringTableRowEditorViewModel> _rows = [];

    public StringTableEditorViewModel(AssetEditorSession editorSession)
    {
        _editorSession = editorSession
            ?? throw new ArgumentNullException(nameof(editorSession));
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
                    "Changes are applied to the detached target-owned draft. Stored hashes are preserved.";
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
        new("Null values", CurrentCells().Count(cell => cell.Value is null).ToString("N0")),
        new("Hashes", "Preserved source values")
    ];

    public void SetCell(int row, int column, string? value)
    {
        ApplyCellValue(row, column, value);
        RefreshTable();
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
        Diagnostics = _editorSession.Validation.Issues;
        StatusMessage =
            "Reverted the detached StringTable draft to its authored baseline.";
        RefreshTable();
    }

    private void ApplyCellValue(int row, int column, string? value)
    {
        if (!IsEditable || _draft is null)
        {
            StatusMessage =
                "This StringTable is read-only or its content is unavailable.";
            return;
        }

        try
        {
            _editorSession.Apply<StringTableDraft>(
                draft => draft.SetCellValue(row, column, value));
            _draft = _editorSession.ReadDraft<StringTableDraft>();
            Diagnostics = _editorSession.Validation.Issues;
            StatusMessage =
                $"Updated row {row}, column {column}; its stored hash was preserved.";
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
        Columns = Array.AsReadOnly(
            Enumerable.Range(0, ColumnCount)
                .Select(column => new StringTableColumnHeaderViewModel(
                    column,
                    column.ToString()))
                .ToArray());

        var rows = new StringTableRowEditorViewModel[RowCount];
        for (int row = 0; row < RowCount; row++)
        {
            var rowCells = new StringTableCellEditorViewModel[ColumnCount];
            for (int column = 0; column < ColumnCount; column++)
            {
                int index = checked(row * ColumnCount + column);
                if ((uint)index >= (uint)cells.Count)
                {
                    StatusMessage =
                        "StringTable dimensions do not match the available row-major cells.";
                    Rows = [];
                    return;
                }

                rowCells[column] = new StringTableCellEditorViewModel(
                    row,
                    column,
                    cells[index],
                    IsEditable,
                    ApplyCellValue);
            }

            rows[row] = new StringTableRowEditorViewModel(
                row,
                Array.AsReadOnly(rowCells));
        }

        Rows = Array.AsReadOnly(rows);
        OnPropertyChanged(nameof(OriginalName));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(ColumnCount));
        OnPropertyChanged(nameof(CellCount));
        OnPropertyChanged(nameof(DimensionText));
        OnPropertyChanged(nameof(HasTable));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private IReadOnlyList<StringTableCellDraft> CurrentCells() =>
        _draft?.Cells
        ?? _readOnlySnapshot?.Cells
        ?? [];
}
