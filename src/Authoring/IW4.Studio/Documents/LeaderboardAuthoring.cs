using IW4.Game.Assets.Leaderboard;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>Detached, ordered LeaderboardDef columns and their native fields.</summary>
public sealed class LeaderboardDraft
{
    private readonly int _offset;
    private readonly XRuntimeAddress? _runtimeAddress;
    private readonly XPointer<string> _namePointer;
    private readonly XPointer<LbColumnDef[]> _columnsPointer;
    private List<LbColumnDef> _rows;

    internal LeaderboardDraft(LeaderboardDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Name = asset.Name;
        _offset = asset.Offset;
        _runtimeAddress = asset.RuntimeAddress;
        _namePointer = asset.NamePointer;
        _columnsPointer = asset.ColumnsPointer;
        BoardId = asset.Id;
        XpColumnId = asset.XpColumnId;
        PrestigeColumnId = asset.PrestigeColumnId;
        _rows = asset.Columns.Select(Copy).ToList();
        if (asset.ColumnCount != _rows.Count)
            throw new InvalidDataException("Leaderboard column count does not match its row table.");
    }

    private LeaderboardDraft(LeaderboardDraft source)
    {
        Name = source.Name;
        _offset = source._offset;
        _runtimeAddress = source._runtimeAddress;
        _namePointer = source._namePointer;
        _columnsPointer = source._columnsPointer;
        BoardId = source.BoardId;
        XpColumnId = source.XpColumnId;
        PrestigeColumnId = source.PrestigeColumnId;
        _rows = source._rows.Select(Copy).ToList();
    }

    public string? Name { get; }
    public int BoardId { get; set; }
    public int XpColumnId { get; set; }
    public int PrestigeColumnId { get; set; }
    public IReadOnlyList<LbColumnDef> Rows => Array.AsReadOnly(_rows.Select(Copy).ToArray());

    public LeaderboardDraft Clone() => new(this);

    /// <summary>Adds a blank column with an unused signed ID and returns its row index.</summary>
    public int AddColumn()
    {
        var used = _rows.Select(row => row.Id).ToHashSet();
        if (XpColumnId != -1) used.Add(XpColumnId);
        if (PrestigeColumnId != -1) used.Add(PrestigeColumnId);
        int id = 0;
        while (used.Contains(id))
            id = checked(id + 1);
        _rows.Add(new LbColumnDef
        {
            Name = "New Column",
            Id = id,
            Pad0DTo0F = [0, 0, 0],
            Type = LbColType.Number,
            Aggregation = LbAggType.Last
        });
        return _rows.Count - 1;
    }

    /// <summary>Moves a row to its final display index without changing its ID.</summary>
    public void MoveColumn(int from, int to)
    {
        RequireIndex(from);
        RequireIndex(to);
        if (from == to)
            return;
        LbColumnDef row = _rows[from];
        _rows.RemoveAt(from);
        _rows.Insert(to, row);
    }

    /// <summary>Updates editable fields while retaining untouched native padding and text provenance.</summary>
    public void UpdateColumn(
        int index,
        int id,
        int propertyId,
        string? name,
        string? statName,
        byte hiddenRaw,
        LbColType type,
        int precision,
        LbAggType aggregation)
    {
        RequireIndex(index);
        if (name?.Contains('\0') == true || statName?.Contains('\0') == true)
            throw new ArgumentException("Column text cannot contain a null character.");
        LbColumnDef previous = _rows[index];
        if (id != previous.Id && _rows.Where((_, rowIndex) => rowIndex != index).Any(row => row.Id == id))
            throw new InvalidOperationException($"Column ID {id} is already used by another row.");
        if (id == -1 && id != previous.Id && (XpColumnId == previous.Id || PrestigeColumnId == previous.Id))
            throw new InvalidOperationException("Choose another column ID or clear its XP or prestige role first. The game uses -1 for an unassigned role.");

        _rows[index] = new LbColumnDef
        {
            Name = name,
            NamePointer = previous.Name == name ? previous.NamePointer : default,
            Id = id,
            PropertyId = propertyId,
            HiddenRaw = hiddenRaw,
            Pad0DTo0F = previous.Pad0DTo0F.ToArray(),
            StatName = statName,
            StatNamePointer = previous.StatName == statName ? previous.StatNamePointer : default,
            Type = type,
            Precision = precision,
            Aggregation = aggregation
        };
        // A role names a column ID, not a display position. A duplicate imported ID
        // has ambiguous ownership, so only retarget an unambiguous role.
        if (id != previous.Id && previous.Id != -1 &&
            !_rows.Where((_, rowIndex) => rowIndex != index).Any(row => row.Id == previous.Id))
        {
            if (XpColumnId == previous.Id) XpColumnId = id;
            if (PrestigeColumnId == previous.Id) PrestigeColumnId = id;
        }
    }

