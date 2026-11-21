using System.Numerics;
using IW4.Game.Assets.Physics;

namespace IW4.Studio.Desktop.Editors.PhysCollmap;

internal static class PhysCollmapViewportGeometry
{
    internal sealed record Outline(Vector3[] Vertices, (int A, int B)[] Edges);

    private static readonly (int A, int B)[] BoxEdges =
    [
        (0, 1), (0, 2), (0, 4), (1, 3), (1, 5), (2, 3),
        (2, 6), (3, 7), (4, 5), (4, 6), (5, 7), (6, 7)
    ];

    internal static Outline Box => new(
    [
        new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
        new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f),
        new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
        new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f)
    ], BoxEdges);

    internal static Outline Cylinder
    {
        get
        {
            const int segments = 32;
            var vertices = new Vector3[segments * 2];
            var edges = new List<(int, int)>(segments * 2 + 4);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * MathF.Tau / segments;
                float x = MathF.Cos(angle) * 0.5f;
                float y = MathF.Sin(angle) * 0.5f;
                vertices[i] = new Vector3(x, y, -0.5f);
                vertices[i + segments] = new Vector3(x, y, 0.5f);
                int next = (i + 1) % segments;
                edges.Add((i, next));
                edges.Add((i + segments, next + segments));
                if (i % (segments / 4) == 0) edges.Add((i, i + segments));
            }
            return new Outline(vertices, [.. edges]);
        }
    }

    internal static Outline Brush(BrushWrapper wrapper)
    {
        Vector3 midpoint = ToVector(wrapper.Bounds.MidPoint);
        Vector3 half = ToVector(wrapper.Bounds.HalfSize);
        if (!Finite(half) || half.X <= 0 || half.Y <= 0 || half.Z <= 0)
            return Box;

        var planes = new List<(Vector3 Normal, float Distance)>
        {
            (Vector3.UnitX, midpoint.X + half.X), (-Vector3.UnitX, -midpoint.X + half.X),
            (Vector3.UnitY, midpoint.Y + half.Y), (-Vector3.UnitY, -midpoint.Y + half.Y),
            (Vector3.UnitZ, midpoint.Z + half.Z), (-Vector3.UnitZ, -midpoint.Z + half.Z)
        };
        foreach (CBrushSide side in wrapper.Brush.Sides)
        {
            if (side.Plane is not { } plane)
                continue;
            Vector3 normal = ToVector(plane.Normal);
            if (Finite(normal) && normal.LengthSquared() > 1e-8f && float.IsFinite(plane.Dist))
                planes.Add((normal, plane.Dist));
        }

        if (planes.Count == 6)
            return Box;
        var vertices = new List<Vector3>();
        for (int a = 0; a < planes.Count - 2; a++)
        for (int b = a + 1; b < planes.Count - 1; b++)
        for (int c = b + 1; c < planes.Count; c++)
        {
            var pa = planes[a]; var pb = planes[b]; var pc = planes[c];
            Vector3 bc = Vector3.Cross(pb.Normal, pc.Normal);
            float determinant = Vector3.Dot(pa.Normal, bc);
            if (MathF.Abs(determinant) < 1e-6f)
                continue;
            Vector3 point = (pa.Distance * bc + pb.Distance * Vector3.Cross(pc.Normal, pa.Normal)
                + pc.Distance * Vector3.Cross(pa.Normal, pb.Normal)) / determinant;
            if (!Finite(point) || planes.Any(p => Vector3.Dot(p.Normal, point) > p.Distance + 0.01f))
                continue;
            if (vertices.Any(v => Vector3.DistanceSquared(v, point) < 1e-4f))
                continue;
            vertices.Add(point);
        }
        if (vertices.Count < 4)
            return Box;

        // Every convex polyhedron edge is shared by two face planes.
        var edges = new List<(int, int)>();
        for (int i = 0; i < vertices.Count - 1; i++)
        for (int j = i + 1; j < vertices.Count; j++)
        {
            Vector3? firstNormal = null;
            foreach (var plane in planes)
            {
                if (MathF.Abs(Vector3.Dot(plane.Normal, vertices[i]) - plane.Distance) >= 0.02f ||
                    MathF.Abs(Vector3.Dot(plane.Normal, vertices[j]) - plane.Distance) >= 0.02f)
                    continue;
                if (firstNormal is { } normal && Vector3.Cross(normal, plane.Normal).LengthSquared() > 1e-6f)
                {
                    edges.Add((i, j));
                    break;
                }
                firstNormal ??= plane.Normal;
            }
        }
        if (edges.Count == 0)
            return Box;

        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = (vertices[i] - midpoint) / (half * 2);
        return new Outline([.. vertices], [.. edges]);
    }

    internal static Vector3 ToVector(IW4.Game.Math.Vec3 value) => new(value.X, value.Y, value.Z);

    internal static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static Matrix4x4 Rotation(Vector3 degrees)
    {
        Vector3 a = degrees * (MathF.PI / 180f);
        return Matrix4x4.CreateRotationX(a.X) * Matrix4x4.CreateRotationY(a.Y) * Matrix4x4.CreateRotationZ(a.Z);
    }

    internal static Vector3 Degrees(Matrix4x4 m)
    {
        float horizontal = MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        Vector3 angles = horizontal > .00001f
            ? new(MathF.Atan2(m.M23, m.M33), MathF.Atan2(-m.M13, horizontal), MathF.Atan2(m.M12, m.M11))
            : new(MathF.Atan2(-m.M32, m.M22), MathF.Atan2(-m.M13, horizontal), 0);
        return angles * (180f / MathF.PI);
    }

    internal static Vector3[] Basis(Vector3 rotationDegrees)
    {
        Matrix4x4 rotation = Rotation(rotationDegrees);
        return [Vector3.TransformNormal(Vector3.UnitX, rotation),
            Vector3.TransformNormal(Vector3.UnitY, rotation),
            Vector3.TransformNormal(Vector3.UnitZ, rotation)];
    }

    internal static Vector3 World(Vector3 local, Vector3 position, Vector3 size, Vector3[] basis) =>
        position + basis[0] * local.X * size.X + basis[1] * local.Y * size.Y + basis[2] * local.Z * size.Z;
}
