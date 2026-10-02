using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Rendering;

internal static class PointEntityGeometry
{
    private static readonly HashSet<string> SpawnClasses = GameplayEntityEditing.Types
        .Where(type => type.Category == "Spawns").Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

    internal static bool IsPointEntity(MapEntity entity) => entity.ClassName != "worldspawn" &&
        entity.Brushes.Count == 0 && entity.Terrains.Count == 0 && entity.PreservedPrimitives.Count == 0;

    internal static MapBrush CreateBrush(MapEntity entity)
    {
        var bounds = EditorSession.EntityBounds(entity);
        return MapBrush.CreateBox(bounds.Min, bounds.Max, "");
    }

    internal static Vector3[] GetSpawnArrow(MapEntity entity)
    {
        if (!SpawnClasses.Contains(entity.ClassName)) return [];
        Vector3 angles;
        try { angles = EntityOrientation.Read(entity); }
        catch (ArgumentException) { return []; }
        var (min, max) = EditorSession.EntityBounds(entity);
        float radius = MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f;
        // The top-face marker shows heading (yaw), keeping pitch and roll out of its flat footprint.
        Vector3 forward = EntityOrientation.Forward(new Vector3(0, angles.Y, 0));
        Vector3 side = new(-forward.Y, forward.X, 0);
        Vector3 center = new((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, max.Z + 0.05f);
        Vector3 neck = center + forward * (radius * 0.1f);
        Vector3 tail = center - forward * (radius * 0.65f);
        return
        [
            center + forward * (radius * 0.85f),
            neck + side * (radius * 0.5f),
            neck + side * (radius * 0.18f),
            tail + side * (radius * 0.18f),
            tail - side * (radius * 0.18f),
            neck - side * (radius * 0.18f),
            neck - side * (radius * 0.5f)
        ];
    }

    internal static IEnumerable<(Vector3 A, Vector3 B)> GetMistGuideLines(MapEntity entity)
    {
        Vector3 origin = EditorSession.EntityOrigin(entity);
        const float radius = 5;
        yield return (origin - Vector3.UnitX * radius, origin + Vector3.UnitX * radius);
        yield return (origin - Vector3.UnitY * radius, origin + Vector3.UnitY * radius);
        yield return (origin - Vector3.UnitZ * radius, origin + Vector3.UnitZ * radius);
        for (int index = 0; index < 8; index++)
        {
            float angle = index * MathF.Tau / 8;
            float nextAngle = (index + 1) * MathF.Tau / 8;
            yield return (origin + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * radius,
                origin + new Vector3(MathF.Cos(nextAngle), MathF.Sin(nextAngle), 0) * radius);
        }
    }

    internal static bool TryRadiusBounds(MapEntity entity, out (Vector3 Min, Vector3 Max) bounds)
    {
        bounds = default;
        if (!GameplayEntityEditing.TryRadiusDimensions(entity, out float radius, out float height)) return false;
        Vector3 origin = EditorSession.EntityOrigin(entity);
        // PS3 SP_trigger_radius (0x1bb860): midpoint.z = halfSize.z = height * 0.5.
        bounds = (origin - new Vector3(radius, radius, 0), origin + new Vector3(radius, radius, height));
        return Finite(bounds.Min) && Finite(bounds.Max);

        static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    internal static IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> GetRadiusTriangles(MapEntity entity)
    {
        if (!TryRadiusBounds(entity, out var bounds)) yield break;
        Vector3 bottom = EditorSession.EntityOrigin(entity), top = new(bottom.X, bottom.Y, bounds.Max.Z);
        foreach (var edge in RadiusEdges(entity, bounds))
        {
            yield return (bottom, edge.BottomB, edge.BottomA);
            yield return (top, edge.TopA, edge.TopB);
            yield return (edge.BottomA, edge.BottomB, edge.TopB);
            yield return (edge.BottomA, edge.TopB, edge.TopA);
        }
    }

    internal static IEnumerable<(Vector3 A, Vector3 B)> GetRadiusLines(MapEntity entity)
    {
        if (!TryRadiusBounds(entity, out var bounds)) yield break;
        foreach (var edge in RadiusEdges(entity, bounds))
        {
            yield return (edge.BottomA, edge.BottomB);
            yield return (edge.TopA, edge.TopB);
            yield return (edge.BottomA, edge.TopA);
        }
    }

    internal static IEnumerable<(Vector3 A, Vector3 B)> GetSoundRangeLines(MapEntity entity)
    {
        if (entity.ClassName != "fx_origin" || entity.Properties.GetValueOrDefault("is_sound") != "1") yield break;
        SoundEmitterSettings settings;
        try { settings = SoundEmitterSettings.Read(entity); }
        catch (ArgumentException) { yield break; }
        Vector3 origin = EditorSession.EntityOrigin(entity);
        // Only authored overrides are available to scene geometry; do not invent library defaults.
        foreach (float? range in new[] { settings.DistanceMin, settings.DistanceMax })
        {
            if (range is not > 0) continue;
            float radius = range.Value;
            for (int plane = 0; plane < 3; plane++)
            for (int index = 0; index < 48; index++)
                yield return (Point(index, plane), Point(index + 1, plane));

            Vector3 Point(int index, int plane)
            {
                float angle = index * MathF.Tau / 48;
                float a = MathF.Cos(angle) * radius, b = MathF.Sin(angle) * radius;
                return origin + (plane switch
                {
                    0 => new Vector3(a, b, 0),
                    1 => new Vector3(a, 0, b),
                    _ => new Vector3(0, a, b)
                });
            }
        }
    }

    private static IEnumerable<(Vector3 BottomA, Vector3 BottomB, Vector3 TopA, Vector3 TopB)> RadiusEdges(
        MapEntity entity, (Vector3 Min, Vector3 Max) bounds)
    {
        // Display tessellation only; the authored radius and height remain native entity fields.
        const int segments = 32;
        Vector3 origin = EditorSession.EntityOrigin(entity);
        float radius = bounds.Max.X - origin.X;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = Point(i), b = Point((i + 1) % segments);
            yield return (a, b, new(a.X, a.Y, bounds.Max.Z), new(b.X, b.Y, bounds.Max.Z));
        }

        Vector3 Point(int index)
        {
            float angle = index * MathF.Tau / segments;
            return origin + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
        }
    }
}
