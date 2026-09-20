using System.Numerics;
using Iw4Radiant.Materials;

namespace Iw4Radiant.MapSource;

// Static water/solid intersections. The same contact distance reaches both GPUs
// in vertex alpha; it is not recomputed while the water animates.
internal sealed class WaterShoreGeometry
{
    internal const float Width = 24;
    internal const float MeshSpacing = Width / 4;
    private const float Epsilon = 0.001f;
    private readonly List<Vector3[]> _surfaces = [];

    internal WaterShoreGeometry(MapDocument document, Func<string, MaterialSource?> resolveMaterial)
    {
        foreach (MapEntity entity in document.Entities.Where(entity => entity.ClassName is "worldspawn" or "func_group"))
        {
            foreach (MapBrush brush in entity.Brushes)
            {
                if (!BrushContents.BlocksPlayer(BrushContents.Read(brush)) ||
                    brush.Faces.Any(face => resolveMaterial(face.Material)?.IsWater == true)) continue;
                foreach (MapPolygon polygon in brush.GetPolygons())
                    if (VisibleSolid(polygon.Face.Material)) _surfaces.Add(polygon.Vertices);
            }
            foreach (MapTerrain terrain in entity.Terrains)
            {
                if (TerrainContents.ReadNonColliding(terrain) || !VisibleSolid(terrain.Material)) continue;
                MapTerrain surface = terrain.GetSurface();
                foreach (var (a, b, c) in surface.GetTriangles())
                    _surfaces.Add([surface.Vertices[a], surface.Vertices[b], surface.Vertices[c]]);
            }
        }

        bool VisibleSolid(string name) => !ClipBrushMaterial.IsPlayerClip(name) && !CaulkMaterial.IsCaulk(name) &&
            resolveMaterial(name) is { IsWater: false, IsSky: false, Surface.IsBlended: false };
    }

    internal (Vector3 A, Vector3 B)[] Contacts(MapPolygon water)
    {
        var result = new List<(Vector3, Vector3)>();
        Vector3 normal = water.Face.Normal;
        Vector3 center = water.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) / water.Vertices.Length;
        float plane = Vector3.Dot(normal, water.Vertices[0]);
        foreach (Vector3[] surface in _surfaces)
        {
            float[] distances = surface.Select(point => Vector3.Dot(normal, point) - plane).ToArray();
            if (distances.All(distance => MathF.Abs(distance) <= Epsilon) ||
                distances.All(distance => distance > Epsilon) || distances.All(distance => distance < -Epsilon)) continue;
            var points = new List<Vector3>();
            for (int i = 0; i < surface.Length; i++)
            {
                int next = (i + 1) % surface.Length;
                if (MathF.Abs(distances[i]) <= Epsilon) Add(surface[i] - normal * distances[i]);
                if (distances[i] < -Epsilon && distances[next] > Epsilon ||
                    distances[i] > Epsilon && distances[next] < -Epsilon)
                    Add(Vector3.Lerp(surface[i], surface[next], distances[i] / (distances[i] - distances[next])));
            }
            if (points.Count < 2) continue;
            // Convex brush faces and terrain triangles intersect a plane in a segment.
            Vector3 a = points[0], b = points.OrderByDescending(point => Vector3.DistanceSquared(a, point)).First();
            float begin = 0, end = 1;
            for (int i = 0; i < water.Vertices.Length && begin <= end; i++)
            {
                Vector3 edge = water.Vertices[i];
                Vector3 inward = Vector3.Normalize(Vector3.Cross(normal, water.Vertices[(i + 1) % water.Vertices.Length] - edge));
                if (Vector3.Dot(inward, center - edge) < 0) inward = -inward;
                float da = Vector3.Dot(inward, a - edge), db = Vector3.Dot(inward, b - edge);
                if (da < -Epsilon && db < -Epsilon) { begin = 1; end = 0; break; }
                if (da < -Epsilon) begin = Math.Max(begin, da / (da - db));
                else if (db < -Epsilon) end = Math.Min(end, da / (da - db));
            }
            if (begin <= end)
            {
                Vector3 start = Vector3.Lerp(a, b, begin), finish = Vector3.Lerp(a, b, end);
                if (Vector3.DistanceSquared(start, finish) > Epsilon * Epsilon) result.Add((start, finish));
            }

            void Add(Vector3 point)
            {
                if (points.All(existing => Vector3.DistanceSquared(point, existing) > Epsilon * Epsilon)) points.Add(point);
            }
        }
        return result.ToArray();
    }

    internal static float Distance(Vector3 point, IReadOnlyList<(Vector3 A, Vector3 B)> contacts) =>
        Distance(point, contacts, out _);

    internal static float Distance(Vector3 point, IReadOnlyList<(Vector3 A, Vector3 B)> contacts, out Vector3 gradient)
    {
        float distance = float.PositiveInfinity;
        gradient = Vector3.Zero;
        foreach (var (a, b) in contacts)
        {
            Vector3 edge = b - a;
            float t = Math.Clamp(Vector3.Dot(point - a, edge) / edge.LengthSquared(), 0, 1);
            Vector3 offset = point - (a + edge * t);
            float candidate = offset.LengthSquared();
            if (candidate < distance) { distance = candidate; gradient = offset; }
        }
        distance = MathF.Sqrt(distance);
        if (distance > Epsilon && float.IsFinite(distance)) gradient /= distance;
        else gradient = Vector3.Zero;
        return distance;
    }

    internal static float VertexAlpha(Vector3 point, IReadOnlyList<(Vector3 A, Vector3 B)> contacts) =>
        MathF.Round(Math.Clamp(Distance(point, contacts) / Width, 0, 1) * 255) / 255;
}
