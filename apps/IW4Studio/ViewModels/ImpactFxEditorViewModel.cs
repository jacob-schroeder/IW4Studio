using IW4.Game.Assets.Fx;
using IW4.Game.Assets.ImpactFx;
using IW4.Game.Assets.Material;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

internal sealed class ImpactFxEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private readonly List<(ImpactFxDraft Draft, int Row, int Slot)> _undo = [];
    private ImpactFxDraft _baseline;
    private ImpactFxDraft _working;
    private string? _message;

    internal ImpactFxEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<ImpactFxDraft>();
        _working = _baseline.Clone();
        SelectPreview();
    }

    internal event Action? Changed;
    internal string Heading => AssetExplorerEntryViewModel.GetDisplayName(
        _session.Entry.AssetType, _session.Entry.OriginalName ?? string.Empty);
    internal int Row { get; private set; }
    internal int Slot { get; private set; } = (int)MaterialSurfaceType.Metal;
    internal int RowCount => _working.RowCount;
    internal const int SlotCount = FxImpactEntry.SurfaceEffectCount + FxImpactEntry.FleshEffectCount;
    internal bool IsEditable => _session.CanEdit;
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanApply => IsEditable && HasUnappliedChanges;
    public bool HasUnappliedChanges => !_working.Matches(_baseline);
    internal FxEffectDefAsset? PreviewEffect { get; private set; }
    internal string? EffectName => _working.GetEffect(Row, Slot)?.Name?.TrimStart(',');
    internal string Status => _message ?? (!IsEditable ? "Read only" : HasUnappliedChanges ? "Unapplied changes" :
        _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");

    internal bool IsAssigned(int slot) => _working.IsAssigned(Row, slot);

    internal void Select(int row, int slot)
    {
        if ((uint)row >= RowCount || (uint)slot >= SlotCount || (row == Row && slot == Slot)) return;
        Row = row;
        Slot = slot;
        _message = null;
        SelectPreview();
        Changed?.Invoke();
    }

    internal void Assign(string name)
    {
        if (!IsEditable) return;
        if (!_session.TryResolveImpactEffect(name, out var effect) || effect is null)
        {
            _message = "This effect is unavailable. Choose an effect with a loaded definition.";
            Changed?.Invoke();
            return;
        }
        Edit(effect);
    }

    internal void Clear()
    {
        if (IsEditable && IsAssigned(Slot)) Edit(null);
    }

    private void Edit(FxEffectDefAsset? effect)
    {
        var previous = _working.Clone();
        if (!_working.SetEffect(Row, Slot, effect)) return;
        _undo.Add((previous, Row, Slot));
        if (_undo.Count > 32) _undo.RemoveAt(0);
        _message = null;
        SelectPreview();
        Notify();
    }

    internal void Undo()
    {
        if (!CanUndo) return;
        (_working, Row, Slot) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _message = null;
        RefreshWorkspace();
    }

    internal void Apply()
    {
        if (!CanApply) return;
        try
        {
            var current = _session.ApplyAndRead<ImpactFxDraft>(draft => draft.ApplyEdits(_baseline, _working));
            if (_session.Validation.HasErrors)
            {
                _message = string.Join(" ", _session.Validation.Issues.Select(issue => issue.Message));
            }
            else
            {
                _baseline = current;
                _working = current.Clone();
                _undo.Clear();
                _message = null;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _message = ex.Message;
        }
        SelectPreview();
        Notify();
    }

    internal void RefreshWorkspace()
    {
        if (!HasUnappliedChanges)
        {
            var current = _session.OpenDraft<ImpactFxDraft>();
            if (!_baseline.Matches(current))
            {
                _undo.Clear();
                _baseline = current;
                _working = current.Clone();
                _message = null;
            }
        }
        SelectPreview();
        Notify();
    }

    private void SelectPreview()
    {
        var stored = _working.GetEffect(Row, Slot);
        if (stored?.Name is { Length: > 0 } name && _session.TryResolveImpactEffect(name, out var resolved))
            PreviewEffect = resolved;
        else
            PreviewEffect = stored?.Name?.StartsWith(',') == true ? null : stored;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        Changed?.Invoke();
    }
}
