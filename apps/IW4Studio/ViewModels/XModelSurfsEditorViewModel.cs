using System.Numerics;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.XModelSurfs;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class XModelSurfsEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private readonly IReadOnlyList<XModelSurfsUsage> _usages;
    private readonly List<XModelSurfsDraft> _undo = [], _redo = [];
    private XModelSurfsDraft _baseline, _working;
    private XModelSurfsDraft? _gesture, _donor;
    private IReadOnlyList<XModelSurfsUsage> _donorUsages = [];
    private string? _message;

    public XModelSurfsEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<XModelSurfsDraft>();
        _working = _baseline.Clone();
        _usages = session.FindXModelSurfsUsages(_baseline);
        var choices = new List<SourceChoice> { new(Name, "This collection") };
        if (_usages.FirstOrDefault() is { } owner)
        {
            for (int lod = 0; lod < owner.Model.Lods.Count; lod++)
            {
                var collection = owner.Model.Lods[lod].ModelSurfs;
                if (collection?.Name is { Length: > 0 } name && !choices.Any(c => c.Name == name))
                    choices.Add(new(name, $"LOD {lod}"));
            }
        }
        Sources = choices;
        SelectedIndex = _working.Surfaces.Count == 0 ? -1 : 0;
        DonorName = Name;
        RefreshGeometry();
    }

    public sealed record SourceChoice(string Name, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<SourceChoice> Sources { get; private set; }
    public event Action? Changed;
    public string Name => _session.Entry.NormalizedName ?? "Untitled collection";
    public string Heading => DisplayLabel(_usages.FirstOrDefault()?.Model.Name ?? Name, "com_");
    public string Context
    {
        get
        {
            if (_usages.FirstOrDefault() is not { } usage) return "Model surfaces";
            int users = _usages.Select(row => row.Model).Distinct().Count();
            string shared = users > 1 ? $"  ·  Shared by {users} models" : "";
            return $"Model surfaces  ·  LOD {usage.LodIndex}{shared}";
        }
    }
    public bool IsEditable => _session.CanEdit;
    public int SelectedIndex { get; private set; }
    public XModelSurfsViewportTool Tool { get; private set; } = XModelSurfsViewportTool.Move;
    public bool Wireframe { get; private set; }
    public bool HasUnappliedChanges => !_working.Matches(_baseline);
    public bool CanUndo => _gesture is null && _undo.Count != 0;
    public bool CanRedo => _gesture is null && _redo.Count != 0;
    public bool CanApply => IsEditable && _gesture is null && HasUnappliedChanges;
    public bool CanTransform => IsEditable && SelectedIndex >= 0 && _working.Surfaces[SelectedIndex].CanEdit;
    public string Status => _message ?? (!IsEditable ? "Read only" : HasUnappliedChanges ? "Unapplied changes" :
        _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");
    public string Hint => SelectedIndex < 0 ? "Choose a collection to start with a mesh." :
        !IsEditable ? "Select a part to inspect it. Drag the background to orbit." :
        !CanTransform ? _working.Surfaces[SelectedIndex].EditBlocker ?? "This surface can be inspected." :
        Tool == XModelSurfsViewportTool.Move ? "Drag an arrow to move the selected surface. Drag the background to orbit." :
        "Drag a square handle to resize the selected surface.";
    public IReadOnlyList<XModelSurfsViewportSurface> Scene { get; private set; } = [];
    public IReadOnlyList<string> Labels { get; private set; } = [];
    public string DonorName { get; private set; }
    public int DonorIndex { get; private set; } = -1;
    public IReadOnlyList<string> DonorLabels { get; private set; } = [];
    public bool CanPreview => IsEditable && _gesture is null && DonorIndex >= 0 &&
        (SelectedIndex < 0 || _donor is not null || SelectedIndex != DonorIndex);
    public string SurfaceInfo => SelectedIndex < 0 ? "" :
        $"{Scene[SelectedIndex].Positions.Count:N0} vertices  ·  {Scene[SelectedIndex].TriangleIndices.Count / 3:N0} triangles";

    public void SelectSurface(int index)
    {
        if (_gesture is not null || index < 0 || index >= Scene.Count || index == SelectedIndex) return;
        SelectedIndex = index;
        ChooseDonorIndex();
        _message = null;
        Notify();
    }

    public void SetTool(XModelSurfsViewportTool tool)
    {
        if (_gesture is not null || !CanTransform) return;
        Tool = tool;
        Notify();
    }

    public void SetWireframe(bool value)
    {
        if (Wireframe == value) return;
        Wireframe = value;
        Notify();
    }

    public void SetDonor(string name)
    {
        if (!IsEditable || _gesture is not null) return;
        if (StringComparer.OrdinalIgnoreCase.Equals(name, Name))
        {
            _donor = null;
            _donorUsages = _usages;
            DonorName = Name;
        }
        else
        {
            var donor = _session.OpenXModelSurfsSource(name);
            if (donor is null) { Report("This collection has no available geometry."); return; }
            _donor = donor;
            _donorUsages = _session.FindXModelSurfsUsages(donor);
            DonorName = name;
            if (!Sources.Any(choice => choice.Name == name))
                Sources = [.. Sources, new(name, name)];
        }
        RefreshLabels();
        ChooseDonorIndex();
        _message = null;
        Notify();
    }

    public void SelectDonorSurface(int index)
    {
        if (index < 0 || index >= DonorLabels.Count || _gesture is not null) return;
        DonorIndex = index;
        _message = null;
        Notify();
    }

    private void ChooseDonorIndex()
    {
        int count = DonorLabels.Count;
        DonorIndex = count == 0 ? -1 : _donor is not null ? Math.Clamp(SelectedIndex, 0, count - 1) :
            SelectedIndex == 0 && count > 1 ? 1 : 0;
    }

    public void PreviewReplacement()
    {
        if (!CanPreview) return;
        Edit(() =>
        {
            var donor = _donor ?? _working;
            if (SelectedIndex < 0)
                _working.SeedFrom(donor);
            else
                _working = _session.StageXModelSurfaceReplacement(_working, SelectedIndex, donor, DonorIndex);
        });
    }

    public void SetExactValue(bool size, int axis, float value)
    {
        if (!CanTransform || _gesture is not null || axis is < 0 or > 2) return;
        if (!float.IsFinite(value)) { Report("Enter a finite number."); return; }
        var surface = Scene[SelectedIndex];
        var position = surface.Position;
        var dimensions = surface.Size;
        if (size) dimensions[axis] = value; else position[axis] = value;
        Edit(() => SetTransform(SelectedIndex, position, dimensions));
    }

    private void SetTransform(int index, Vector3 position, Vector3 size)
    {
        var row = _working.Surfaces[index];
        var currentSize = row.OriginalSize * row.Scale;
        if (size != currentSize)
        {
            var scale = row.Scale;
            for (int axis = 0; axis < 3; axis++)
            {
                if (size[axis] == currentSize[axis]) continue;
                if (row.OriginalSize[axis] <= 0 || size[axis] <= 0)
                    throw new InvalidDataException("Give this dimension a positive size. A flat surface has no thickness to resize.");
                scale[axis] = size[axis] / row.OriginalSize[axis];
            }
            _working.SetScale(index, scale);
        }
        if (position != row.OriginalCenter + row.Translation)
            _working.SetTranslation(index, position - row.OriginalCenter);
    }

    public void HandleViewportEdit(XModelSurfsViewportEdit edit)
    {
        if (edit.Index != SelectedIndex || !CanTransform) return;
        switch (edit.Phase)
        {
            case XModelSurfsViewportPhase.Begin:
                _gesture ??= _working.Clone();
                break;
            case XModelSurfsViewportPhase.Update when _gesture is { } start:
                _working = start.Clone();
                try { SetTransform(edit.Index, edit.Position, edit.Size); _message = null; }
                catch (Exception e) when (IsEditError(e)) { _working = start.Clone(); _message = e.Message; }
                RefreshGeometry(false);
                break;
            case XModelSurfsViewportPhase.Commit when _gesture is { } before:
                _gesture = null;
                Record(before);
                RefreshGeometry(false);
                break;
            case XModelSurfsViewportPhase.Cancel when _gesture is { } previous:
                _working = previous;
                _gesture = null;
                _message = null;
                RefreshGeometry(false);
                break;
        }
    }

    public void ApplyChanges()
    {
        if (!CanApply) return;
        try
        {
            var validation = _session.ValidateCandidate(_working);
            if (validation.HasErrors) { Report(string.Join(" ", validation.Issues.Select(issue => issue.Message))); return; }
            _baseline = _session.ApplyAndRead<XModelSurfsDraft>(current => current.ApplyEdits(_baseline, _working));
            _working = _baseline.Clone();
            _undo.Clear();
            _redo.Clear();
            _message = null;
        }
        catch (Exception e) when (IsEditError(e)) { _message = e.Message; }
        RefreshGeometry();
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    public void Report(string message) { _message = message; Notify(); }
    internal void RefreshSessionState() => Notify();

    private void Edit(Action mutation)
    {
        if (!IsEditable || _gesture is not null) return;
        var previous = _working.Clone();
        try { mutation(); _message = null; Record(previous); }
        catch (Exception e) when (IsEditError(e)) { _working = previous; _message = e.Message; }
        RefreshGeometry();
    }

    private void Record(XModelSurfsDraft previous)
    {
        if (_working.Matches(previous)) return;
        if (_session.ValidateXModelSurfsBounds(_working) is { } problem)
        {
            _working = previous;
            _message = problem;
            return;
        }
        _undo.Add(previous);
        if (_undo.Count > 48) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Restore(List<XModelSurfsDraft> from, List<XModelSurfsDraft> to)
    {
        if (from.Count == 0 || _gesture is not null) return;
        to.Add(_working);
        _working = from[^1];
        from.RemoveAt(from.Count - 1);
        _message = null;
        RefreshGeometry();
    }

    private void RefreshGeometry(bool updateLabels = true)
    {
        Scene = _working.Surfaces.Select(s => new XModelSurfsViewportSurface(
            s.Positions, s.Normals, s.TriangleIndices, s.OriginalCenter, s.OriginalSize,
            s.OriginalCenter + s.Translation, s.OriginalSize * s.Scale, s.CanEdit)).ToArray();
        SelectedIndex = Scene.Count == 0 ? -1 : Math.Clamp(SelectedIndex, 0, Scene.Count - 1);
        if (updateLabels)
        {
            RefreshLabels();
            if (DonorIndex < 0 || DonorIndex >= DonorLabels.Count) ChooseDonorIndex();
        }
        Notify();
    }

    private void RefreshLabels()
    {
        Labels = SurfaceLabels(_working, _usages);
        DonorLabels = _donor is null ? Labels : SurfaceLabels(_donor, _donorUsages);
    }

    private static string[] SurfaceLabels(XModelSurfsDraft draft, IReadOnlyList<XModelSurfsUsage> usages) =>
        Enumerable.Range(0, draft.Surfaces.Count).Select(i => usages.FirstOrDefault()?.MaterialName(i) is { Length: > 0 } material
            ? $"{i + 1}.  {DisplayLabel(material, "mtl_")}" : $"Surface {i + 1}").ToArray();

    private static string DisplayLabel(string identifier, string prefix)
    {
        string label = identifier[(identifier.LastIndexOf('/') + 1)..];
        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) label = label[prefix.Length..];
        label = label.Replace('_', ' ').Trim();
        return label.Length == 0 ? identifier : char.ToUpperInvariant(label[0]) + label[1..];
    }

    private static bool IsEditError(Exception e) => e is InvalidDataException or InvalidOperationException or
        NotSupportedException or ArgumentException or OverflowException;

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        Changed?.Invoke();
    }
}
