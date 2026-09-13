using System.Globalization;
using System.Numerics;

using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal sealed class EditorSession
{
    private readonly List<(MapDocument Document, long Revision)> _undo = [];
    private readonly List<(MapDocument Document, long Revision)> _redo = [];
    private MapDocument? _beforeEdit;
    private long _revision, _savedRevision, _nextRevision = 1;

    public MapDocument Document { get; private set; } = MapDocument.Create();
    public object? Selection { get; private set; }
    public string? FilePath { get; private set; }
    public string Material { get; set; } = "";
    public EditorTool Tool { get; set; }
    public float GridSize { get; set; } = 16;
    public float BrushBottom { get; set; }
    public float BrushHeight { get; set; } = 64;
    public int TerrainVertices { get; set; } = 5;
    public float SculptRadius { get; set; } = 128;
    public float SculptStrength { get; set; } = 8;
    public bool IsDirty => _revision != _savedRevision || _beforeEdit is not null;
    public bool CanTransformSelection => Selection is MapBrush or MapTerrain ||
        Selection is MapEntity entity && entity.ClassName != "worldspawn" && entity.PreservedPrimitives.Count == 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event EventHandler? Changed;

    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
    public float Snap(float value) => MathF.Round(value / GridSize, MidpointRounding.AwayFromZero) * GridSize;

    public void Select(object? value)
    {
        Selection = value;
        Refresh();
    }

    public void Replace(MapDocument document, string? path)
    {
        Document = document;
        FilePath = path;
        Selection = null;
        _undo.Clear();
        _redo.Clear();
        _beforeEdit = null;
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
        _beforeEdit ??= Document.Clone();
    }

    public void CompleteEdit(bool changed)
    {
        if (_beforeEdit is { } before && changed)
        {
            _undo.Add((before, _revision));
            if (_undo.Count > 32)
                _undo.RemoveAt(0);
            _redo.Clear();
            _revision = _nextRevision++;
        }
        else if (_beforeEdit is { } unchanged)
        {
            Selection = MatchingSelection(unchanged);
            Document = unchanged;
        }
        _beforeEdit = null;
        Refresh();
    }

    public void CancelEdit()
    {
        if (_beforeEdit is { } before)
        {
            Selection = MatchingSelection(before);
            Document = before;
        }
        _beforeEdit = null;
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
        if (_undo.Count == 0)
            return;
        _redo.Add((Document, _revision));
        (Document, _revision) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Selection = null;
        Refresh();
    }

    public void Redo()
    {
        if (_beforeEdit is not null) CancelEdit();
        if (_redo.Count == 0)
            return;
        _undo.Add((Document, _revision));
        (Document, _revision) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Selection = null;
        Refresh();
    }

    public void DeleteSelection()
    {
        if (Selection is null || ReferenceEquals(Selection, Document.World))
            return;
        Edit(() =>
        {
            foreach (var entity in Document.Entities)
            {
                if (Selection is MapBrush brush) entity.Brushes.Remove(brush);
                if (Selection is MapTerrain terrain) entity.Terrains.Remove(terrain);
            }
            if (Selection is MapEntity selectedEntity) Document.Entities.Remove(selectedEntity);
            Selection = null;
        });
    }

    public void DuplicateSelection()
    {
        if (!CanTransformSelection)
            return;
        Edit(() =>
        {
            foreach (var entity in Document.Entities.ToArray())
            {
                if (Selection is MapBrush brush && entity.Brushes.Contains(brush))
                {
                    var clone = brush.Clone();
                    clone.Translate(new Vector3(GridSize, GridSize, 0));
                    entity.Brushes.Add(clone);
                    Selection = clone;
                    break;
                }
                if (Selection is MapTerrain terrain && entity.Terrains.Contains(terrain))
                {
                    var clone = terrain.Clone();
                    clone.Translate(new Vector3(GridSize, GridSize, 0));
                    entity.Terrains.Add(clone);
                    Selection = clone;
                    break;
                }
            }
            if (Selection is MapEntity selectedEntity)
            {
                var clone = selectedEntity.Clone();
                Document.Entities.Add(clone);
                Selection = clone;
                TranslateSelection(new Vector3(GridSize, GridSize, 0));
            }
        });
    }

    public void ApplyMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material) || material.Any(char.IsWhiteSpace) ||
            material.Any(char.IsControl) || material.Contains("//", StringComparison.Ordinal) ||
            material.Contains("/*", StringComparison.Ordinal) || material.IndexOfAny(['"', '{', '}', '(', ')', ';']) >= 0)
            throw new ArgumentException("A material must be a single Radiant asset name.");
        Material = material;
        if (Selection is MapBrush or MapTerrain)
            Edit(() =>
            {
                if (Selection is MapBrush brush)
                    foreach (var face in brush.Faces) face.Material = material;
                if (Selection is MapTerrain terrain) terrain.Material = material;
            });
        else
            Refresh();
    }

    public (Vector3 Min, Vector3 Max)? SelectionBounds => Selection switch
    {
        MapBrush brush => brush.GetBounds(),
        MapTerrain terrain => terrain.GetBounds(),
        MapEntity entity => EntityBounds(entity),
        _ => null
    };

    public static (Vector3 Min, Vector3 Max) EntityBounds(MapEntity entity)
    {
        var bounds = entity.Brushes.Select(brush => brush.GetBounds())
            .Concat(entity.Terrains.Select(terrain => terrain.GetBounds())).ToArray();
        if (bounds.Length == 0)
            return (EntityOrigin(entity) - new Vector3(8), EntityOrigin(entity) + new Vector3(8));
        return (bounds.Select(value => value.Min).Aggregate(Vector3.Min),
            bounds.Select(value => value.Max).Aggregate(Vector3.Max));
    }

    public void TranslateSelection(Vector3 offset)
    {
        if (!CanTransformSelection) return;
        if (Selection is MapBrush brush) brush.Translate(offset);
        if (Selection is MapTerrain terrain) terrain.Translate(offset);
        if (Selection is MapEntity entity && !ReferenceEquals(entity, Document.World))
        {
            Vector3 origin = EntityOrigin(entity) + offset;
            entity.Properties["origin"] = FormattableString.Invariant($"{origin.X:G9} {origin.Y:G9} {origin.Z:G9}");
            foreach (var ownedBrush in entity.Brushes) ownedBrush.Translate(offset);
            foreach (var ownedTerrain in entity.Terrains) ownedTerrain.Translate(offset);
        }
    }

    private object? MatchingSelection(MapDocument target)
    {
        for (int entityIndex = 0; entityIndex < Math.Min(Document.Entities.Count, target.Entities.Count); entityIndex++)
        {
            var entity = Document.Entities[entityIndex];
            var targetEntity = target.Entities[entityIndex];
            if (ReferenceEquals(entity, Selection)) return targetEntity;
            if (Selection is MapBrush brush)
            {
                int index = entity.Brushes.IndexOf(brush);
                if (index >= 0 && index < targetEntity.Brushes.Count) return targetEntity.Brushes[index];
            }
            if (Selection is MapTerrain terrain)
            {
                int index = entity.Terrains.IndexOf(terrain);
                if (index >= 0 && index < targetEntity.Terrains.Count) return targetEntity.Terrains[index];
            }
        }
        return null;
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
