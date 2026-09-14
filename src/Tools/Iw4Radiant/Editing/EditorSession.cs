using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal sealed class EditorSession
{
    internal EditorSession() => Scene = new EditorScene(this);
    internal EditorVisibility Visibility { get; } = new();
    internal PrefabLibrary Prefabs { get; } = new();
    internal EditorScene Scene { get; }
    private readonly List<(MapDocument Document, long Revision, SelectionPath[] Selection)> _undo = [];
    private readonly List<(MapDocument Document, long Revision, SelectionPath[] Selection)> _redo = [];
    private MapDocument? _beforeEdit;
    private SelectionPath[] _beforeSelection = [];
    private long _revision, _savedRevision, _nextRevision = 1;
    private Action<Vector3, Vector3?>? _place;
    internal string? PlacementLabel { get; private set; }
    internal bool HasPlacement => _place is not null;

    public MapDocument Document { get; private set; } = MapDocument.Create();
    public EditorSelection Selection { get; } = new();
    public string? FilePath { get; private set; }
    public string Material { get; set; } = "";
    public EditorTool Tool { get; set; }
    public TransformMode TransformMode { get; set; }
    public TerrainSculptMode SculptMode { get; set; }
    public ClipMode ClipMode { get; set; }
    public bool TextureLock { get; set; } = true;
    public float GridSize { get; set; } = 16;
    public float AngleSnap { get; set; } = 15;
    public float ScaleSnap { get; set; } = 0.1f;
    public float BrushBottom { get; set; }
    public float BrushHeight { get; set; } = 64;
    public int TerrainVertices { get; set; } = 5;
    public float SculptRadius { get; set; } = 128;
    public float SculptStrength { get; set; } = 8;
    public float FlattenHeight { get; set; }
    internal Vector4 PaintColor { get; set; } = Vector4.One;
    internal float PaintOpacity { get; set; } = 0.25f;
    internal float PaintAlpha { get; set; } = 1;
    public bool IsDirty => _revision != _savedRevision || _beforeEdit is not null;
    public bool CanTransformSelection => Selection.Count > 0 && Selection.Items.All(item =>
        Visibility.CanSelect(Document, item) && SelectionGeometry.CanTransform(item));
    public (Vector3 Min, Vector3 Max)? SelectionBounds => Scene.Bounds(Selection.Items);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event EventHandler? Changed;

    public void Refresh()
    {
        Visibility.Invalidate();
        Selection.SetRange(Selection.Items.Where(item => ReferenceEquals(item, Document.World) ||
            Visibility.CanSelect(Document, item)).ToArray());
        Scene.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public float Snap(float value) => MathF.Round(value / GridSize, MidpointRounding.AwayFromZero) * GridSize;

    public void Select(object? value, bool additive = false, bool toggle = false)
    {
        if (value is not null && !ReferenceEquals(value, Document.World) && !Visibility.CanSelect(Document, value)) return;
        Selection.Set(value, additive, toggle);
        Refresh();
    }

    public void SelectRange(IEnumerable<object> values)
    {
        Selection.SetRange(values.Where(item => ReferenceEquals(item, Document.World) || Visibility.CanSelect(Document, item)));
        Refresh();
    }

    internal void BeginPlacement(string label, Action<Vector3, Vector3?> place)
    {
        _place = place;
        PlacementLabel = label;
        Refresh();
    }

    internal void CancelPlacement()
    {
        _place = null;
        PlacementLabel = null;
        Refresh();
    }

    internal void Place(Vector3 position, Vector3? normal, bool repeat)
    {
        if (_place is not { } place) return;
        place(position, normal);
        if (!repeat) CancelPlacement();
    }

    public void Replace(MapDocument document, string? path)
    {
        Document = document;
        _place = null;
        PlacementLabel = null;
        FilePath = path;
        Visibility.Clear();
        Prefabs.Reload(document, path);
        Selection.Clear();
        _undo.Clear();
        _redo.Clear();
        _beforeEdit = null;
        _beforeSelection = [];
        _revision = _savedRevision = 0;
        Refresh();
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        _savedRevision = _revision;
        Refresh();
    }

    public void BeginEdit()
    {
        if (_beforeEdit is not null) return;
        _beforeSelection = Selection.Capture(Document);
        _beforeEdit = Document.Clone();
    }

    public void CompleteEdit(bool changed)
    {
        if (_beforeEdit is { } before && changed)
        {
            _undo.Add((before, _revision, _beforeSelection));
            if (_undo.Count > 32) _undo.RemoveAt(0);
            _redo.Clear();
            _revision = _nextRevision++;
        }
        else if (_beforeEdit is { } unchanged)
        {
            Document = unchanged;
            Visibility.Clear();
            Selection.Restore(Document, _beforeSelection);
        }
        _beforeEdit = null;
        _beforeSelection = [];
        Refresh();
    }

    public void CancelEdit()
    {
        if (_beforeEdit is { } before)
        {
            Document = before;
            Visibility.Clear();
            Selection.Restore(Document, _beforeSelection);
        }
        _beforeEdit = null;
        _beforeSelection = [];
        Refresh();
    }

    public void Edit(Action action)
    {
        BeginEdit();
        try
        {
            action();
            CompleteEdit(true);
        }
        catch
        {
            CancelEdit();
            throw;
        }
    }

    public void Undo()
    {
        if (_beforeEdit is not null) CancelEdit();
        if (_undo.Count == 0) return;
        _redo.Add((Document, _revision, Selection.Capture(Document)));
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Document = previous.Document;
        Visibility.Clear();
        _revision = previous.Revision;
        Selection.Restore(Document, previous.Selection);
        Refresh();
    }

    public void Redo()
    {
        if (_beforeEdit is not null) CancelEdit();
        if (_redo.Count == 0) return;
        _undo.Add((Document, _revision, Selection.Capture(Document)));
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Document = next.Document;
        Visibility.Clear();
        _revision = next.Revision;
        Selection.Restore(Document, next.Selection);
        Refresh();
    }

    public static (Vector3 Min, Vector3 Max) EntityBounds(MapEntity entity)
    {
        if (PointEntityGeometry.IsPointEntity(entity) && PointEntityGeometry.TryRadiusBounds(entity, out var radiusBounds))
            return radiusBounds;
        var bounds = entity.Brushes.Select(brush => brush.GetBounds())
            .Concat(entity.Terrains.Select(terrain => terrain.GetBounds())).ToArray();
        if (bounds.Length == 0)
            return (EntityOrigin(entity) - new Vector3(8), EntityOrigin(entity) + new Vector3(8));
        return (bounds.Select(value => value.Min).Aggregate(Vector3.Min),
            bounds.Select(value => value.Max).Aggregate(Vector3.Max));
    }

    public static Vector3 EntityOrigin(MapEntity entity)
    {
        string[] parts = entity.Properties.GetValueOrDefault("origin", "0 0 0")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
            float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z))
            return new Vector3(x, y, z);
        return Vector3.Zero;
    }
}
