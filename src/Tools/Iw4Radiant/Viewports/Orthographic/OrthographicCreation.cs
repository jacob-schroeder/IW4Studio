using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Orthographic;

internal static class OrthographicCreation
{
    internal static object Create(EditorSession session, OrthographicProjection projection, Vector2 min, Vector2 max, bool terrain)
    {
        if (!terrain)
        {
            float bottom = session.Snap(session.BrushBottom);
            float top = Math.Max(bottom + session.GridSize, session.Snap(bottom + session.BrushHeight));
            var brush = MapBrush.CreateBox(projection.Unproject(min, bottom), projection.Unproject(max, top), session.Material);
            session.Document.World.Brushes.Add(brush);
            return brush;
        }
        int count = Math.Clamp(session.TerrainVertices, 2, 16);
        float spacing = (max.X - min.X) / (count - 1);
        var patch = MapTerrain.Create(new Vector3(min.X, min.Y, session.BrushBottom), spacing, count, session.Material);
        for (int x = 0; x < count; x++)
        for (int y = 0; y < count; y++)
        {
            int index = x * count + y;
            float offset = y * (max.Y - min.Y) / (count - 1);
            patch.Vertices[index].Y = min.Y + offset;
            patch.TextureCoordinates[index].Y = offset / 128;
        }
        session.Document.World.Terrains.Add(patch);
        return patch;
    }
}
