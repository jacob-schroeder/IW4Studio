using IW4.Game.Assets.Leaderboard;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Leaderboard;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

internal sealed class LeaderboardEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private readonly List<(LeaderboardDraft Draft, int Selection)> _undo = [];
    private LeaderboardDraft _baseline;
    private LeaderboardDraft _working;
    private string? _message;

    internal LeaderboardEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<LeaderboardDraft>();
        _working = _baseline.Clone();
        Columns = _working.Rows;
        SelectedIndex = Enumerable.Range(0, Columns.Count).FirstOrDefault(
            i => !Columns[i].Hidden && !string.IsNullOrEmpty(Columns[i].StatName), Columns.Count > 0 ? 0 : -1);
    }

    internal event Action? Changed;
    internal string Heading => _working.Name ?? _session.Entry.OriginalName ?? "Leaderboard";
    internal string Status => _message ?? (!IsEditable ? "Read only" : HasUnappliedChanges ? "Unapplied changes" :
        _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");
    public bool HasUnappliedChanges => !_working.Matches(_baseline);
    internal bool IsEditable => _session.CanEdit;
    internal bool CanUndo => IsEditable && _undo.Count > 0;
    internal bool CanApply => IsEditable && HasUnappliedChanges;
    internal IReadOnlyList<LbColumnDef> Columns { get; private set; }
    internal int SelectedIndex { get; private set; }
    internal LbColumnDef? SelectedColumn => SelectedIndex < 0 ? null : Columns[SelectedIndex];
    internal int BoardId => _working.BoardId;
    internal int XpColumnId => _working.XpColumnId;
    internal int PrestigeColumnId => _working.PrestigeColumnId;
    internal IReadOnlyList<LeaderboardStatChoice> SourceChoices => LeaderboardSamples.Sources(SelectedColumn?.StatName);
    internal IReadOnlyList<LeaderboardRoleChoice> RoleChoices
    {
        get
        {
            var choices = new List<LeaderboardRoleChoice> { new(-1, "None") };
            var ids = new HashSet<int> { -1 };
            foreach (var col in Columns)
                if (ids.Add(col.Id)) choices.Add(new(col.Id, $"{col.Name ?? "Unnamed"} ({col.Id})"));
            foreach (int id in new[] { XpColumnId, PrestigeColumnId })
                if (ids.Add(id)) choices.Add(new(id, $"Unmatched ID {id}"));
            return choices;
        }
    }

    internal bool ShowPlayerLabels => !Columns.Any(c => !c.Hidden && LeaderboardSamples.IsPlayerName(c));
    internal static IReadOnlyList<string> Players => LeaderboardSamples.Players;
    internal string SampleValue(int columnIndex, int playerIndex) => LeaderboardSamples.Value(Columns[columnIndex], playerIndex);
    internal string SampleHistory => SelectedColumn is { } c ? LeaderboardSamples.History(c) : "";
    internal string SampleResult => SelectedColumn is { } c ? LeaderboardSamples.Value(c, 0) : "";

    internal void Select(int index)
    {
        if ((uint)index >= (uint)Columns.Count || index == SelectedIndex) return;
        SelectedIndex = index;
        _message = null;
        Changed?.Invoke();
    }

    internal void MoveColumn(int from, int toFinalIndex)
    {
        if (from == toFinalIndex || (uint)from >= (uint)Columns.Count || (uint)toFinalIndex >= (uint)Columns.Count) return;
        Edit(draft => { draft.MoveColumn(from, toFinalIndex); SelectedIndex = toFinalIndex; });
    }

    internal void AddColumn() => Edit(draft => SelectedIndex = draft.AddColumn());
    internal void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            if (IsEditable) { _message = "Give the column a name."; Changed?.Invoke(); }
            return;
        }
        ChangeColumn(name: title);
    }
    internal void SetVisible(bool visible)
    {
        if (SelectedColumn is { } c && c.Hidden == visible) ChangeColumn(hidden: visible ? (byte)0 : (byte)1);
    }
    internal void SetSourceStat(string? key)
    {
        if (string.Equals(key ?? "", SelectedColumn?.StatName ?? "", StringComparison.Ordinal)) return;
        if (SourceChoices.Any(s => s.Key == key)) ChangeColumn(replaceSource: true, source: key);
    }
    internal void SetType(LbColType type) => ChangeColumn(type: type);
    internal void SetAggregation(LbAggType aggregation) => ChangeColumn(aggregation: aggregation);
    internal void SetPrecision(int precision) => ChangeColumn(precision: precision);
    internal void SetColumnId(int id) => ChangeColumn(id: id);
    internal void SetPropertyId(int id) => ChangeColumn(property: id);
    internal void SetBoardId(int id) => Edit(draft => draft.BoardId = id);
    internal void SetXpColumnId(int id) => Edit(draft => draft.XpColumnId = id);
    internal void SetPrestigeColumnId(int id) => Edit(draft => draft.PrestigeColumnId = id);

    private void ChangeColumn(string? name = null, bool replaceSource = false, string? source = null,
        int? id = null, int? property = null, byte? hidden = null, LbColType? type = null,
        int? precision = null, LbAggType? aggregation = null)
    {
        if (SelectedColumn is not { } c) return;
        Edit(draft => draft.UpdateColumn(SelectedIndex, id ?? c.Id, property ?? c.PropertyId, name ?? c.Name,
            replaceSource ? source : c.StatName, hidden ?? c.HiddenRaw, type ?? c.Type,
            precision ?? c.Precision, aggregation ?? c.Aggregation));
    }

    private void Edit(Action<LeaderboardDraft> change)
    {
        if (!IsEditable) return;
        var candidate = _working.Clone();
        int previousSelection = SelectedIndex;
        try
        {
            change(candidate);
            _message = null;
            if (!_working.Matches(candidate))
            {
                _undo.Add((_working, previousSelection));
                if (_undo.Count > 32) _undo.RemoveAt(0);
                _working = candidate;
                Columns = candidate.Rows;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            SelectedIndex = previousSelection;
            _message = ex.Message;
        }
        Notify();
    }

    internal void Undo()
    {
        if (!CanUndo) return;
        (_working, SelectedIndex) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Columns = _working.Rows;
        _message = null;
        RefreshWorkspace();
    }

    internal void Apply()
    {
        if (!CanApply) return;
        try
        {
            var current = _session.ApplyAndRead<LeaderboardDraft>(draft => draft.ApplyEdits(_baseline, _working));
            if (_session.Validation.HasErrors)
                _message = string.Join(" ", _session.Validation.Issues.Select(issue => issue.Message));
            else
                Load(current);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            _message = ex.Message;
        }
        Notify();
    }

    internal void RefreshWorkspace()
    {
        if (!HasUnappliedChanges)
        {
            var current = _session.OpenDraft<LeaderboardDraft>();
            if (!_baseline.Matches(current)) Load(current);
        }
        Notify();
    }

    private void Load(LeaderboardDraft current)
    {
        _baseline = current;
        _working = current.Clone();
        Columns = _working.Rows;
        SelectedIndex = Columns.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, Columns.Count - 1);
        _undo.Clear();
        _message = null;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        Changed?.Invoke();
    }
}
