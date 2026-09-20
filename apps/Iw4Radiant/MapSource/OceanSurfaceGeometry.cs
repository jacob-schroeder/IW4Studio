using System.Numerics;
using IW4.AssetExchange.SourceFormat.Material;

namespace Iw4Radiant.MapSource;

// Static subdivision and edge weights, shared by the editor and native world writer.
// Positions remain at rest; both vertex shaders evaluate the waves on the GPU.
internal static class OceanSurfaceGeometry
{
    internal static bool IsTop(MapPolygon polygon) => polygon.Face.Normal.Z > 0.999999f;

    // A water brush describes the occupied volume. Its upper hull is the visible
    // liquid surface; drawing the buried sides creates sheets between adjacent pools.
    internal static bool IsVisibleSurface(MapPolygon polygon, bool water) => !water || polygon.Face.Normal.Z > 0.000001f;

    internal static IEnumerable<MapPolygon> Subdivide(MapPolygon polygon, OceanWaveSettings? ocean,
        IReadOnlyList<(Vector3 A, Vector3 B)>? shore = null)
    {
        if ((ocean is null || !IsTop(polygon)) && shore is not { Count: > 0 })
        {
            yield return polygon;
            yield break;
        }
        Vector3 minimum = polygon.Vertices.Aggregate(Vector3.Min), maximum = polygon.Vertices.Aggregate(Vector3.Max);
        int columns = ocean is not null && IsTop(polygon)
            ? Math.Max(2, checked((int)MathF.Ceiling((maximum.X - minimum.X) / ocean.MeshSpacing))) : 1;
        int rows = ocean is not null && IsTop(polygon)
            ? Math.Max(2, checked((int)MathF.Ceiling((maximum.Y - minimum.Y) / ocean.MeshSpacing))) : 1;
        if (columns < 1 || rows < 1 || (long)columns * rows > 4096)
            throw new InvalidDataException("An ocean surface requires more than 4096 mesh cells. Increase its wavelength or divide the water brush.");
        int cells = 0;
        var tiles = new List<MapPolygon>();
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            float left = minimum.X + (maximum.X - minimum.X) * x / columns;
            float right = minimum.X + (maximum.X - minimum.X) * (x + 1) / columns;
            float bottom = minimum.Y + (maximum.Y - minimum.Y) * y / rows;
            float top = minimum.Y + (maximum.Y - minimum.Y) * (y + 1) / rows;
            tiles.AddRange(Cell(polygon.Vertices, left, right, bottom, top));
        }
        foreach (MapPolygon tile in shore is { Count: > 0 } ? JoinEdges(tiles) : tiles) yield return tile;

