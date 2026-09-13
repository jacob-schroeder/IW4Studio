using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class SelectionGeometry
{
    internal static IEnumerable<object> GetVertexHandles(EditorSelection selection)
    {
        var owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (object selected in selection.Items)
        {
            object owner = EditorSelection.Owner(selected);
            if (owner is MapEntity entity)
            {
                foreach (MapBrush brush in entity.Brushes) owners.Add(brush);
                foreach (MapTerrain terrain in entity.Terrains) owners.Add(terrain);
            }
            else owners.Add(owner);
        }
        foreach (object owner in owners)
        {
            if (owner is MapBrush brush)
                foreach (Vector3 point in brush.GetVertices()) yield return new BrushVertexSelection(brush, point);
            if (owner is MapTerrain terrain)
                for (int index = 0; index < terrain.Vertices.Length; index++) yield return new TerrainVertexSelection(terrain, index);
        }
    }

    internal static (Vector3 Min, Vector3 Max)? Bounds(IEnumerable<object> items)
    {
        var bounds = items.Select(Bounds).OfType<(Vector3 Min, Vector3 Max)>().ToArray();
        return bounds.Length == 0 ? null : (bounds.Select(b => b.Min).Aggregate(Vector3.Min), bounds.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    internal static (Vector3 Min, Vector3 Max)? Bounds(object item) => item switch
    {
        MapBrush brush => brush.GetBounds(),
        MapTerrain terrain when terrain.Vertices.Length > 0 => terrain.GetBounds(),
        MapEntity entity => EditorSession.EntityBounds(entity),
        BrushFaceSelection face => FaceBounds(face),
        BrushVertexSelection vertex => (vertex.Position, vertex.Position),
        TerrainVertexSelection vertex when (uint)vertex.Index < vertex.Terrain.Vertices.Length =>
            (vertex.Terrain.Vertices[vertex.Index], vertex.Terrain.Vertices[vertex.Index]),
        _ => null
    };

    private static (Vector3 Min, Vector3 Max)? FaceBounds(BrushFaceSelection face)
    {
        Vector3[]? vertices = face.Brush.GetPolygons().FirstOrDefault(polygon => ReferenceEquals(polygon.Face, face.Face))?.Vertices;
        return vertices is not { Length: > 0 } ? null : (vertices.Aggregate(Vector3.Min), vertices.Aggregate(Vector3.Max));
    }

    internal static bool CanTransform(object item) => item is MapBrush or MapTerrain or BrushVertexSelection or TerrainVertexSelection ||
        item is MapEntity entity && entity.ClassName != "worldspawn" && entity.PreservedPrimitives.Count == 0;
}
