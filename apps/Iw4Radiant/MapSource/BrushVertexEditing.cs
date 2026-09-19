using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class BrushVertexEditing
{
    internal static void MoveVertices(MapBrush brush, IReadOnlyDictionary<Vector3, Vector3> moves, bool textureLock = false)
    {
        if (moves.Count == 0) return;
        BrushGeometry.Validate(brush);
        var before = brush.GetVertices();
        Vector3[] after = before.ToArray();
        HashSet<int> moved = [];
        foreach (var (original, destination) in moves)
        {
            int index = BrushGeometry.FindVertex(before, original);
            if (index < 0 || !BrushGeometry.IsFinite(destination) || !moved.Add(index))
                throw new ArgumentException("Vertex edits require distinct existing vertices and finite destinations.");
            after[index] = destination;
        }
        for (int a = 0; a < after.Length; a++)
        for (int b = a + 1; b < after.Length; b++)
            if (Vector3.DistanceSquared(after[a], after[b]) < BrushGeometry.PointTolerance * BrushGeometry.PointTolerance)
                throw new ArgumentException("The edit would collapse two brush vertices.");

        var triangles = BuildHull(after);
        List<MapFace> planes = [];
        foreach (var triangle in triangles)
        {
            // Hull triangles use outward counterclockwise winding; map plane points
            // use the opposite order (Cross(A - B, C - B)).
            var plane = new MapFace { A = after[triangle.A], B = after[triangle.C], C = after[triangle.B] };
            Vector3 normal = plane.Normal;
            double distance = BrushGeometry.Dot(normal, plane.A);
            if (planes.Any(existing => BrushGeometry.Dot(existing.Normal, normal) > 0.999999 &&
                    Math.Abs(BrushGeometry.Dot(normal, existing.A) - distance) <= BrushGeometry.PlaneTolerance &&
                    Math.Abs(BrushGeometry.Dot(normal, existing.B) - distance) <= BrushGeometry.PlaneTolerance &&
                    Math.Abs(BrushGeometry.Dot(normal, existing.C) - distance) <= BrushGeometry.PlaneTolerance))
                continue;
            planes.Add(plane);
        }
        var sourceFaces = brush.GetPolygons().Select(polygon => (polygon.Face,
            Vertices: polygon.Vertices.Select(point => BrushGeometry.FindVertex(before, point)).ToHashSet())).ToArray();
        var candidate = new MapBrush();
        foreach (var plane in planes)
        {
            Vector3 normal = plane.Normal;
            double distance = BrushGeometry.Dot(normal, plane.A);
            int[] vertices = Enumerable.Range(0, after.Length).Where(index =>
                Math.Abs(BrushGeometry.Dot(normal, after[index]) - distance) <= BrushGeometry.PlaneTolerance).ToArray();
            var source = sourceFaces.OrderByDescending(sourceFace => vertices.Count(sourceFace.Vertices.Contains))
                .ThenByDescending(sourceFace => BrushGeometry.Dot(sourceFace.Face.Normal, normal)).First().Face;
            var face = source.Clone();
            face.A = plane.A;
            face.B = plane.B;
            face.C = plane.C;
            if (textureLock)
                face.Projection = SurfaceProjection.Parse(source.Projection).Reproject(source.Normal, normal,
                    vertices.Select(index => before[index]).ToArray(), vertices.Select(index => after[index]).ToArray()).Format();
            candidate.Faces.Add(face);
        }
        var actual = candidate.GetVertices();
        if (after.Any(point => BrushGeometry.FindVertex(actual, point) < 0) ||
            actual.Any(point => BrushGeometry.FindVertex(after, point) < 0))
            throw new ArgumentException("The edit would hide a vertex inside the brush or change its requested position.");
        brush.ReplaceFaces(candidate);
    }

    private static List<(int A, int B, int C)> BuildHull(IReadOnlyList<Vector3> points)
    {
        int first = 0;
        int second = Enumerable.Range(1, points.Count - 1).MaxBy(index => Vector3.DistanceSquared(points[first], points[index]));
        Vector3 direction = points[second] - points[first];
        int third = Enumerable.Range(0, points.Count).MaxBy(index =>
            Vector3.Cross(direction, points[index] - points[first]).LengthSquared());
        Vector3 baseNormal = BrushGeometry.FaceNormal(points[first], points[third], points[second]);
        if (!BrushGeometry.IsFinite(baseNormal))
            throw new ArgumentException("The edit would flatten the brush into a line.");
        int fourth = Enumerable.Range(0, points.Count).MaxBy(index =>
            Math.Abs(BrushGeometry.Dot(baseNormal, points[index] - points[first])));
        if (Math.Abs(BrushGeometry.Dot(baseNormal, points[fourth] - points[first])) <= BrushGeometry.PlaneTolerance)
            throw new ArgumentException("The edit would flatten the brush into a plane.");
        Vector3 interior = points[first] + ((points[second] - points[first]) +
            (points[third] - points[first]) + (points[fourth] - points[first])) / 4;
        List<(int A, int B, int C)> triangles = [];
        AddTriangle(first, second, third);
        AddTriangle(first, fourth, second);
        AddTriangle(second, fourth, third);
        AddTriangle(third, fourth, first);
        for (int point = 0; point < points.Count; point++)
        {
            if (point == first || point == second || point == third || point == fourth) continue;
            HashSet<int> visible = [];
            var horizon = new Dictionary<(int, int), (int A, int B, int Count)>();
            for (int index = 0; index < triangles.Count; index++)
            {
                var triangle = triangles[index];
                Vector3 normal = BrushGeometry.FaceNormal(points[triangle.A], points[triangle.C], points[triangle.B]);
                if (BrushGeometry.Dot(normal, points[point] - points[triangle.A]) <= BrushGeometry.PlaneTolerance)
                    continue;
                visible.Add(index);
                AddEdge(triangle.A, triangle.B);
                AddEdge(triangle.B, triangle.C);
                AddEdge(triangle.C, triangle.A);
            }
            if (visible.Count == 0) continue;
            triangles = triangles.Where((_, index) => !visible.Contains(index)).ToList();
            foreach (var edge in horizon.Values)
                if (edge.Count == 1) AddTriangle(edge.A, edge.B, point);

            void AddEdge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                horizon.TryGetValue(key, out var existing);
                horizon[key] = (a, b, existing.Count + 1);
            }
        }
        return triangles;

        void AddTriangle(int a, int b, int c)
        {
            Vector3 normal = BrushGeometry.FaceNormal(points[a], points[c], points[b]);
            if (!BrushGeometry.IsFinite(normal))
                return; // Extending a coplanar hull edge adds no triangle area here.
            if (BrushGeometry.Dot(normal, interior - points[a]) > 0) (b, c) = (c, b);
            triangles.Add((a, b, c));
        }
    }
}
