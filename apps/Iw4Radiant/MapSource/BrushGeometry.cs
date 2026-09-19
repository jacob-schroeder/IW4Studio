using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class BrushGeometry
{
    internal const float PointTolerance = 0.01f;
    internal const float PlaneTolerance = 0.02f;

    internal static bool IsFinite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    internal static double Dot(Vector3 a, Vector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;

    internal static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        double ax = (double)a.X - b.X, ay = (double)a.Y - b.Y, az = (double)a.Z - b.Z;
        double cx = (double)c.X - b.X, cy = (double)c.Y - b.Y, cz = (double)c.Z - b.Z;
        double x = ay * cz - az * cy, y = az * cx - ax * cz, z = ax * cy - ay * cx;
        double length = Math.Sqrt(x * x + y * y + z * z);
        return new((float)(x / length), (float)(y / length), (float)(z / length));
    }

    internal static List<MapPolygon> BuildPolygons(IReadOnlyList<MapFace> faces)
    {
        var normals = faces.Select(face => face.Normal).ToArray();
        var distances = faces.Select((face, index) => Dot(normals[index], face.A)).ToArray();
        var points = faces.Select(_ => new List<Vector3>()).ToArray();
        for (int a = 0; a < faces.Count; a++)
        for (int b = a + 1; b < faces.Count; b++)
        for (int c = b + 1; c < faces.Count; c++)
        {
            var na = normals[a]; var nb = normals[b]; var nc = normals[c];
            double x = (double)nb.Y * nc.Z - (double)nb.Z * nc.Y;
            double y = (double)nb.Z * nc.X - (double)nb.X * nc.Z;
            double z = (double)nb.X * nc.Y - (double)nb.Y * nc.X;
            double determinant = na.X * x + na.Y * y + na.Z * z;
            if (Math.Abs(determinant) < 1e-10) continue;
            Vector3 point = new(
                (float)((distances[a] * x + distances[b] * ((double)nc.Y * na.Z - (double)nc.Z * na.Y) +
                         distances[c] * ((double)na.Y * nb.Z - (double)na.Z * nb.Y)) / determinant),
                (float)((distances[a] * y + distances[b] * ((double)nc.Z * na.X - (double)nc.X * na.Z) +
                         distances[c] * ((double)na.Z * nb.X - (double)na.X * nb.Z)) / determinant),
                (float)((distances[a] * z + distances[b] * ((double)nc.X * na.Y - (double)nc.Y * na.X) +
                         distances[c] * ((double)na.X * nb.Y - (double)na.Y * nb.X)) / determinant));
            if (!IsFinite(point)) continue;
            bool inside = true;
            for (int face = 0; face < faces.Count; face++)
                if (Dot(normals[face], point) > distances[face] + PlaneTolerance)
                {
                    inside = false;
                    break;
                }
            if (!inside) continue;
            AddDistinct(points[a], point);
            AddDistinct(points[b], point);
            AddDistinct(points[c], point);
        }
        List<MapPolygon> polygons = [];
        for (int face = 0; face < faces.Count; face++)
        {
            if (points[face].Count < 3) continue;
            Vector3 center = points[face][0];
            center += points[face].Aggregate(Vector3.Zero, (sum, point) => sum + (point - center)) / points[face].Count;
            Vector3 u = Vector3.Normalize(points[face][0] - center);
            Vector3 v = Vector3.Cross(normals[face], u);
            polygons.Add(new MapPolygon(faces[face], points[face].OrderBy(point =>
                Math.Atan2(Dot(point - center, v), Dot(point - center, u))).ToArray()));
        }
        return polygons;
    }

    internal static List<Vector3> Vertices(IReadOnlyList<MapPolygon> polygons)
    {
        List<Vector3> result = [];
        foreach (var polygon in polygons)
        foreach (Vector3 point in polygon.Vertices)
            AddDistinct(result, point);
        return result;
    }

    internal static int FindVertex(IReadOnlyList<Vector3> vertices, Vector3 point)
    {
        for (int index = 0; index < vertices.Count; index++)
            if (Vector3.DistanceSquared(vertices[index], point) < PointTolerance * PointTolerance)
                return index;
        return -1;
    }

    internal static void Validate(MapBrush brush)
    {
        if (brush.Faces.Count < 4 || brush.Faces.Any(face => !IsFinite(face.A) || !IsFinite(face.B) ||
                !IsFinite(face.C) || !IsFinite(face.Normal)))
            throw new ArgumentException("A brush must have at least four finite, nondegenerate planes.");
        var polygons = brush.GetPolygons();
        var vertices = Vertices(polygons);
        if (polygons.Count != brush.Faces.Count || vertices.Count < 4)
            throw new ArgumentException("The edit would create an empty or degenerate brush.");
        Vector3 center = vertices[0];
        center += vertices.Aggregate(Vector3.Zero, (sum, point) => sum + (point - center)) / vertices.Count;
        var edges = new Dictionary<(int, int), (int Count, int Direction)>();
        double volume = 0;
        foreach (var polygon in polygons)
        {
            Vector3 normal = polygon.Face.Normal;
            double distance = Dot(normal, polygon.Face.A);
            if (vertices.Any(point => Dot(normal, point) > distance + PlaneTolerance))
                throw new ArgumentException("The edit would create a nonconvex brush.");
            for (int index = 0; index < polygon.Vertices.Length; index++)
            {
                Vector3 point = polygon.Vertices[index];
                if (Math.Abs(Dot(normal, point) - distance) > PlaneTolerance)
                    throw new ArgumentException("The edit would create a nonplanar face.");
                int a = FindVertex(vertices, point), b = FindVertex(vertices, polygon.Vertices[(index + 1) % polygon.Vertices.Length]);
                if (a == b)
                    throw new ArgumentException("The edit would collapse a brush edge.");
                var key = a < b ? (a, b) : (b, a);
                edges.TryGetValue(key, out var edge);
                edges[key] = (edge.Count + 1, edge.Direction + (a < b ? 1 : -1));
            }
            double area = 0;
            for (int index = 1; index < polygon.Vertices.Length - 1; index++)
            {
                Vector3 a = polygon.Vertices[0] - center, b = polygon.Vertices[index] - center,
                    c = polygon.Vertices[index + 1] - center;
                area += Dot(normal, Vector3.Cross(b - a, c - a));
                volume += (a.X * ((double)b.Y * c.Z - (double)b.Z * c.Y) +
                           a.Y * ((double)b.Z * c.X - (double)b.X * c.Z) +
                           a.Z * ((double)b.X * c.Y - (double)b.Y * c.X)) / 6;
            }
            if (!double.IsFinite(area) || area <= 1e-8)
                throw new ArgumentException("The edit would create a zero-area brush face.");
        }
        if (edges.Values.Any(edge => edge.Count != 2 || edge.Direction != 0) ||
            !double.IsFinite(volume) || volume <= 1e-6)
            throw new ArgumentException("The edit would create an open or zero-volume brush.");
    }

    private static void AddDistinct(List<Vector3> vertices, Vector3 point)
    {
        if (FindVertex(vertices, point) < 0) vertices.Add(point);
    }
}
