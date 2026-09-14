using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Rendering;

internal static class PointEntityGeometry
{
    internal static bool IsPointEntity(MapEntity entity) => entity.ClassName != "worldspawn" &&
        entity.Brushes.Count == 0 && entity.Terrains.Count == 0 && entity.PreservedPrimitives.Count == 0;

    internal static MapBrush CreateBrush(MapEntity entity)
    {
        var bounds = EditorSession.EntityBounds(entity);
        return MapBrush.CreateBox(bounds.Min, bounds.Max, "");
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