    public bool Matches(LeaderboardDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Name == other.Name && BoardId == other.BoardId &&
            XpColumnId == other.XpColumnId && PrestigeColumnId == other.PrestigeColumnId &&
            _rows.Count == other._rows.Count &&
            _rows.Zip(other._rows).All(pair => SameRow(pair.First, pair.Second));
    }

    /// <summary>Replaces the draft only when the caller's baseline is current.</summary>
    public void ApplyEdits(LeaderboardDraft before, LeaderboardDraft after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (!Matches(before))
            throw new InvalidOperationException("This leaderboard changed since the edit began; refresh it before applying edits.");
        if (Name != after.Name)
            throw new InvalidOperationException("A leaderboard edit cannot change its asset identity.");
        List<LbColumnDef> replacement = after._rows.Select(Copy).ToList();
        BoardId = after.BoardId;
        XpColumnId = after.XpColumnId;
        PrestigeColumnId = after.PrestigeColumnId;
        _rows = replacement;
    }

    internal LeaderboardDefAsset ToAsset() => new()
    {
        Offset = _offset,
        RuntimeAddress = _runtimeAddress,
        Name = Name,
        NamePointer = _namePointer,
        Id = BoardId,
        ColumnCount = _rows.Count,
        XpColumnId = XpColumnId,
        PrestigeColumnId = PrestigeColumnId,
        ColumnsPointer = _columnsPointer,
        Columns = Array.AsReadOnly(_rows.Select(Copy).ToArray())
    };

    private void RequireIndex(int index)
    {
        if ((uint)index >= (uint)_rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static LbColumnDef Copy(LbColumnDef row) => new()
    {
        NamePointer = row.NamePointer,
        Name = row.Name,
        Id = row.Id,
        PropertyId = row.PropertyId,
        HiddenRaw = row.HiddenRaw,
        Pad0DTo0F = row.Pad0DTo0F.ToArray(),
        StatNamePointer = row.StatNamePointer,
        StatName = row.StatName,
        Type = row.Type,
        Precision = row.Precision,
        Aggregation = row.Aggregation
    };

    private static bool SameRow(LbColumnDef left, LbColumnDef right) =>
        left.Name == right.Name && left.Id == right.Id &&
        left.PropertyId == right.PropertyId && left.HiddenRaw == right.HiddenRaw &&
        left.Pad0DTo0F.AsSpan().SequenceEqual(right.Pad0DTo0F) &&
        left.StatName == right.StatName && left.Type == right.Type &&
        left.Precision == right.Precision && left.Aggregation == right.Aggregation;
}

internal sealed class LeaderboardAdapter : AssetAuthoringAdapter<LeaderboardDefAsset, LeaderboardDraft>
{
    public override XAssetType AssetType => XAssetType.LeaderboardDef;
    public override LeaderboardDraft CreateDraft(LeaderboardDefAsset asset) => new(asset);
    public override LeaderboardDraft CloneDraft(LeaderboardDraft draft) => draft.Clone();
    public override LeaderboardDefAsset CreateDefinition(LeaderboardDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(LeaderboardDraft left, LeaderboardDraft right) => left.Matches(right);
}
