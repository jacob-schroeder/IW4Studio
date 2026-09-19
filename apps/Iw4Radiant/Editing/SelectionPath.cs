using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal readonly record struct SelectionPath(int Entity, int Brush = -1, int Terrain = -1,
    int Face = -1, Vector3? BrushVertex = null, int TerrainVertex = -1)
{
    internal object? Resolve(MapDocument document)
    {
        if ((uint)Entity >= document.Entities.Count) return null;
        MapEntity entity = document.Entities[Entity];
        if (Brush >= 0)
        {
            if (Brush >= entity.Brushes.Count) return null;
            MapBrush brush = entity.Brushes[Brush];
            if (Face >= 0) return Face < brush.Faces.Count ? new BrushFaceSelection(brush, brush.Faces[Face]) : null;
            return BrushVertex is { } point ? new BrushVertexSelection(brush, point) : brush;
        }
        if (Terrain >= 0)
        {
            if (Terrain >= entity.Terrains.Count) return null;
            MapTerrain terrain = entity.Terrains[Terrain];
            return TerrainVertex >= 0
                ? TerrainVertex < terrain.Vertices.Length ? new TerrainVertexSelection(terrain, TerrainVertex) : null
                : terrain;
        }
        return entity;
    }
}
