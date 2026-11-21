using IW4.Game.Assets.Sound;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.SndCurve;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class SndCurveEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private readonly List<EditState> _undo = [], _redo = [];
    private SndCurveDraft _baseline, _working;
    private EditState? _gesture;
    private string? _message;
    private sealed record EditState(SndCurveDraft Curve, int Selection);

    public SndCurveEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<SndCurveDraft>();
        _working = _baseline.Clone();
        SelectedIndex = _working.KnotCount / 2;
        RefreshPoints();
    }

    public event Action? Changed;
    public string Name => _session.Entry.NormalizedName ?? _working.Filename ?? "Untitled curve";
    public string Heading
    {
        get
        {
            string name = Name[(Name.LastIndexOf('/') + 1)..].Replace('_', ' ');
            return name.Length == 0 ? Name : char.ToUpperInvariant(name[0]) + name[1..];
        }
    }
    public IReadOnlyList<SndCurveKnot> Points { get; private set; } = [];
    public int SelectedIndex { get; private set; }
    public float ListenerDistance { get; private set; } = .35f;
    public float ListenerVolume => HasPreview ? _working.VolumeAt(ListenerDistance) : 0;
    public bool HasPreview => _working.EditBlocker is null;
    public bool CanEdit => _session.CanEdit && HasPreview;
    public bool CanRecover => _session.CanEdit && !HasPreview;
    public bool IsInteriorPoint => SelectedIndex > 0 && SelectedIndex < Points.Count - 1;
    public bool CanAdd => CanEdit && _gesture is null && Points.Count < IW4.Game.Assets.Sound.SndCurve.MaxKnotCount;
    public bool CanRemove => CanEdit && _gesture is null && IsInteriorPoint;
    public bool HasUnappliedChanges => !_working.Matches(_baseline);
    public bool CanApply => CanEdit && _gesture is null && HasUnappliedChanges;
    public bool CanUndo => _gesture is null && _undo.Count > 0;
    public bool CanRedo => _gesture is null && _redo.Count > 0;
    public string Status => _message ?? _working.EditBlocker ?? (!_session.CanEdit ? "Read only" :
        HasUnappliedChanges ? "Unapplied changes" : _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");

    public void SelectPoint(int index)
    {
        if (index < 0 || index >= Points.Count || SelectedIndex == index || _gesture is not null) return;
        SelectedIndex = index;
        Notify();
    }

    public void ListenAt(double fraction)
    {
        if (!double.IsFinite(fraction)) return;
        float distance = (float)Math.Clamp(fraction, 0, 1);
        if (distance == ListenerDistance) return;
        ListenerDistance = distance;
        Changed?.Invoke();
    }

    public void AddPoint()
    {
        if (!CanAdd) return;
        int left = 0;
        for (int i = 1; i < Points.Count - 1; i++)
            if (Points[i + 1].X - Points[i].X > Points[left + 1].X - Points[left].X) left = i;
        Edit(() => SelectedIndex = _working.AddKnot((Points[left].X + Points[left + 1].X) * .5f));
    }

    public void RemovePoint()
    {
        if (!CanRemove) return;
        Edit(() => _working.RemoveKnot(SelectedIndex));
    }

    public void StartLinearFade()
    {
        if (CanRecover) Edit(_working.StartLinearFade);
    }

    public void SetPoint(float distance, float volume)
    {
        if (!CanEdit || _gesture is not null || SelectedIndex < 0) return;
        Edit(() => _working.MoveKnot(SelectedIndex, distance, volume));
    }

    public void HandleGraphEdit(SndCurveGraphEdit edit)
    {
        if (!CanEdit || edit.Index < 0 || edit.Index >= Points.Count) return;
        switch (edit.Phase)
        {
            case SndCurveGraphEditPhase.Begin:
                if (_gesture is not null) return;
                SelectedIndex = edit.Index;
                _gesture = Capture();
                _message = null;
                break;
            case SndCurveGraphEditPhase.Update when _gesture is not null:
                try { _working.MoveKnot(edit.Index, edit.Knot.X, edit.Knot.Y); _message = null; }
                catch (Exception e) when (IsEditError(e)) { _message = e.Message; }
                break;
            case SndCurveGraphEditPhase.Commit when _gesture is { } start:
                Record(start);
                _gesture = null;
                break;
            case SndCurveGraphEditPhase.Cancel when _gesture is { } start:
                _working = start.Curve;
                SelectedIndex = start.Selection;
                _gesture = null;
                _message = null;
                break;
            default: return;
        }
        RefreshPoints();
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    public void Apply()
    {
        if (!CanApply) return;
        try
        {
            _baseline = _session.ApplyAndRead<SndCurveDraft>(current => current.ApplyEdits(_baseline, _working));
            _working = _baseline.Clone();
            _undo.Clear();
            _redo.Clear();
            _message = null;
        }
        catch (Exception e) when (IsEditError(e)) { _message = e.Message; }
        RefreshPoints();
    }

    internal void RefreshSessionState() => Notify();

    private void Edit(Action change)
    {
        var previous = Capture();
        try { change(); Record(previous); _message = null; }
        catch (Exception e) when (IsEditError(e))
        {
            _working = previous.Curve;
            SelectedIndex = previous.Selection;
            _message = e.Message;
        }
        RefreshPoints();
    }

    private EditState Capture() => new(_working.Clone(), SelectedIndex);

    private void Record(EditState previous)
    {
        if (_working.Matches(previous.Curve)) return;
        _undo.Add(previous);
        if (_undo.Count > 64) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Restore(List<EditState> from, List<EditState> to)
    {
        if (from.Count == 0 || _gesture is not null) return;
        to.Add(Capture());
        var previous = from[^1];
        from.RemoveAt(from.Count - 1);
        _working = previous.Curve;
        SelectedIndex = previous.Selection;
        _message = null;
        RefreshPoints();
    }

    private void RefreshPoints()
    {
        Points = _working.ActiveKnots;
        SelectedIndex = Points.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, Points.Count - 1);
        Notify();
    }

    private static bool IsEditError(Exception e) => e is InvalidOperationException or InvalidDataException or ArgumentException;
    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        Changed?.Invoke();
    }
}
