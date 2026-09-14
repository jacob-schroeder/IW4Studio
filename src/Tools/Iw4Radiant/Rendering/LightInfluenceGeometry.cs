using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Rendering;

internal static class LightInfluenceGeometry
{
    private const int Segments = 48;

    internal static IEnumerable<(Vector3 A, Vector3 B)> GetLines(EditorScene scene, MapEntity entity)
    {
        if (!MapLight.TryCreate(entity, scene.ResolveTargets(entity), out MapLight light, out _)) yield break;
        if (!light.IsSpotlight)
        {
            foreach (var line in Ring(light.Origin, Vector3.UnitX, Vector3.UnitY, light.Radius)) yield return line;
            foreach (var line in Ring(light.Origin, Vector3.UnitX, Vector3.UnitZ, light.Radius)) yield return line;
            foreach (var line in Ring(light.Origin, Vector3.UnitY, Vector3.UnitZ, light.Radius)) yield return line;
            yield break;
        }

        Vector3 reference = Math.Abs(light.Direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(light.Direction, reference));
        Vector3 up = Vector3.Cross(right, light.Direction);
        Vector3 center = light.Origin + light.Direction * (light.Radius * MathF.Cos(light.OuterAngle));
        float radius = light.Radius * Math.Max(0, MathF.Sin(light.OuterAngle));
        foreach (var line in Ring(center, right, up, radius)) yield return line;
        for (int i = 0; i < 4; i++)
        {
            Vector3 end = CirclePoint(center, right, up, radius, i * MathF.PI / 2);
            if (Finite(end)) yield return (light.Origin, end);
        }
        if (light.InnerAngle <= 0) yield break;
        center = light.Origin + light.Direction * (light.Radius * MathF.Cos(light.InnerAngle));
        radius = light.Radius * Math.Max(0, MathF.Sin(light.InnerAngle));
        foreach (var line in Ring(center, right, up, radius)) yield return line;
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
