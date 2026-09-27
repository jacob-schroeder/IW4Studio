using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Camera;

// Procedural editor preview. These sizes and crack choices are not recovered IW4 parameters.
internal static class CameraGlassFracture
{
    private const int MaximumShards = 128;
    private const float PreviewAreaPerShard = 380f;

    internal static IReadOnlyList<Vector2[]> Create(float width, float height, Vector2 impact)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Glass pane dimensions must be finite and positive.");
        if (!float.IsFinite(impact.X) || !float.IsFinite(impact.Y))
            throw new ArgumentOutOfRangeException(nameof(impact), "The impact must be finite.");

        impact = Vector2.Clamp(impact, Vector2.Zero, new Vector2(width, height));
        List<Vector2[]> pieces =
        [
            [new(0, 0), new(width, 0), new(width, height), new(0, height)]
        ];
        int desired = (int)Math.Clamp(Math.Round((double)width * height / PreviewAreaPerShard),
            1, MaximumShards);
        if (desired == 1) return pieces;

        uint random = 2166136261;
        Mix(ref random, width);
        Mix(ref random, height);
        Mix(ref random, impact.X);
        Mix(ref random, impact.Y);
        if (random == 0) random = 1;

        int radialLines = Math.Min(7, Math.Max(1, desired / 6));
        float phase = Next(ref random) * MathF.PI / radialLines;
        for (int line = 0; line < radialLines && pieces.Count < desired; line++)
        {
            float angle = (line + 0.5f) * MathF.PI / radialLines + phase +
                (Next(ref random) - 0.5f) * 0.18f;
            Vector2 normal = new(-MathF.Sin(angle), MathF.Cos(angle));
            double offset = Dot(normal, impact);
            for (int index = 0; index < pieces.Count && pieces.Count < desired; index++)
            {
                if (!TrySplit(pieces[index], normal, offset, width, height, out var a, out var b))
                    continue;
                pieces[index] = a;
                pieces.Add(b);
            }
        }

        // Cut long spokes across their radial direction. Near-impact pieces receive a smaller
        // target area, so the resulting shard density changes continuously across the pane.
        HashSet<Vector2[]> unsplittable = new(ReferenceEqualityComparer.Instance);
        while (pieces.Count < desired)
        {
            Vector2[]? chosen = null;
            float highestPriority = 0;
            foreach (Vector2[] piece in pieces)
            {
                if (unsplittable.Contains(piece)) continue;
                Vector2 center = Center(piece);
                float distance = Vector2.Distance(center, impact) / (0.45f * MathF.Max(width, height));
                float targetArea = PreviewAreaPerShard * (0.55f + 1.1f * MathF.Min(1, distance));
                float priority = (float)(Area(piece) / targetArea);
                if (priority <= highestPriority) continue;
                highestPriority = priority;
                chosen = piece;
            }
            if (chosen is null) break;

            bool split = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 radial = Center(chosen) - impact;
                float angle = radial.LengthSquared() > 0.0001f
                    ? MathF.Atan2(radial.Y, radial.X)
                    : Next(ref random) * MathF.PI;
                angle += (Next(ref random) - 0.5f) * 0.55f;
                Vector2 normal = new(MathF.Cos(angle), MathF.Sin(angle));
                double low = double.PositiveInfinity, high = double.NegativeInfinity;
                foreach (Vector2 vertex in chosen)
                {
                    double position = Dot(normal, vertex);
                    low = Math.Min(low, position);
                    high = Math.Max(high, position);
                }
                double offset = low + (high - low) * (0.38 + Next(ref random) * 0.24);
                if (!TrySplit(chosen, normal, offset, width, height, out var a, out var b))
                    continue;
                pieces[pieces.IndexOf(chosen)] = a;
                pieces.Add(b);
                split = true;
                break;
            }
            if (!split) unsplittable.Add(chosen);
        }
        return pieces;
    }

    private static bool TrySplit(Vector2[] source, Vector2 normal, double offset,
        float paneWidth, float paneHeight, out Vector2[] a, out Vector2[] b)
    {
        List<Vector2> positive = [], negative = [];
        for (int index = 0; index < source.Length; index++)
        {
            Vector2 first = source[index], second = source[(index + 1) % source.Length];
            double firstDistance = Dot(normal, first) - offset;
            double secondDistance = Dot(normal, second) - offset;
            if (firstDistance >= 0) positive.Add(first);
            if (firstDistance <= 0) negative.Add(first);
            if ((firstDistance < 0 && secondDistance > 0) ||
                (firstDistance > 0 && secondDistance < 0))
            {
                float fraction = (float)(firstDistance / (firstDistance - secondDistance));
                Vector2 crossing = first + (second - first) * fraction;
                positive.Add(crossing);
                negative.Add(crossing);
            }
        }
        a = Clean(positive);
        b = Clean(negative);
        float minimumWidth = MathF.Min(0.5f, MathF.Min(paneWidth, paneHeight) * 0.1f);
        return Usable(a, minimumWidth) && Usable(b, minimumWidth);
    }

    private static Vector2[] Clean(List<Vector2> polygon)
    {
        // Removing points on straight edges preserves the clipped region and avoids redundant
        // planes when each polygon is extruded into a convex brush.
        for (int index = polygon.Count - 1; index >= 0 && polygon.Count > 2; index--)
        {
            Vector2 previous = polygon[(index + polygon.Count - 1) % polygon.Count];
            Vector2 current = polygon[index];
            Vector2 next = polygon[(index + 1) % polygon.Count];
            if (current == previous || current == next ||
                Cross(current - previous, next - current) == 0)
                polygon.RemoveAt(index);
        }
        return polygon.ToArray();
    }

    private static bool Usable(Vector2[] polygon, float minimumWidth)
    {
        if (polygon.Length < 3 || Area(polygon) <= minimumWidth * minimumWidth) return false;
        float minimumEdge = MathF.Max(4 * BrushGeometry.PlaneTolerance, 2 * BrushGeometry.PointTolerance);
        for (int index = 0; index < polygon.Length; index++)
        {
            Vector2 first = polygon[index], second = polygon[(index + 1) % polygon.Length];
            Vector2 edge = second - first;
            float length = edge.Length();
            if (!float.IsFinite(length) || length < minimumEdge) return false;
            Vector2 previous = polygon[(index + polygon.Length - 1) % polygon.Length];
            double cornerDistance = Cross(first - previous, second - first) /
                Vector2.Distance(previous, second);
            if (cornerDistance < 2 * BrushGeometry.PlaneTolerance) return false;
            double greatestDistance = 0;
            foreach (Vector2 vertex in polygon)
                greatestDistance = Math.Max(greatestDistance, Cross(edge, vertex - first) / length);
            if (greatestDistance < minimumWidth) return false;
        }
        return true;
    }

    private static Vector2 Center(Vector2[] polygon)
    {
        Vector2 center = Vector2.Zero;
        foreach (Vector2 point in polygon) center += point;
        return center / polygon.Length;
    }

    private static double Area(Vector2[] polygon)
    {
        double twiceArea = 0;
        for (int index = 0; index < polygon.Length; index++)
            twiceArea += Cross(polygon[index], polygon[(index + 1) % polygon.Length]);
        return twiceArea * 0.5;
    }

    private static double Dot(Vector2 a, Vector2 b) => (double)a.X * b.X + (double)a.Y * b.Y;
    private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;

    private static void Mix(ref uint state, float value)
    {
        state = (state ^ (uint)BitConverter.SingleToInt32Bits(value)) * 16777619;
    }

    private static float Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00ffffff) / 16777216f;
    }
}
