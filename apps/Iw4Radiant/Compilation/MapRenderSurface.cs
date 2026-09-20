using System.Numerics;

namespace Iw4Radiant.Compilation;

// A planar brush face or mesh triangle with the attributes consumed by both the bake and world writer.
internal sealed record MapRenderSurface(string Material, Vector3[] Vertices, Vector3 Normal,
    Vector3[] Normals, Vector3[] Tangents, Vector3[] Binormals, Vector2[] TextureCoordinates, Vector4[] Colors, int SourceIndex)
{
    internal int ModelIndex { get; init; }
    internal float Displacement { get; init; }
    internal Vector3? ReflectionCenter { get; init; }
    internal bool SharesBoundaryAt(MapRenderSurface other, Vector3 point)
    {
        if (Material != other.Material) return false;
        Vector3 first = default, second = default;
        int sharedCount = 0;
        foreach (Vector3 vertex in Vertices)
        {
            if (sharedCount > 0 && vertex.Equals(first) || sharedCount > 1 && vertex.Equals(second)) continue;
            bool shared = false;
            foreach (Vector3 otherVertex in other.Vertices)
                if (vertex.Equals(otherVertex))
                {
                    shared = true;
                    break;
                }
            if (!shared) continue;
            if (sharedCount == 0) first = vertex;
            else if (sharedCount == 1) second = vertex;
            else return false;
            sharedCount++;
        }
        if (sharedCount == 1) return Vector3.DistanceSquared(point, first) < 0.000001f;
        if (sharedCount != 2) return false;
        if (!(Edge(Vertices, first, second) && Edge(other.Vertices, second, first) ||
              Edge(Vertices, second, first) && Edge(other.Vertices, first, second))) return false;
        Vector3 edge = second - first;
        float along = Math.Clamp(Vector3.Dot(point - first, edge) / edge.LengthSquared(), 0, 1);
        return Vector3.DistanceSquared(point, first + edge * along) < 0.000001f;

        static bool Edge(Vector3[] vertices, Vector3 a, Vector3 b)
        {
            for (int index = 0; index < vertices.Length; index++)
                if (vertices[index] == a && vertices[(index + 1) % vertices.Length] == b)
                    return true;
            return false;
        }
    }

    internal (Vector2 Texture, Vector4 Color, Vector3 Normal) Sample(Vector3 point)
    {
        for (int corner = 1; corner < Vertices.Length - 1; corner++)
        {
            Vector3 u = Vertices[corner] - Vertices[0], v = Vertices[corner + 1] - Vertices[0];
            Vector3 p = point - Vertices[0];
            double uu = Vector3.Dot(u, u), uv = Vector3.Dot(u, v), vv = Vector3.Dot(v, v);
            double pu = Vector3.Dot(p, u), pv = Vector3.Dot(p, v), determinant = uu * vv - uv * uv;
            if (determinant <= 0) continue;
            float b = (float)((vv * pu - uv * pv) / determinant), c = (float)((uu * pv - uv * pu) / determinant);
            if (b < -0.001f || c < -0.001f || b + c > 1.001f) continue;
            b = Math.Clamp(b, 0, 1);
            c = Math.Clamp(c, 0, 1 - b);
            float a = 1 - b - c;
            return (TextureCoordinates[0] * a + TextureCoordinates[corner] * b + TextureCoordinates[corner + 1] * c,
                Colors[0] * a + Colors[corner] * b + Colors[corner + 1] * c,
                Vector3.Normalize(Normals[0] * a + Normals[corner] * b + Normals[corner + 1] * c));
        }
        throw new InvalidDataException("A baked surface sample lies outside its polygon.");
    }
}
