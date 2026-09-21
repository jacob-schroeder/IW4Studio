using System.Numerics;
using IW4.AssetExchange.SourceFormat.Material;
using IW4.Assets.Assets.GfxMap;

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
        IReadOnlyList<(Vector3 A, Vector3 B)>? shore = null, WaterShoreGeometry? bed = null)
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
        IEnumerable<MapPolygon> joined = shore is { Count: > 0 } || ocean is not null ? JoinEdges(tiles) : tiles;
        if (ocean is not null && bed is not null && IsTop(polygon))
            joined = RefineBedInterpolation(joined, ocean, bed);
        // Native displaced draws must bypass the SPU's rest-position clipper.
        // Refine real small meshes; the compiler merges their patches into one
        // draw above the native threshold. No dummy vertices or CPU animation.
        if (ocean is { Height: > 0 } && IsTop(polygon))
            joined = RefineGpuClippedMesh(joined);
        foreach (MapPolygon tile in joined) yield return tile;

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
            bool nearContact = shore is { Count: > 0 } &&
                WaterShoreGeometry.Distance(center, shore) < WaterShoreGeometry.Width + radius;
            bool shallow = false;
            if (ocean is not null && bed is not null && IsTop(polygon))
            {
                float minimumDepth = float.PositiveInfinity, maximumDepth = float.NegativeInfinity;
                foreach (Vector3 point in clipped.Append(center))
                {
                    float depth = bed.Depth(point, ocean.Height, out _);
                    minimumDepth = Math.Min(minimumDepth, depth);
                    maximumDepth = Math.Max(maximumDepth, depth);
                }
                float quantization = (ocean.DepthRange + ocean.Height) / 510;
                float foamDepth = ocean.ShoreDepth + quantization;
                float activeDepth = Math.Min(2 * foamDepth + ocean.SwashDepth, foamDepth + ocean.Height);
                shallow = minimumDepth <= activeDepth && maximumDepth >= -ocean.SwashDepth - quantization;
            }
            if ((nearContact || shallow) && Math.Max(right - left, top - bottom) > WaterShoreGeometry.MeshSpacing)
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
            if (ocean is not null && bed is not null && shallow)
            {
                var tile = new MapPolygon(polygon.Face, clipped);
                if (BedInterpolationError(tile, ocean, bed) > ocean.DepthRange / 255)
                {
                    var boundary = new List<Vector3>(clipped.Length * 2);
                    for (int i = 0; i < clipped.Length; i++)
                    {
                        boundary.Add(clipped[i]);
                        boundary.Add(Interpolate(clipped[i], clipped[(i + 1) % clipped.Length], 0.5f));
                    }
                    for (int i = 0; i < boundary.Count; i++)
                        yield return new(polygon.Face, [boundary[i], boundary[(i + 1) % boundary.Count], center]);
                    yield break;
                }
            }
            if (ocean is not null && IsTop(polygon) && clipped.All(point => VertexColor(polygon, ocean, point).X == 0))
            {
                // Thin or triangular tops can have only pinned grid intersections.
                // A convex cell's centroid supplies an interior displacement sample.
                for (int i = 0; i < clipped.Length; i++)
                    yield return new(polygon.Face, [clipped[i], clipped[(i + 1) % clipped.Length], center]);
            }
            else yield return new(polygon.Face, clipped);
        }
    }

    private static IEnumerable<MapPolygon> RefineBedInterpolation(IEnumerable<MapPolygon> source,
        OceanWaveSettings ocean, WaterShoreGeometry bed)
    {
        foreach (MapPolygon tile in source)
        {
            if (BedInterpolationError(tile, ocean, bed) <= ocean.DepthRange / 255)
            {
                yield return tile;
                continue;
            }
            Vector3 center = tile.Vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point) / tile.Vertices.Length;
            for (int i = 0; i < tile.Vertices.Length; i++)
                yield return new(tile.Face, [tile.Vertices[i], tile.Vertices[(i + 1) % tile.Vertices.Length], center]);
        }
    }

    private static float BedInterpolationError(MapPolygon polygon, OceanWaveSettings ocean, WaterShoreGeometry bed)
    {
        float quantization = ocean.DepthRange / 255;
        float minimumDepth = -ocean.SwashDepth - quantization;
        float maximumDepth = ocean.ShoreDepth + quantization;
        float error = 0;
        for (int i = 1; i < polygon.Vertices.Length - 1; i++)
        {
            Vector3[] triangle = [polygon.Vertices[0], polygon.Vertices[i], polygon.Vertices[i + 1]];
            float[] values = triangle.Select(BakedDepth).ToArray();
            foreach (Vector3 weights in new[] { new Vector3(1f / 3), new(.5f, .25f, .25f),
                         new Vector3(.25f, .5f, .25f), new Vector3(.25f, .25f, .5f),
                         new Vector3(.5f, .5f, 0), new Vector3(.5f, 0, .5f), new Vector3(0, .5f, .5f) })
            {
                Vector3 point = triangle[0] * weights.X + triangle[1] * weights.Y + triangle[2] * weights.Z;
                float actual = RawDepth(point);
                if (actual < minimumDepth || actual > maximumDepth) continue;
                float interpolated = values[0] * weights.X + values[1] * weights.Y + values[2] * weights.Z;
                error = Math.Max(error, Math.Abs(actual - interpolated));
            }
        }
        return error;

        float RawDepth(Vector3 point) => Math.Clamp(bed.Depth(point, ocean.Height, out _),
            -ocean.Height, ocean.DepthRange - ocean.Height);
        float BakedDepth(Vector3 point)
        {
            return EncodeDepth(ocean, RawDepth(point)) * ocean.DepthRange - ocean.Height;
        }
    }

    private static IEnumerable<MapPolygon> RefineGpuClippedMesh(IEnumerable<MapPolygon> source)
    {
        var queue = new PriorityQueue<MapPolygon, (float Area, int Order)>();
        int count = 0, order = 0;
        foreach (MapPolygon tile in source) Add(tile);
        while (queue.Count > 0 && count <= SrfTriangles.SoftwareTriangleCullVertexLimit)
        {
            MapPolygon tile = queue.Dequeue();
            count -= tile.Vertices.Length;
            Vector3 center = tile.Vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point) / tile.Vertices.Length;
            for (int corner = 0; corner < tile.Vertices.Length; corner++)
                Add(new(tile.Face, [tile.Vertices[corner], tile.Vertices[(corner + 1) % tile.Vertices.Length], center]));
        }
        return queue.UnorderedItems.OrderBy(item => item.Priority.Order).Select(item => item.Element);

        void Add(MapPolygon tile)
        {
            float area = 0;
            for (int corner = 1; corner < tile.Vertices.Length - 1; corner++)
                area += Vector3.Cross(tile.Vertices[corner] - tile.Vertices[0], tile.Vertices[corner + 1] - tile.Vertices[0]).Z;
            queue.Enqueue(tile, (-Math.Abs(area), order++));
            count += tile.Vertices.Length;
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
                    Vector3 point = Interpolate(a, b, (coordinate - start) / (end - start));
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
        WaterShoreGeometry? shore = null)
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
        float edge = Math.Clamp(distance / ocean.FadeWidth, 0, 1);
        float perimeterWeight = edge * edge * (3 - 2 * edge);
        Vector2 perimeterGradient = gradient * (6 * edge * (1 - edge) / ocean.FadeWidth);
        float depthWeight = 1;
        Vector2 depthGradient = Vector2.Zero;
        float depth = shore?.Depth(point, ocean.Height, out depthGradient) ?? float.PositiveInfinity;
        if (ocean.Height > 0)
        {
            // A small swash remains at the resting shoreline. Away from that band,
            // even the deepest trough retains water above the bed.
            float shallow = Math.Clamp((depth + ocean.SwashDepth) / (2 * ocean.Height), 0, 1);
            depthWeight = shallow * shallow * (3 - 2 * shallow);
            depthGradient *= 6 * shallow * (1 - shallow) / (2 * ocean.Height);
        }
        float weight = perimeterWeight * depthWeight;
        gradient = perimeterGradient * depthWeight + depthGradient * perimeterWeight;
        gradient /= ocean.GradientScale;
        // Quantize exactly as the native vertex color stream, including its zero-gradient byte.
        return new(MathF.Round(weight * 255) / 255,
            MathF.Round(128 + 127 * Math.Clamp(gradient.X, -1, 1)) / 255,
            MathF.Round(128 + 127 * Math.Clamp(gradient.Y, -1, 1)) / 255,
            EncodeDepth(ocean, depth));
    }

    private static float EncodeDepth(OceanWaveSettings ocean, float depth) =>
        MathF.Round(Math.Clamp((depth + ocean.Height) / ocean.DepthRange, 0, 1) * 255) / 255;

    private static Vector3 Interpolate(Vector3 a, Vector3 b, float amount)
    {
        Vector3 point = Vector3.Lerp(a, b, amount);
        if (a.X == b.X) point.X = a.X;
        if (a.Y == b.Y) point.Y = a.Y;
        if (a.Z == b.Z) point.Z = a.Z;
        return point;
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
                Vector3 intersection = Interpolate(previous, point, previousDistance / (previousDistance - distance));
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
