using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal static class SurfaceRaycast
{
    internal static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
        out float distance, out Vector3 normal)
    {
        distance = 0;
        normal = Vector3.Zero;
        Vector3 edge1 = b - a, edge2 = c - a;
        Vector3 p = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < 0.000001f) return false;
        Vector3 t = origin - a;
        float u = Vector3.Dot(t, p) / determinant;
        if (u < 0 || u > 1) return false;
        Vector3 q = Vector3.Cross(t, edge1);
        float v = Vector3.Dot(direction, q) / determinant;
        if (v < 0 || u + v > 1) return false;
        distance = Vector3.Dot(edge2, q) / determinant;
        if (!float.IsFinite(distance) || distance < 0) return false;
        normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
        if (Vector3.Dot(normal, direction) > 0) normal = -normal;
        return true;
    }

    internal static bool TryHit(MapDocument document, Vector3 origin, Vector3 direction,
        Func<string, XModelSource?> resolveModel, Func<string, MaterialSource?>? resolveMaterial,
        IReadOnlySet<MapEntity>? excluded, out Vector3 point, out Vector3 normal)
    {
        float closest = float.PositiveInfinity;
        Vector3 hitNormal = Vector3.Zero;
        foreach (var entity in document.Entities)
        {
            if (excluded?.Contains(entity) == true) continue;
            foreach (var brush in entity.Brushes)
            foreach (var polygon in brush.GetPolygons())
                for (int index = 1; index < polygon.Vertices.Length - 1; index++)
                    Consider(polygon.Vertices[0], polygon.Vertices[index], polygon.Vertices[index + 1], polygon.Face.Material);
            foreach (var terrain in entity.Terrains)
            {
                var surface = terrain.GetSurface();
                foreach (var (a, b, c) in surface.GetTriangles())
                    Consider(surface.Vertices[a], surface.Vertices[b], surface.Vertices[c], surface.Material);
            }
            if (XModelGeometry.IsModel(entity) && resolveModel(entity.Properties["model"]) is { } model)
                foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                    Consider(triangle.A.Position, triangle.B.Position, triangle.C.Position, triangle.Material);
        }
        normal = hitNormal;
        point = float.IsFinite(closest) ? origin + direction * closest : Vector3.Zero;
        return float.IsFinite(closest);

        void Consider(Vector3 a, Vector3 b, Vector3 c, string material)
        {
            if (resolveMaterial?.Invoke(material) is { IsSky: true }) return;
            if (!RayTriangle(origin, direction, a, b, c, out float distance, out Vector3 triangleNormal) || distance >= closest) return;
            closest = distance;
            hitNormal = triangleNormal;
        }
    }
}
