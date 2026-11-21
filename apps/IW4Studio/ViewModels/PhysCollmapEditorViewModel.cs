using System.Numerics;
using IW4.Formats.XModel;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.PhysCollmap;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class PhysCollmapEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private PhysCollmapDraft _baseline;
    private PhysCollmapDraft _working;
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private Snapshot? _gesture;
    private PhysCollmapViewportShape? _gestureShape;
    private Vector3 _modelMin, _modelMax;
    private string? _message;
    private sealed record Snapshot(PhysCollmapDraft Draft, int SelectedIndex);

    public PhysCollmapEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<PhysCollmapDraft>();
        _working = _baseline.Clone();
        SelectedIndex = _working.Geoms.Count > 0 ? 0 : -1;
        LoadModel(session.FindPhysCollmapPreviewModel());
        Refresh();
    }

    public event Action? SceneChanged;
    public string Name => _session.Entry.NormalizedName ?? "Collision map";
    public bool IsEditable => _session.CanEdit;
    public bool HasUnappliedChanges => !_working.Matches(_baseline);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool CanApply => IsEditable && _gesture is null && HasUnappliedChanges;
    public bool CanFit => CanTransform && ModelVertices.Count > 0;
    public bool CanTransform => IsEditable && !EditingBalance && SelectedIndex >= 0 && _working.Geoms[SelectedIndex].CanEditGeometry;
    public bool CanRotate => CanTransform && _working.Geoms[SelectedIndex].CanRotateGeometry;
    public string Status => _message ?? (!IsEditable ? "Read only" :
        HasUnappliedChanges ? "Unapplied changes" : _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");
    public int SelectedIndex { get; private set; }
    public bool EditingBalance { get; private set; }
    public Vector3 BalancePoint => _working.CenterOfMass;
    public PhysCollmapViewportTool Tool { get; private set; } = PhysCollmapViewportTool.Move;
    public PhysCollmapViewportLayer Layer { get; private set; } = PhysCollmapViewportLayer.Overlay;
    public IReadOnlyList<PhysCollmapViewportShape> Shapes { get; private set; } = [];
    public IReadOnlyList<Vector3> ModelVertices { get; private set; } = [];
    public IReadOnlyList<XModelExportTriangle> ModelTriangles { get; private set; } = [];
    public string? ModelName { get; private set; }
    public string ModelDescription { get; private set; } = "";
    public string Hint => Layer == PhysCollmapViewportLayer.Model ? "Drag the background to orbit." :
        EditingBalance ? "Drag an arrow to move the balance point." :
        SelectedIndex < 0 ? "Add a shape to start editing." :
        !IsEditable ? "Select a shape to inspect it. Drag the background to orbit." :
        !CanTransform ? "This hull has incomplete geometry. You can inspect or replace it." :
        Tool switch
        {
            PhysCollmapViewportTool.Move => "Drag an arrow, or slide the shape along the floor.",
            PhysCollmapViewportTool.Resize => "Drag a square grip to resize one side.",
            _ => "Drag a colored ring to rotate around its axis."
        };

    public void SelectShape(int index)
    {
        if (index < 0 || index >= Shapes.Count || _gesture is not null) return;
        SelectedIndex = index;
        EditingBalance = false;
        if (Layer == PhysCollmapViewportLayer.Model) Layer = PhysCollmapViewportLayer.Overlay;
        if (!CanRotate && Tool == PhysCollmapViewportTool.Rotate) Tool = PhysCollmapViewportTool.Move;
        _message = null;
        Notify();
    }

    public void SetTool(PhysCollmapViewportTool tool)
    {
        if (_gesture is not null || (!EditingBalance && !CanTransform) ||
            EditingBalance && tool != PhysCollmapViewportTool.Move ||
            tool == PhysCollmapViewportTool.Rotate && !CanRotate) return;
        Tool = tool;
        if (Layer == PhysCollmapViewportLayer.Model) Layer = PhysCollmapViewportLayer.Overlay;
        Notify();
    }

    public void SetLayer(PhysCollmapViewportLayer layer)
    {
        if (_gesture is not null || layer != PhysCollmapViewportLayer.Shape && ModelVertices.Count == 0) return;
        Layer = layer;
        Notify();
    }

    public void SetEditingBalance(bool value)
    {
        if (_gesture is not null || EditingBalance == value) return;
        EditingBalance = value;
        Tool = PhysCollmapViewportTool.Move;
        if (value && Layer == PhysCollmapViewportLayer.Model) Layer = PhysCollmapViewportLayer.Overlay;
        Notify();
    }

    public void ChooseModel(string name)
    {
        if (_gesture is not null) return;
        var model = _session.ResolveEditorReference<XModelAsset>(XAssetType.XModel, name, out _);
        if (model is null) { Report("That model is unavailable in this workspace."); return; }
        LoadModel(model);
        Notify();
    }

    private void LoadModel(XModelAsset? model)
    {
        ModelName = model?.Name;
        ModelVertices = []; ModelTriangles = [];
        if (model is not null)
        {
            for (int lod = model.MaxLoadedLod; lod < model.Lods.Count; lod++)
            {
                if (!XModelExportProjector.TryProjectMaterializedLod(model, lod, out var mesh, out _) ||
                    mesh is null || mesh.Triangles.Count == 0) continue;
                var vertices = mesh.Vertices.Select(v => v.Position).ToArray();
                ModelTriangles = mesh.Triangles;
                ModelVertices = vertices;
                _modelMin = vertices.Aggregate(Vector3.Min);
                _modelMax = vertices.Aggregate(Vector3.Max);
                ModelDescription = model.Name ?? "Workspace model";
                Layer = PhysCollmapViewportLayer.Overlay;
                return;
            }
        }
        ModelDescription = model is null ? "Choose a model to preview the fit." : $"{model.Name} · mesh unavailable";
        Layer = PhysCollmapViewportLayer.Shape;
    }

    public void AddShape(PhysGeomShape kind)
    {
        if (kind is not (PhysGeomShape.Box or PhysGeomShape.Cylinder)) return;
        Edit(() =>
        {
            var extents = ModelVertices.Count > 0 ? _modelMax - _modelMin :
                Shapes.Count > 0 ? Shapes[0].Size : new Vector3(16);
            var size = Vector3.Max(extents * (Shapes.Count == 0 ? 1f : .35f), new Vector3(.1f));
            Vector3 pos = ModelVertices.Count > 0 ? (_modelMin + _modelMax) / 2 :
                Shapes.Count > 0 ? Shapes[0].Position : Vector3.Zero;
            if (kind == PhysGeomShape.Cylinder)
            {
                size.X = size.Y = MathF.Max(size.X, size.Y);
                _working.AddCylinder(pos, size);
            }
            else _working.AddBox(pos, size);
            SelectedIndex = _working.Geoms.Count - 1;
            EditingBalance = false;
            Tool = PhysCollmapViewportTool.Move;
            if (Layer == PhysCollmapViewportLayer.Model) Layer = PhysCollmapViewportLayer.Overlay;
        });
    }

    public void RemoveShape()
    {
        if (SelectedIndex < 0 || EditingBalance) return;
        Edit(() => { _working.Remove(SelectedIndex); SelectedIndex = Math.Min(SelectedIndex, _working.Geoms.Count - 1); });
    }

    public void FitToModel()
    {
        if (!CanFit) return;
        Edit(() =>
        {
            Vector3 size = Vector3.Max(_modelMax - _modelMin, new Vector3(.01f));
            Vector3 center = (_modelMax + _modelMin) / 2f;
            var geom = _working.Geoms[SelectedIndex];
            if (geom.Shape == PhysGeomShape.Cylinder)
            {
                float diameter = 2 * ModelVertices.Max(v => new Vector2(v.X - center.X, v.Y - center.Y).Length());
                size.X = size.Y = Math.Max(.01f, diameter);
            }
            if (geom.Shape != PhysGeomShape.Brush) _working.SetOrientation(SelectedIndex, Matrix4x4.Identity);
            _working.SetSize(SelectedIndex, size);
            _working.SetPosition(SelectedIndex, center);
        });
    }

    public void SetExactValue(string field, int axis, float value)
    {
        if (!float.IsFinite(value) || axis is < 0 or > 2) { Report("Enter a finite number."); return; }
        if (field == "Balance")
        {
            Edit(() => { var v = BalancePoint; v[axis] = value; _working.CenterOfMass = v; });
            return;
        }
        if (!CanTransform) return;
        var s = Shapes[SelectedIndex];
        Edit(() =>
        {
            switch (field)
            {
                case "Position":
                    var position = s.Position; position[axis] = value; _working.SetPosition(SelectedIndex, position); break;
                case "Size":
                    if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Dimensions must be greater than zero.");
                    var size = s.Size; size[axis] = value;
                    if (s.Shape == PhysGeomShape.Cylinder && axis < 2) size.X = size.Y = value;
                    _working.SetSize(SelectedIndex, size); break;
                case "Rotation" when CanRotate:
                    var degrees = s.RotationDegrees; degrees[axis] = value; _working.SetOrientation(SelectedIndex, PhysCollmapViewportGeometry.Rotation(degrees)); break;
            }
        });
    }

    public void HandleViewportEdit(PhysCollmapViewportEdit edit)
    {
        if (!IsEditable) return;
        if (edit.Phase == PhysCollmapViewportPhase.Begin)
        {
            if (_gesture is not null || (edit.Index < 0 ? !EditingBalance : !CanTransform || edit.Index != SelectedIndex)) return;
            _gesture = Capture();
            _gestureShape = edit.Index >= 0 ? Shapes[edit.Index] : null;
            return;
        }
        if (_gesture is not { } start) return;
        if (edit.Phase == PhysCollmapViewportPhase.Cancel)
        {
            _working = start.Draft; _gesture = null; Refresh(); return;
        }
        if (edit.Phase == PhysCollmapViewportPhase.Update)
        {
            _working = start.Draft.Clone();
            try
            {
                if (_gestureShape is not { } source) _working.CenterOfMass = edit.Position;
                else
                {
                    int i = start.SelectedIndex;
                    if (edit.Size != source.Size) _working.SetSize(i, edit.Size);
                    if (edit.Position != source.Position) _working.SetPosition(i, edit.Position);
                    if (edit.RotationDegrees != source.RotationDegrees) _working.SetOrientation(i, PhysCollmapViewportGeometry.Rotation(edit.RotationDegrees));
                }
                _message = null;
            }
            catch (Exception e) when (IsEditError(e)) { _working = start.Draft.Clone(); _message = e.Message; }
            Refresh();
            return;
        }
        if (edit.Phase == PhysCollmapViewportPhase.Commit)
        {
            _gesture = null;
            Record(start);
            Refresh();
        }
    }

    internal void RefreshSessionState() => Notify();

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    public void ApplyChanges()
    {
        if (!CanApply) return;
        try
        {
            var issues = _session.ValidateCandidate(_working);
            if (issues.HasErrors) { Report(string.Join(" ", issues.Issues.Select(i => i.Message))); return; }
            var applied = _session.ApplyAndRead<PhysCollmapDraft>(current => current.ApplyEdits(_baseline, _working));
            _baseline = applied; _working = applied.Clone();
            _undo.Clear(); _redo.Clear(); _message = null;
        }
        catch (Exception e) when (IsEditError(e)) { _message = e.Message; }
        Refresh();
    }

    public void Report(string message) { _message = message; Notify(); }
    private static bool IsEditError(Exception e) => e is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException or NotSupportedException;
    private Snapshot Capture() => new(_working.Clone(), SelectedIndex);

    private void Edit(Action action)
    {
        if (!IsEditable || _gesture is not null) return;
        var before = Capture();
        try { action(); Record(before); _message = null; }
        catch (Exception e) when (IsEditError(e)) { _working = before.Draft; SelectedIndex = before.SelectedIndex; _message = e.Message; }
        Refresh();
    }

    private void Record(Snapshot before)
    {
        if (_working.Matches(before.Draft)) return;
        _undo.Add(before);
        if (_undo.Count > 48) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Restore(List<Snapshot> from, List<Snapshot> to)
    {
        if (_gesture is not null || from.Count == 0) return;
        to.Add(Capture());
        var prior = from[^1]; from.RemoveAt(from.Count - 1);
        _working = prior.Draft; SelectedIndex = prior.SelectedIndex; _message = null;
        Refresh();
    }

    private void Refresh()
    {
        Shapes = _working.Geoms.Select(g => new PhysCollmapViewportShape(g.Shape, g.Position, g.Size,
            PhysCollmapViewportGeometry.Degrees(g.Orientation), g.Brush, g.CanEditGeometry, g.CanRotateGeometry)).ToArray();
        if (Tool == PhysCollmapViewportTool.Rotate && !CanRotate) Tool = PhysCollmapViewportTool.Move;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        SceneChanged?.Invoke();
    }
}
