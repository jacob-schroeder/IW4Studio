using System.Numerics;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Rendering;

internal static class TransformGizmoGeometry
{
    internal static float GetSize((Vector3 Min, Vector3 Max) bounds)
    {
        Vector3 size = bounds.Max - bounds.Min;
        return Math.Clamp(Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.4f, 24, 256);
    }

    internal static IEnumerable<(Vector3 A, Vector3 B, Vector3 Color, int Axis)> GetLines(
        (Vector3 Min, Vector3 Max) bounds, TransformMode mode)
    {
        Vector3 center = bounds.Min + (bounds.Max - bounds.Min) / 2;
        float size = GetSize(bounds);
        Vector3[] axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        Vector3[] colors = [new(1, 0.25f, 0.2f), new(0.25f, 1, 0.35f), new(0.25f, 0.55f, 1)];
        for (int index = 0; index < axes.Length; index++)
        {
            int axis = index + 1;
            Vector3 direction = axes[index], side = axes[(index + 1) % 3], other = axes[(index + 2) % 3];
            Vector3 color = colors[index];
            if (mode == TransformMode.Rotate)
            {
                const int segments = 64;
                for (int segment = 0; segment < segments; segment++)
                {
                    float a = segment * (2 * MathF.PI / segments), b = (segment + 1) * (2 * MathF.PI / segments);
                    yield return (center + size * (side * MathF.Cos(a) + other * MathF.Sin(a)),
                        center + size * (side * MathF.Cos(b) + other * MathF.Sin(b)), color, axis);
                }
                continue;
            }
            Vector3 tip = center + direction * size;
            yield return (center, tip, color, axis);
            if (mode == TransformMode.Scale)
            {
                foreach (var edge in Handle(tip, size * 0.07f)) yield return (edge.A, edge.B, color, axis);
            }
            else
            {
                foreach (Vector3 wing in new[] { side, -side, other, -other })
                    yield return (tip, tip - direction * size * 0.2f + wing * size * 0.08f, color, axis);
            }
        }
        if (mode == TransformMode.Scale)
        {
            Vector3 tip = center + new Vector3(size * 0.45f), color = new(1, 0.9f, 0.45f);
            yield return (center, tip, color, 4);
            foreach (var edge in Handle(tip, size * 0.09f)) yield return (edge.A, edge.B, color, 4);
        }
    }

    private static IEnumerable<(Vector3 A, Vector3 B)> Handle(Vector3 center, float radius)
    {
        for (int axis = 0; axis < 3; axis++)
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
        {
            Vector3 offset = axis switch
            {
                0 => new(0, a * radius, b * radius),
                1 => new(a * radius, 0, b * radius),
                _ => new(a * radius, b * radius, 0)
            };
            Vector3 along = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            yield return (center + offset - along * radius, center + offset + along * radius);
        }
    }
}
