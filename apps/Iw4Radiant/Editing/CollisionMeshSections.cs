using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class CollisionMeshSections
{
    internal static Vector3[][] Split(Vector3[] points, int[] triangles)
    {
        if (triangles.Length < 6 || triangles.Length % 3 != 0)
            return [points];

        // Native surfaces can repeat a position at a material or UV seam. Those
        // vertices still describe the same geometric edge of the collision mesh.
        var positions = new Dictionary<Vector3, int>();
        int[] welded = new int[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            if (!positions.TryGetValue(points[index], out int vertex))
                positions.Add(points[index], vertex = positions.Count);
            welded[index] = vertex;
        }

        var edges = new Dictionary<(int A, int B), List<int>>();
        var shortest = new (int A, int B)[triangles.Length / 3];
        for (int triangle = 0; triangle < shortest.Length; triangle++)
        {
            int first = triangles[triangle * 3], second = triangles[triangle * 3 + 1],
                third = triangles[triangle * 3 + 2];
            if ((uint)first >= (uint)points.Length || (uint)second >= (uint)points.Length ||
                (uint)third >= (uint)points.Length)
                return [points];
            int a = welded[first], b = welded[second], c = welded[third];
            if (a == b || b == c || c == a) return [points];
            (int A, int B) ab = Edge(a, b), bc = Edge(b, c), ca = Edge(c, a);
            float abLength = Vector3.DistanceSquared(points[first], points[second]);
            float bcLength = Vector3.DistanceSquared(points[second], points[third]);
            float caLength = Vector3.DistanceSquared(points[third], points[first]);
            shortest[triangle] = abLength <= bcLength && abLength <= caLength ? ab :
                bcLength <= caLength ? bc : ca;
            AddEdge(ab, triangle);
            AddEdge(bc, triangle);
            AddEdge(ca, triangle);
        }

        // Shortest on both adjacent triangles is a candidate cross-section edge.
        // Only complete, nonbranching loops become cuts, keeping the entire ring
        // in both neighboring bands; partial seams cannot safely divide a hull.
        var neighbors = new Dictionary<int, List<int>>();
        var boundaryDegrees = new Dictionary<int, int>();
        foreach (var (edge, owners) in edges)
        {
            if (owners.Count > 2) return [points];
            if (owners.Count == 1)
            {
                boundaryDegrees[edge.A] = boundaryDegrees.GetValueOrDefault(edge.A) + 1;
                boundaryDegrees[edge.B] = boundaryDegrees.GetValueOrDefault(edge.B) + 1;
                continue;
            }
            if (shortest[owners[0]] != edge || shortest[owners[1]] != edge)
                continue;
            AddNeighbor(edge.A, edge.B);
            AddNeighbor(edge.B, edge.A);
        }
        // A tube may have open ends, but a torn or branched boundary has no
        // complete cap ring for the generated convex brushes to share.
        if (boundaryDegrees.Values.Any(degree => degree != 2)) return [points];
        var cutEdges = new HashSet<(int A, int B)>();
        var visitedVertices = new HashSet<int>();
        foreach (int start in neighbors.Keys)
        {
            if (!visitedVertices.Add(start)) continue;
            var queue = new Queue<int>();
            var ring = new List<int>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out int vertex))
            {
                ring.Add(vertex);
                foreach (int adjacent in neighbors[vertex])
                    if (visitedVertices.Add(adjacent)) queue.Enqueue(adjacent);
            }
            if (ring.Count < 3 || ring.Any(vertex => neighbors[vertex].Count != 2))
                continue;
            foreach (int vertex in ring)
                foreach (int adjacent in neighbors[vertex])
                    cutEdges.Add(Edge(vertex, adjacent));
        }
        if (cutEdges.Count == 0) return [points];

        var adjacentTriangles = new List<int>[shortest.Length];
        for (int index = 0; index < adjacentTriangles.Length; index++) adjacentTriangles[index] = [];
        bool[] touchesCut = new bool[shortest.Length];
        foreach (var (edge, owners) in edges)
        {
            if (owners.Count != 2) continue;
            if (cutEdges.Contains(edge))
            {
                touchesCut[owners[0]] = touchesCut[owners[1]] = true;
                continue;
            }
            adjacentTriangles[owners[0]].Add(owners[1]);
            adjacentTriangles[owners[1]].Add(owners[0]);
        }

        var sections = new List<Vector3[]>();
        bool[] visitedTriangles = new bool[shortest.Length];
        bool concave = false;
        for (int start = 0; start < shortest.Length; start++)
        {
            if (visitedTriangles[start]) continue;
            var queue = new Queue<int>();
            var component = new List<int>();
            var indices = new HashSet<int>();
            visitedTriangles[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out int triangle))
            {
                component.Add(triangle);
                for (int corner = 0; corner < 3; corner++) indices.Add(triangles[triangle * 3 + corner]);
                foreach (int adjacent in adjacentTriangles[triangle])
                    if (!visitedTriangles[adjacent])
                    {
                        visitedTriangles[adjacent] = true;
                        queue.Enqueue(adjacent);
                    }
            }
            if (!component.Any(triangle => touchesCut[triangle])) return [points];
            if (sections.Count == 64) return [points];
            sections.Add(indices.Select(index => points[index]).ToArray());
            if (!concave)
            {
                foreach (int sample in new[] { component[0], component[component.Count / 2], component[^1] })
                    if (HasNonSupportingFace(points, triangles, sample)) { concave = true; break; }
            }
        }
        return sections.Count > 1 && concave ? sections.ToArray() : [points];

        void AddEdge((int A, int B) edge, int triangle)
        {
            if (!edges.TryGetValue(edge, out List<int>? owners)) edges.Add(edge, owners = []);
            owners.Add(triangle);
        }

        void AddNeighbor(int vertex, int adjacent)
        {
            if (!neighbors.TryGetValue(vertex, out List<int>? list)) neighbors.Add(vertex, list = []);
            list.Add(adjacent);
        }
    }

    private static (int A, int B) Edge(int a, int b) => a < b ? (a, b) : (b, a);

    private static bool HasNonSupportingFace(Vector3[] points, int[] triangles, int triangle)
    {
        Vector3 origin = points[triangles[triangle * 3]];
        Vector3 normal = Vector3.Cross(points[triangles[triangle * 3 + 1]] - origin,
            points[triangles[triangle * 3 + 2]] - origin);
        if (normal.LengthSquared() <= 1e-12f) return false;
        normal = Vector3.Normalize(normal);
        bool positive = false, negative = false;
        foreach (Vector3 point in points)
        {
            double side = BrushGeometry.Dot(normal, point - origin);
            positive |= side > BrushGeometry.PlaneTolerance;
            negative |= side < -BrushGeometry.PlaneTolerance;
            if (positive && negative) return true;
        }
        return false;
    }
}
