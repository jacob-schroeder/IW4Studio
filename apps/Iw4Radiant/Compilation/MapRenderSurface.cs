using System.Numerics;

namespace Iw4Radiant.Compilation;

// A planar brush face or mesh triangle with the attributes consumed by both the bake and world writer.
internal sealed record MapRenderSurface(string Material, Vector3[] Vertices, Vector3 Normal,
    Vector3[] Normals, Vector3[] Tangents, Vector3[] Binormals, Vector2[] TextureCoordinates, Vector4[] Colors, int SourceIndex)
{
    internal int ModelIndex { get; init; }
    internal bool ContributesToLightGrid { get; init; } = true;
    internal byte SunPrimaryLightIndex { get; init; } = 1;
    // Authored world units covered by one secondary-lightmap luxel. Terrain carries its authored
    // density here; brush surfaces remain null so the baker can apply its automatic 4-unit baseline.
    internal float? LightmapSize { get; init; }
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
            if (!TryTriangleCoordinates(point, corner, out double weightB, out double weightC) ||
                weightB < -0.001 || weightC < -0.001 || weightB + weightC > 1.001) continue;
            float b = (float)Math.Clamp(weightB, 0, 1);
            float c = (float)Math.Clamp(weightC, 0, 1 - b);
            return Sample(corner, b, c);
        }
        // Luxel clamping and ray hits round their boundary point back to Vector3.
        // On very thin faces that rounding can exceed a barycentric tolerance;
        // recover only points within a few float ULPs of an actual polygon edge.
        var (nearestEdge, nearestAlong, nearestDistance) = ClosestEdge(point);
        float coordinateScale = MathF.Max(1, MathF.Max(MathF.Abs(point.X), MathF.Max(MathF.Abs(point.Y), MathF.Abs(point.Z))));
        foreach (Vector3 vertex in Vertices)
            coordinateScale = MathF.Max(coordinateScale, MathF.Max(MathF.Abs(vertex.X), MathF.Max(MathF.Abs(vertex.Y), MathF.Abs(vertex.Z))));
        double roundingTolerance = 8d * (MathF.BitIncrement(coordinateScale) - coordinateScale);
        if (nearestEdge >= 0 && nearestDistance <= roundingTolerance * roundingTolerance)
        {
            int next = (nearestEdge + 1) % Vertices.Length;
            float along = (float)nearestAlong;
            return (Vector2.Lerp(TextureCoordinates[nearestEdge], TextureCoordinates[next], along),
                Vector4.Lerp(Colors[nearestEdge], Colors[next], along),
                Vector3.Normalize(Vector3.Lerp(Normals[nearestEdge], Normals[next], along)));
        }
        throw new InvalidDataException("A baked surface sample lies outside its polygon.");
    }

    internal (Vector2 Texture, Vector4 Color, Vector3 Normal) Sample(int corner, float b, float c)
    {
        float a = 1 - b - c;
        return (TextureCoordinates[0] * a + TextureCoordinates[corner] * b + TextureCoordinates[corner + 1] * c,
            Colors[0] * a + Colors[corner] * b + Colors[corner + 1] * c,
            Vector3.Normalize(Normals[0] * a + Normals[corner] * b + Normals[corner + 1] * c));
    }

    internal Vector3 ClampToPolygon(Vector3 point)
    {
        // The baker and attribute sampler must agree about the polygon interior.
        for (int corner = 1; corner < Vertices.Length - 1; corner++)
            if (TryTriangleCoordinates(point, corner, out double b, out double c) && b >= 0 && c >= 0 && b + c <= 1)
                return point;
        var (edge, along, _) = ClosestEdge(point);
        if (edge < 0) throw new InvalidDataException("A baked surface has no nonzero polygon edges.");
        Vector3 first = Vertices[edge], second = Vertices[(edge + 1) % Vertices.Length];
        return new((float)(first.X + ((double)second.X - first.X) * along),
            (float)(first.Y + ((double)second.Y - first.Y) * along),
            (float)(first.Z + ((double)second.Z - first.Z) * along));
    }

    private bool TryTriangleCoordinates(Vector3 point, int corner, out double b, out double c)
    {
        Vector3 a = Vertices[0], second = Vertices[corner], third = Vertices[corner + 1];
        double ux = (double)second.X - a.X, uy = (double)second.Y - a.Y, uz = (double)second.Z - a.Z;
        double vx = (double)third.X - a.X, vy = (double)third.Y - a.Y, vz = (double)third.Z - a.Z;
        double px = (double)point.X - a.X, py = (double)point.Y - a.Y, pz = (double)point.Z - a.Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        b = c = 0;
        // Signed projected areas avoid squaring the condition number of a thin
        // triangle, as the Gram determinant does. Subtract in double as well.
        if (Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz))
        {
            if (nx == 0) return false;
            b = (py * vz - pz * vy) / nx;
            c = (uy * pz - uz * py) / nx;
        }
        else if (Math.Abs(ny) >= Math.Abs(nz))
        {
            b = (pz * vx - px * vz) / ny;
            c = (uz * px - ux * pz) / ny;
        }
        else
        {
            b = (px * vy - py * vx) / nz;
            c = (ux * py - uy * px) / nz;
        }
        return double.IsFinite(b) && double.IsFinite(c);
    }

    private (int Edge, double Along, double DistanceSquared) ClosestEdge(Vector3 point)
    {
        double nearestDistance = double.PositiveInfinity, nearestAlong = 0;
        int nearestEdge = -1;
        for (int edge = 0; edge < Vertices.Length; edge++)
        {
            Vector3 a = Vertices[edge], b = Vertices[(edge + 1) % Vertices.Length];
            double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y, dz = (double)b.Z - a.Z;
            double lengthSquared = dx * dx + dy * dy + dz * dz;
            if (lengthSquared == 0) continue;
            double px = (double)point.X - a.X, py = (double)point.Y - a.Y, pz = (double)point.Z - a.Z;
            double along = Math.Clamp((px * dx + py * dy + pz * dz) / lengthSquared, 0, 1);
            double ex = px - along * dx, ey = py - along * dy, ez = pz - along * dz;
            double distance = ex * ex + ey * ey + ez * ez;
            if (distance < nearestDistance) { nearestDistance = distance; nearestAlong = along; nearestEdge = edge; }
        }
        return (nearestEdge, nearestAlong, nearestDistance);
    }
}
