using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Rendering;

internal static class LightInfluenceGeometry
{
    private const int Segments = 48;
    private static readonly Vector3 RadiusColor = new(1, 0.85f, 0.35f);
    private static readonly Vector3 SweepColor = new(0.72f, 0.35f, 1);
    internal static float SweepHandleDistance(MapLight light) => MathF.Min(light.Radius, 64);

    internal static IEnumerable<(Vector3 A, Vector3 B, Vector3 Color)> GetLines(EditorScene scene, MapEntity entity)
    {
        if (!MapLight.TryCreate(entity, scene.ResolveTargets(entity), out MapLight light, out _)) yield break;
        if (!light.IsSpotlight)
        {
            foreach (var line in Ring(light.Origin, Vector3.UnitX, Vector3.UnitY, light.Radius)) yield return (line.A, line.B, RadiusColor);
            foreach (var line in Ring(light.Origin, Vector3.UnitX, Vector3.UnitZ, light.Radius)) yield return (line.A, line.B, RadiusColor);
            foreach (var line in Ring(light.Origin, Vector3.UnitY, Vector3.UnitZ, light.Radius)) yield return (line.A, line.B, RadiusColor);
            yield break;
        }

        if (light.IsMoving)
        {
            float distance = SweepHandleDistance(light);
            Vector3 previous = default;
            for (int segment = 0; segment <= 24; segment++)
            {
                float offset = -light.SweepAngle / 2 + light.SweepAngle * segment / 24;
                Vector3 point = light.Origin + light.SweepDirection(offset) * distance;
                if (segment > 0 && Finite(previous) && Finite(point)) yield return (previous, point, SweepColor);
                previous = point;
            }
            foreach (Vector3 direction in new[] { light.SweepStartDirection, light.SweepEndDirection })
            {
                Vector3 endpoint = light.Origin + direction * distance;
                if (!Finite(endpoint)) continue;
                yield return (light.Origin, endpoint, SweepColor);
                const float handleSize = 5;
                yield return (endpoint - Vector3.UnitZ * handleSize, endpoint + Vector3.UnitZ * handleSize, Vector3.Zero);
                yield return (endpoint - Vector3.UnitX * handleSize, endpoint + Vector3.UnitX * handleSize, Vector3.Zero);
                yield return (endpoint - Vector3.UnitY * handleSize, endpoint + Vector3.UnitY * handleSize, Vector3.Zero);
            }
        }

        foreach (var line in GetConeRing(light, inner: false)) yield return (line.A, line.B, RadiusColor);
        Vector3 reference = Math.Abs(light.Direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(light.Direction, reference));
        Vector3 up = Vector3.Cross(right, light.Direction);
        Vector3 center = light.Origin + light.Direction * (light.Radius * MathF.Cos(light.OuterAngle));
        float radius = light.Radius * Math.Max(0, MathF.Sin(light.OuterAngle));
        for (int i = 0; i < 4; i++)
        {
            Vector3 end = CirclePoint(center, right, up, radius, i * MathF.PI / 2);
            if (Finite(end)) yield return (light.Origin, end, RadiusColor);
        }
        // Keep the guide vertex count stable as the inner cone reaches zero.
        foreach (var line in GetConeRing(light, inner: true)) yield return (line.A, line.B, RadiusColor);
        Vector3 innerCenter = light.Origin + light.Direction * (light.Radius * MathF.Cos(light.InnerAngle));
        float markerSize = light.InnerAngle <= 0 ? MathF.Min(light.Radius * 0.05f, 5) : 0;
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            yield return (innerCenter - axis * markerSize, innerCenter + axis * markerSize, RadiusColor);
    }

    internal static IEnumerable<(Vector3 A, Vector3 B)> GetConeRing(MapLight light, bool inner)
    {
        Vector3 reference = Math.Abs(light.Direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(light.Direction, reference));
        Vector3 up = Vector3.Cross(right, light.Direction);
        float angle = inner ? light.InnerAngle : light.OuterAngle;
        Vector3 center = light.Origin + light.Direction * (light.Radius * MathF.Cos(angle));
        float radius = light.Radius * Math.Max(0, MathF.Sin(angle));
        return Ring(center, right, up, radius);
    }

    private static IEnumerable<(Vector3 A, Vector3 B)> Ring(Vector3 center, Vector3 right, Vector3 up, float radius)
    {
        for (int i = 0; i < Segments; i++)
        {
            Vector3 a = CirclePoint(center, right, up, radius, i * MathF.Tau / Segments);
            Vector3 b = CirclePoint(center, right, up, radius, (i + 1) * MathF.Tau / Segments);
            if (Finite(a) && Finite(b)) yield return (a, b);
        }
    }

    private static Vector3 CirclePoint(Vector3 center, Vector3 right, Vector3 up, float radius, float angle) =>
        center + (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius;

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
