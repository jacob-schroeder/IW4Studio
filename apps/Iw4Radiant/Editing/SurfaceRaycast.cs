using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal static class SurfaceRaycast
{
    internal static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
        out float distance)
    {
        distance = 0;
        // Thin terrain and model triangles need double intermediates for the
        // hit predicate, even though callers store the final distance as float.
        double e1x = (double)b.X - a.X, e1y = (double)b.Y - a.Y, e1z = (double)b.Z - a.Z;
        double e2x = (double)c.X - a.X, e2y = (double)c.Y - a.Y, e2z = (double)c.Z - a.Z;
        double px = direction.Y * e2z - direction.Z * e2y;
        double py = direction.Z * e2x - direction.X * e2z;
        double pz = direction.X * e2y - direction.Y * e2x;
        double determinant = e1x * px + e1y * py + e1z * pz;
        if (Math.Abs(determinant) < 0.000001) return false;
        double tx = (double)origin.X - a.X, ty = (double)origin.Y - a.Y, tz = (double)origin.Z - a.Z;
        double u = (tx * px + ty * py + tz * pz) / determinant;
        if (u < 0 || u > 1) return false;
        double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
        double v = (direction.X * qx + direction.Y * qy + direction.Z * qz) / determinant;
        if (v < 0 || u + v > 1) return false;
        distance = (float)((e2x * qx + e2y * qy + e2z * qz) / determinant);
        if (!float.IsFinite(distance) || distance < 0) return false;
        return true;
    }

    internal static bool TryHit(MapDocument document, Vector3 origin, Vector3 direction,
        Func<string, XModelSource?> resolveModel, Func<string, MaterialSource?>? resolveMaterial,
        IReadOnlySet<MapEntity>? excluded, out Vector3 point, out Vector3 normal)
        => TryHit(document, origin, direction, resolveModel, resolveMaterial, excluded, includeModels: true,
            out point, out normal);

    internal static bool TryHitSurfaces(MapDocument document, Vector3 origin, Vector3 direction,
        Func<string, MaterialSource?>? resolveMaterial, out Vector3 point, out Vector3 normal)
        => TryHit(document, origin, direction, _ => null, resolveMaterial, null, includeModels: false,
            out point, out normal);

    private static bool TryHit(MapDocument document, Vector3 origin, Vector3 direction,
        Func<string, XModelSource?> resolveModel, Func<string, MaterialSource?>? resolveMaterial,
        IReadOnlySet<MapEntity>? excluded, bool includeModels, out Vector3 point, out Vector3 normal)
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
            if (includeModels && XModelGeometry.IsModel(entity) && resolveModel(entity.Properties["model"]) is { } model)
                foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                    Consider(triangle.A.Position, triangle.B.Position, triangle.C.Position, triangle.Material);
        }
        normal = hitNormal;
        point = float.IsFinite(closest) ? origin + direction * closest : Vector3.Zero;
        return float.IsFinite(closest);

        void Consider(Vector3 a, Vector3 b, Vector3 c, string material)
        {
            if (ClipBrushMaterial.IsCollisionOnly(material)) return;
            if (resolveMaterial?.Invoke(material) is { IsSky: true }) return;
            if (!RayTriangle(origin, direction, a, b, c, out float distance) || distance >= closest) return;
            Vector3 triangleNormal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (Vector3.Dot(triangleNormal, direction) > 0) triangleNormal = -triangleNormal;
            closest = distance;
            hitNormal = triangleNormal;
        }
    }
}
