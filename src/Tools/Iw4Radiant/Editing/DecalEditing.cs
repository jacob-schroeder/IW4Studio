using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class DecalEditing
{
    internal static void Project(EditorSession session, float width, float height, float rotation, Vector2 offset,
        Func<string, bool> supportsAlpha)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0 ||
            !float.IsFinite(rotation) || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            throw new ArgumentException("Decal dimensions must be positive and all projection values must be finite.");
        if (!supportsAlpha(session.Material))
            throw new ArgumentException("Choose a material with supported alpha blending in the material browser before projecting a decal.");
        BrushFaceSelection[] faces = session.Selection.Items.OfType<BrushFaceSelection>().ToArray();
        if (faces.Length == 0 || faces.Length != session.Selection.Count)
            throw new ArgumentException("Use Face mode to select the brush faces that should receive the decal.");
        var additions = new List<(MapEntity Owner, MapTerrain Mesh)>();
        foreach (BrushFaceSelection selected in faces)
        {
            MapPolygon? polygon = selected.Brush.GetPolygons().FirstOrDefault(face => ReferenceEquals(face.Face, selected.Face));
            if (polygon is null) throw new ArgumentException("A selected face no longer has valid geometry.");
            MapEntity owner = session.Document.Entities.First(entity => entity.Brushes.Contains(selected.Brush));
            foreach (MapTerrain mesh in ProjectFace(polygon, width, height, rotation, offset, session.Material))
            {
                mesh.Directives.AddRange(selected.Brush.Directives);
                additions.Add((owner, mesh));
            }
        }
        if (additions.Count == 0)
            throw new ArgumentException("The decal rectangle does not overlap the selected faces. Reduce the offsets or enlarge the decal.");
        session.Edit(() =>
        {
            foreach (var addition in additions) addition.Owner.Terrains.Add(addition.Mesh);
            session.Selection.SetRange(additions.Select(addition => addition.Mesh));
            session.Tool = EditorTool.Select;
        });
    }

    private static IEnumerable<MapTerrain> ProjectFace(MapPolygon polygon, float width, float height, float rotation,
        Vector2 offset, string material)
    {
        Vector3 normal = polygon.Face.Normal;
        Vector3 axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 basisU = Vector3.Normalize(Vector3.Cross(axis, normal)), basisV = Vector3.Cross(normal, basisU);
        float angle = rotation % 360 * MathF.PI / 180;
        Vector3 u = basisU * MathF.Cos(angle) + basisV * MathF.Sin(angle);
        Vector3 v = -basisU * MathF.Sin(angle) + basisV * MathF.Cos(angle);
        Vector3 center = polygon.Vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point / polygon.Vertices.Length) +
            u * offset.X + v * offset.Y;
        List<Vector2> boundary = polygon.Vertices.Select(point =>
            new Vector2(Vector3.Dot(point - center, u), Vector3.Dot(point - center, v))).ToList();
        boundary = Clip(boundary, point => point.X + width / 2);
        boundary = Clip(boundary, point => width / 2 - point.X);
        boundary = Clip(boundary, point => point.Y + height / 2);
        boundary = Clip(boundary, point => height / 2 - point.Y);
        if (boundary.Count < 3) yield break;
        for (int index = 1; index < boundary.Count - 1; index++)
        {
            Vector2 a = boundary[0], b = boundary[index], c = boundary[index + 1];
            if (MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) < 0.0001f) continue;
            // A collapsed corner represents each clipped triangle using native 2 × 2 mesh topology.
            Vector2[] points = [a, c, b, c];
            var mesh = new MapTerrain
            {
                Width = 2, Height = 2, Material = material,
                Vertices = points.Select(point => center + u * point.X + v * point.Y).ToArray(),
                TextureCoordinates = points.Select(point => new Vector2(point.X / width + 0.5f, point.Y / height + 0.5f)).ToArray(),
                LightmapCoordinates = points.Select(point => new Vector2(point.X / width + 0.5f, point.Y / height + 0.5f)).ToArray(),
                Colors = Enumerable.Repeat(Vector4.One, 4).ToArray(), EdgeFlags = new int[4]
            };
            yield return mesh;
        }
    }

    private static List<Vector2> Clip(List<Vector2> polygon, Func<Vector2, float> distance)
    {
        var clipped = new List<Vector2>();
        if (polygon.Count == 0) return clipped;
        Vector2 previous = polygon[^1];
        float previousDistance = distance(previous);
        foreach (Vector2 current in polygon)
        {
            float currentDistance = distance(current);
            bool previousInside = previousDistance >= 0, currentInside = currentDistance >= 0;
            if (previousInside != currentInside)
                clipped.Add(Vector2.Lerp(previous, current, previousDistance / (previousDistance - currentDistance)));
            if (currentInside) clipped.Add(current);
            previous = current;
            previousDistance = currentDistance;
        }
        return clipped;
    }
}
