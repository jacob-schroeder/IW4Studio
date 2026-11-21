using System.Numerics;
using IW4.Game.Assets.MapEnts;

namespace IW4.Studio.Desktop.Editors.AddonMapEnts;

public static class AddonEntitySceneGeometry
{
    private readonly record struct Plane(Vector3 Normal, float Distance);

    // Trigger hull coordinates are local to the entity origin. Every slab is a pair
    // of planes, so projecting its clipped vertices retains compound trigger shape.
    public static IReadOnlyList<AddonEntitySceneOutline> Outlines(MapTriggers triggers, int modelIndex)
    {
        if ((uint)modelIndex >= (uint)triggers.Models.Count)
            return [];

        TriggerModel model = triggers.Models[modelIndex];
        var outlines = new List<AddonEntitySceneOutline>(model.HullCount);
        for (int offset = 0; offset < model.HullCount; offset++)
        {
            int hullIndex = model.FirstHull + offset;
            if ((uint)hullIndex >= (uint)triggers.Hulls.Count) break;
            TriggerHull hull = triggers.Hulls[hullIndex];
            var midpoint = hull.Bounds.MidPoint;
            var half = hull.Bounds.HalfSize;
            if (!Finite(midpoint.X, midpoint.Y, midpoint.Z, half.X, half.Y, half.Z) ||
                half.X < 0 || half.Y < 0 || half.Z < 0)
                continue;

            float minX = midpoint.X - half.X, maxX = midpoint.X + half.X;
            float minY = midpoint.Y - half.Y, maxY = midpoint.Y + half.Y;
            float minZ = midpoint.Z - half.Z, maxZ = midpoint.Z + half.Z;
            List<List<Vector3>> faces =
            [
                [new(minX, minY, minZ), new(minX, maxY, minZ), new(minX, maxY, maxZ), new(minX, minY, maxZ)],
                [new(maxX, minY, minZ), new(maxX, minY, maxZ), new(maxX, maxY, maxZ), new(maxX, maxY, minZ)],
                [new(minX, minY, minZ), new(maxX, minY, minZ), new(maxX, maxY, minZ), new(minX, maxY, minZ)],
                [new(minX, minY, maxZ), new(minX, maxY, maxZ), new(maxX, maxY, maxZ), new(maxX, minY, maxZ)],
                [new(minX, minY, minZ), new(minX, minY, maxZ), new(maxX, minY, maxZ), new(maxX, minY, minZ)],
                [new(minX, maxY, minZ), new(maxX, maxY, minZ), new(maxX, maxY, maxZ), new(minX, maxY, maxZ)]
            ];
            for (int slabOffset = 0; slabOffset < hull.SlabCount; slabOffset++)
            {
                int slabIndex = hull.FirstSlab + slabOffset;
                if ((uint)slabIndex >= (uint)triggers.Slabs.Count) break;
                TriggerSlab slab = triggers.Slabs[slabIndex];
                if (!Finite(slab.Dir.X, slab.Dir.Y, slab.Dir.Z, slab.MidPoint, slab.HalfSize) ||
                    slab.HalfSize < 0) continue;
                Vector3 normal = new(slab.Dir.X, slab.Dir.Y, slab.Dir.Z);
                if (normal.LengthSquared() < 0.000001f) continue;
                faces = Clip(faces, new Plane(normal, slab.MidPoint + slab.HalfSize));
                if (faces.Count == 0) break;
                faces = Clip(faces, new Plane(-normal, -slab.MidPoint + slab.HalfSize));
                if (faces.Count == 0) break;
            }

            Vector3[] vertices = faces.SelectMany(face => face).Distinct().ToArray();
            if (vertices.Length == 0) continue;

            Vector2[] top = Hull(vertices.Select(vertex => new Vector2(vertex.X, vertex.Y)));
            Vector2[] side = Hull(vertices.Select(vertex => new Vector2(vertex.X, vertex.Z)));
            if (top.Length > 0 || side.Length > 0)
                outlines.Add(new AddonEntitySceneOutline(top, side));
        }
        return outlines;
    }

    private static bool Finite(params float[] values) => values.All(float.IsFinite);

    private static List<List<Vector3>> Clip(List<List<Vector3>> faces, Plane plane)
    {
        var result = new List<List<Vector3>>(faces.Count + 1);
        var cap = new List<Vector3>();
        foreach (List<Vector3> face in faces)
        {
            var clipped = new List<Vector3>(face.Count + 2);
            Vector3 previous = face[^1];
            float previousDistance = Vector3.Dot(plane.Normal, previous) - plane.Distance;
            foreach (Vector3 current in face)
            {
                float distance = Vector3.Dot(plane.Normal, current) - plane.Distance;
                bool currentInside = distance <= 0, previousInside = previousDistance <= 0;
                if (currentInside != previousInside)
                {
                    Vector3 crossing = previous + (current - previous) *
                        (previousDistance / (previousDistance - distance));
                    clipped.Add(crossing);
                    if (!cap.Any(point => Vector3.DistanceSquared(point, crossing) < 0.000001f))
                        cap.Add(crossing);
                }
                if (currentInside) clipped.Add(current);
                previous = current;
                previousDistance = distance;
            }
            if (clipped.Count >= 3) result.Add(clipped);
        }
        if (cap.Count >= 3)
        {
            Vector3 center = Vector3.Zero;
            foreach (Vector3 point in cap) center += point;
            center /= cap.Count;
            Vector3 normal = Vector3.Normalize(plane.Normal);
            Vector3 reference = Math.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 u = Vector3.Normalize(Vector3.Cross(reference, normal));
            Vector3 v = Vector3.Cross(normal, u);
            cap.Sort((a, b) => Math.Atan2(Vector3.Dot(a - center, v), Vector3.Dot(a - center, u))
                .CompareTo(Math.Atan2(Vector3.Dot(b - center, v), Vector3.Dot(b - center, u))));
            result.Add(cap);
        }
        return result;
    }

    private static Vector2[] Hull(IEnumerable<Vector2> source)
    {
        Vector2[] points = source.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (points.Length <= 2) return points;
        var lower = new List<Vector2>();
        foreach (Vector2 point in points)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }
        var upper = new List<Vector2>();
        for (int i = points.Length - 1; i >= 0; i--)
        {
            Vector2 point = points[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower.ToArray();
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
