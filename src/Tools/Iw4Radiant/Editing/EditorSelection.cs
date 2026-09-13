using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal sealed class EditorSelection
{
    private readonly List<object> _items = [];
    internal IReadOnlyList<object> Items => _items;
    internal object? Active => _items.LastOrDefault();
    internal int Count => _items.Count;
    internal bool Contains(object item) => _items.Any(selected => Same(selected, item));
    internal void Clear() => _items.Clear();

    internal void Set(object? item, bool additive = false, bool toggle = false)
    {
        if (!additive) _items.Clear();
        if (item is null) return;
        int index = _items.FindIndex(selected => Same(selected, item));
        if (index >= 0)
        {
            if (toggle) _items.RemoveAt(index);
            return;
        }
        // Keep only one level of an entity/object/component hierarchy selected.
        object owner = Owner(item);
        _items.RemoveAll(selected =>
            ReferenceEquals(Owner(selected), owner) &&
                (ReferenceEquals(item, owner) || ReferenceEquals(selected, owner)) ||
            item is MapEntity entity && Owns(entity, Owner(selected)) ||
            selected is MapEntity selectedEntity && Owns(selectedEntity, owner));
        _items.Add(item);
    }

    internal void SetRange(IEnumerable<object> items)
    {
        object[] values = items.ToArray();
        _items.Clear();
        foreach (object item in values) Set(item, additive: true);
    }

    internal SelectionPath[] Capture(MapDocument document)
    {
        var paths = new List<SelectionPath>();
        foreach (object item in _items)
        for (int index = 0; index < document.Entities.Count; index++)
        {
            MapEntity entity = document.Entities[index];
            if (ReferenceEquals(item, entity)) { paths.Add(new(index)); break; }
            object owner = Owner(item);
            if (owner is MapBrush brush && entity.Brushes.IndexOf(brush) is int bi && bi >= 0)
            {
                paths.Add(new(index, Brush: bi,
                    Face: item is BrushFaceSelection face ? brush.Faces.IndexOf(face.Face) : -1,
                    BrushVertex: item is BrushVertexSelection vertex ? vertex.Position : null));
                break;
            }
            if (owner is MapTerrain terrain && entity.Terrains.IndexOf(terrain) is int ti && ti >= 0)
            {
                paths.Add(new(index, Terrain: ti, TerrainVertex: item is TerrainVertexSelection vertex ? vertex.Index : -1));
                break;
            }
        }
        return paths.ToArray();
    }

    internal void Restore(MapDocument document, IEnumerable<SelectionPath> paths) =>
        SetRange(paths.Select(path => path.Resolve(document)).OfType<object>());

    internal static object Owner(object item) => item switch
    {
        BrushFaceSelection face => face.Brush,
        BrushVertexSelection vertex => vertex.Brush,
        TerrainVertexSelection vertex => vertex.Terrain,
        _ => item
    };

    private static bool Owns(MapEntity entity, object item) => item switch
    {
        MapBrush brush => entity.Brushes.Contains(brush),
        MapTerrain terrain => entity.Terrains.Contains(terrain),
        _ => false
    };

    private static bool Same(object a, object b) => (a, b) switch
    {
        (BrushFaceSelection x, BrushFaceSelection y) => x == y,
        (TerrainVertexSelection x, TerrainVertexSelection y) => x == y,
        (BrushVertexSelection x, BrushVertexSelection y) => ReferenceEquals(x.Brush, y.Brush) && x.Position == y.Position,
        _ => ReferenceEquals(a, b)
    };
}