        IEnumerable<MapPolygon> Cell(Vector3[] vertices, float left, float right, float bottom, float top)
        {
            Vector3[] clipped = Clip(Clip(Clip(Clip(vertices, 0, left, true),
                0, right, false), 1, bottom, true), 1, top, false);
            if (clipped.Length < 3) yield break;
            double area = 0;
            for (int i = 1; i < clipped.Length - 1; i++)
                area += Vector3.Cross(clipped[i] - clipped[0], clipped[i + 1] - clipped[0]).Z;
            if (Math.Abs(area) <= 0.000001) yield break;
            Vector3 center = clipped.Aggregate(Vector3.Zero, (sum, point) => sum + point) / clipped.Length;
            float radius = MathF.Sqrt(clipped.Max(point => Vector3.DistanceSquared(point, center)));
            if (shore is { Count: > 0 } && Math.Max(right - left, top - bottom) > WaterShoreGeometry.MeshSpacing &&
                WaterShoreGeometry.Distance(center, shore) < WaterShoreGeometry.Width + radius)
            {
                float middleX = (left + right) * 0.5f, middleY = (bottom + top) * 0.5f;
                foreach (var bounds in new[] { (left, middleX, bottom, middleY), (middleX, right, bottom, middleY),
                             (left, middleX, middleY, top), (middleX, right, middleY, top) })
                    foreach (MapPolygon tile in Cell(clipped, bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4))
                        yield return tile;
                yield break;
            }
            if (++cells > 4096)
                throw new InvalidDataException("A water surface requires more than 4096 mesh cells. Divide the water brush to simplify its shoreline.");
            if (ocean is not null && IsTop(polygon) && clipped.All(point => VertexColor(polygon, ocean, point, shore).X == 0))
            {
                // Thin or triangular tops can have only pinned grid intersections.
                // A convex cell's centroid supplies an interior displacement sample.
                for (int i = 0; i < clipped.Length; i++)
                    yield return new(polygon.Face, [clipped[i], clipped[(i + 1) % clipped.Length], center]);
            }
            else yield return new(polygon.Face, clipped);
        }
    }

    private static IEnumerable<MapPolygon> JoinEdges(IReadOnlyList<MapPolygon> tiles)
    {
        // Refinement must not leave T-junctions: independently displaced coarse
        // and fine edges would open cracks as their GPU waves move.
        Vector3[] points = tiles.SelectMany(tile => tile.Vertices).Distinct().ToArray();
        var horizontal = points.GroupBy(point => point.Y).ToDictionary(group => group.Key,
            group => new SortedSet<float>(group.Select(point => point.X)));
        var vertical = points.GroupBy(point => point.X).ToDictionary(group => group.Key,
            group => new SortedSet<float>(group.Select(point => point.Y)));
        foreach (MapPolygon tile in tiles)
        {
            var boundary = new List<Vector3>();
            for (int i = 0; i < tile.Vertices.Length; i++)
            {
                Vector3 a = tile.Vertices[i], b = tile.Vertices[(i + 1) % tile.Vertices.Length];
                boundary.Add(a);
                bool alongX = a.Y == b.Y;
                if (!alongX && a.X != b.X) continue;
                float start = alongX ? a.X : a.Y, end = alongX ? b.X : b.Y;
                if (start == end) continue;
                var coordinates = (alongX ? horizontal[a.Y] : vertical[a.X])
                    .GetViewBetween(Math.Min(start, end), Math.Max(start, end));
                foreach (float coordinate in start < end ? coordinates : coordinates.Reverse())
                {
                    if (coordinate == start || coordinate == end) continue;
                    Vector3 point = Vector3.Lerp(a, b, (coordinate - start) / (end - start));
                    if (alongX) point.X = coordinate; else point.Y = coordinate;
                    boundary.Add(point);
                }
            }
            if (boundary.Count == tile.Vertices.Length) { yield return tile; continue; }
            Vector3 center = tile.Vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point) / tile.Vertices.Length;
            for (int i = 0; i < boundary.Count; i++)
                yield return new(tile.Face, [boundary[i], boundary[(i + 1) % boundary.Count], center]);
        }
    }

    internal static Vector4 VertexColor(MapPolygon boundary, OceanWaveSettings ocean, Vector3 point,
        IReadOnlyList<(Vector3 A, Vector3 B)>? shore = null)
    {
        if (!IsTop(boundary)) return new(0, 128f / 255, 128f / 255, 1);
        Vector3 center = boundary.Vertices.Aggregate(Vector3.Zero, (sum, p) => sum + p) / boundary.Vertices.Length;
        float distance = float.PositiveInfinity;
        Vector2 gradient = Vector2.Zero;
        for (int i = 0; i < boundary.Vertices.Length; i++)
        {
            Vector3 a = boundary.Vertices[i], b = boundary.Vertices[(i + 1) % boundary.Vertices.Length];
            Vector2 inward = Vector2.Normalize(new Vector2(a.Y - b.Y, b.X - a.X));
            if (Vector2.Dot(inward, new Vector2(center.X - a.X, center.Y - a.Y)) < 0) inward = -inward;
            float candidate = Vector2.Dot(inward, new Vector2(point.X - a.X, point.Y - a.Y));
            if (candidate < distance) { distance = candidate; gradient = inward; }
        }
        if (shore is { Count: > 0 })
        {
            float contactDistance = WaterShoreGeometry.Distance(point, shore, out Vector3 contactGradient);
            if (contactDistance < distance)
            {
                distance = contactDistance;
                gradient = new(contactGradient.X, contactGradient.Y);
            }
        }
        float weight = Math.Clamp(distance / ocean.FadeWidth, 0, 1);
        if (distance >= ocean.FadeWidth) gradient = Vector2.Zero;
        // Quantize exactly as the native vertex color stream, including its zero-gradient byte.
        return new(MathF.Round(weight * 255) / 255,
            MathF.Round(128 + 127 * gradient.X) / 255, MathF.Round(128 + 127 * gradient.Y) / 255, 1);
    }

    private static Vector3[] Clip(Vector3[] points, int axis, float limit, bool keepGreater)
    {
        if (points.Length == 0) return points;
        var result = new List<Vector3>();
        Vector3 previous = points[^1];
        float previousDistance = Distance(previous);
        foreach (Vector3 point in points)
        {
            float distance = Distance(point);
            if ((distance >= 0) != (previousDistance >= 0))
            {
                Vector3 intersection = Vector3.Lerp(previous, point, previousDistance / (previousDistance - distance));
                if (axis == 0) intersection.X = limit; else intersection.Y = limit;
                Add(intersection);
            }
            if (distance >= 0) Add(point);
            previous = point;
            previousDistance = distance;
        }
        if (result.Count > 1 && result[0] == result[^1]) result.RemoveAt(result.Count - 1);
        return result.ToArray();

        float Distance(Vector3 point) => ((axis == 0 ? point.X : point.Y) - limit) * (keepGreater ? 1 : -1);
        void Add(Vector3 point) { if (result.Count == 0 || result[^1] != point) result.Add(point); }
    }
}
