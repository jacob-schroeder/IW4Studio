using IW4.Assets.Assets.StringTable;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents;

public sealed record StringTableCellDraft(string? Value, int Hash) : IStringTableCellBuildData;

public sealed class StringTableAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal StringTableAuthoredSnapshot(string? name, int rowCount, int columnCount, IEnumerable<StringTableCellDraft> cells)
    {
        Name = name;
        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = Array.AsReadOnly(cells.ToArray());
    }

    public string? Name { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<StringTableCellDraft> Cells { get; }
    public XAssetType AssetType => XAssetType.StringTable;

    internal static StringTableAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        if (source.SerializedType != XAssetType.StringTable || source.State != TargetZoneRowSourceState.Definition ||
            source.AuthoredDefinition?.SemanticSnapshot is not StringTableAuthoredSnapshot snapshot)
        {
            throw new InvalidDataException("StringTable editing requires a capture-time detached semantic snapshot; source-fragment replay is not an authoring input.");
        }
        return snapshot;
    }

    internal static StringTableAuthoredSnapshot FromLoaded(StringTableAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new StringTableAuthoredSnapshot(
            asset.Name,
            asset.RowCount,
            asset.ColumnCount,
            asset.Cells.Select(cell => new StringTableCellDraft(cell.String, cell.Hash)));
    }
}

public sealed class StringTableDraft
{
    private readonly List<StringTableCellDraft> _cells;
    private readonly IReadOnlyList<StringTableCellDraft> _readOnlyCells;

    internal StringTableDraft(StringTableAuthoredSnapshot snapshot)
        : this(
            snapshot.Name,
            snapshot.RowCount,
            snapshot.ColumnCount,
            snapshot.Cells,
            nullCellCount: null)
    {
    }

    private StringTableDraft(
        string? name,
        int rowCount,
        int columnCount,
        IEnumerable<StringTableCellDraft> cells,
        int? nullCellCount)
    {
        Name = name;
        RowCount = rowCount;
        ColumnCount = columnCount;
        _cells = cells.ToList();
        _readOnlyCells = _cells.AsReadOnly();
        NullCellCount = nullCellCount
            ?? _cells.Count(cell => cell.Value is null);
    }

    public string? Name { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<StringTableCellDraft> Cells => _readOnlyCells;
    public int NullCellCount { get; private set; }

    public void SetCellValue(int row, int column, string? value)
    {
        int index = CheckedIndex(row, column);
        bool wasNull = _cells[index].Value is null;
        _cells[index] = _cells[index] with { Value = value };
        if (wasNull != (value is null))
            NullCellCount += value is null ? 1 : -1;
    }
    public void SetCellHash(int row, int column, int hash)
    {
        int index = CheckedIndex(row, column);
        _cells[index] = _cells[index] with { Hash = hash };
    }
    internal StringTableDraft Clone() =>
        new(Name, RowCount, ColumnCount, _cells, NullCellCount);
    private int CheckedIndex(int row, int column)
    {
        if ((uint)row >= (uint)RowCount || (uint)column >= (uint)ColumnCount)
            throw new ArgumentOutOfRangeException($"Cell ({row}, {column}) is outside this {RowCount}×{ColumnCount} StringTable.");
        return checked(row * ColumnCount + column);
    }
}

public sealed class StringTableBuildData : IStringTableBuildData
{
    internal StringTableBuildData(StringTableDraft draft)
    {
        Name = draft.Name;
        RowCount = draft.RowCount;
        ColumnCount = draft.ColumnCount;
        Cells = Array.AsReadOnly(draft.Cells.Select(cell => new StringTableCellDraft(cell.Value, cell.Hash)).ToArray());
    }
    public XAssetType AssetType => XAssetType.StringTable;
    public string? Name { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<IStringTableCellBuildData> Cells { get; }
}

/// <summary>
/// Detached read-only copy of a currently resolved StringTable provider. It
/// retains only authored scalar values and never exposes the runtime asset.
/// </summary>
public sealed class StringTableReadOnlySnapshot
{
    private StringTableReadOnlySnapshot(
        string? name,
        int rowCount,
        int columnCount,
        IEnumerable<StringTableCellDraft> cells)
    {
        Name = name;
        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = Array.AsReadOnly(cells.ToArray());
    }

    public string? Name { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<StringTableCellDraft> Cells { get; }

    public static StringTableReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        WorkspaceAssetCatalogEntry entry = editorSession.Entry;
        WorkspaceAssetResolvedProvider provider = entry.ResolvedProvider
            ?? throw new InvalidDataException(
                "StringTable read-only viewing requires a catalog-resolved full-definition provider.");
        XAssetProviderContribution contribution = editorSession.Workspace.Runtime.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .SingleOrDefault(candidate => candidate.Id == provider.ProviderId)
            ?? throw new InvalidDataException(
                "The catalog-resolved StringTable provider is no longer present in this workspace runtime.");
        if (contribution.AssetType != XAssetType.StringTable ||
            contribution.IsReferencePlaceholder ||
            contribution.Owner != provider.Zone.Handle ||
            contribution.Asset is not StringTableAsset stringTable)
        {
            throw new InvalidDataException(
                "The catalog-resolved provider no longer matches a readable StringTable full definition.");
        }

        return new StringTableReadOnlySnapshot(
            stringTable.Name ?? contribution.Name,
            stringTable.RowCount,
            stringTable.ColumnCount,
            stringTable.Cells.Select(cell =>
                new StringTableCellDraft(cell.String, cell.Hash)));
    }
}

public sealed class StringTableAuthoringAdapter : AssetAuthoringAdapter<StringTableAuthoredSnapshot, StringTableDraft, StringTableBuildData>
{
    public override XAssetType AssetType => XAssetType.StringTable;
    public override StringTableAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => StringTableAuthoredSnapshot.Import(source);
    public override StringTableDraft CreateDraft(StringTableAuthoredSnapshot authoredSnapshot) => new(authoredSnapshot);
    public override StringTableDraft CloneDraft(StringTableDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(StringTableDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<AssetValidationIssue>();
        LocalizeAuthoringAdapter.ValidateString(draft.Name, "name", issues);
        IReadOnlyList<StringTableCellDraft> cells = draft.Cells;
        int expected = -1;
        try { expected = checked(draft.RowCount * draft.ColumnCount); } catch (OverflowException) { }
        if (draft.RowCount < 0 || draft.ColumnCount < 0 || expected < 0 || expected > 0x100000 || cells.Count != expected)
            issues.Add(new AssetValidationIssue("dimensions", "Row/column dimensions must remain within the loader bounds and match the ordered cell count.", AssetValidationSeverity.Error));
        for (int index = 0; index < cells.Count; index++)
        {
            string? value = cells[index].Value;
            if (!LocalizeAuthoringAdapter.IsStringValid(value))
                LocalizeAuthoringAdapter.ValidateString(value, $"cells[{index}].value", issues);
        }
        return Array.AsReadOnly(issues.ToArray());
    }
    public override bool SemanticallyEquals(StringTableDraft baseline, StringTableDraft current) =>
        baseline.Name == current.Name && baseline.RowCount == current.RowCount && baseline.ColumnCount == current.ColumnCount && baseline.Cells.SequenceEqual(current.Cells);
    public override StringTableBuildData ExportBuildData(StringTableDraft draft)
    {
        if (ValidateDraft(draft).Any(issue => issue.Severity == AssetValidationSeverity.Error))
            throw new InvalidOperationException("StringTable draft has validation errors and cannot produce build data.");
        return new StringTableBuildData(draft);
    }
}
