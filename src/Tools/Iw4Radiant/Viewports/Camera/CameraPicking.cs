using System.Numerics;
using Avalonia;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal static class CameraPicking
{
    internal static object? Pick(MapDocument document, CameraNavigation camera, Point point, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return null;
        float x = (float)(point.X / size.Width * 2 - 1), y = (float)(1 - point.Y / size.Height * 2);
        var (origin, direction) = camera.PickRay(x, y, (float)(size.Width / size.Height));
        float closest = float.PositiveInfinity;
        object? result = null;
        foreach (var brush in document.Brushes)
        foreach (var polygon in brush.GetPolygons())
        for (int i = 1; i < polygon.Vertices.Length - 1; i++)
            Consider(brush, polygon.Vertices[0], polygon.Vertices[i], polygon.Vertices[i + 1]);
        foreach (var terrain in document.Terrains)
        foreach (var (a, b, c) in terrain.GetTriangles())
            Consider(terrain, terrain.Vertices[a], terrain.Vertices[b], terrain.Vertices[c]);
        foreach (var entity in document.Entities.Where(PointEntityGeometry.IsPointEntity))
        foreach (var polygon in PointEntityGeometry.CreateBrush(entity).GetPolygons())
        for (int i = 1; i < polygon.Vertices.Length - 1; i++)
            Consider(entity, polygon.Vertices[0], polygon.Vertices[i], polygon.Vertices[i + 1]);
        return result;

        void Consider(object item, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (MathF.Abs(determinant) < 0.000001f)
                return;
            Vector3 t = origin - a;
            float u = Vector3.Dot(t, p) / determinant;
            if (u < 0 || u > 1)
                return;
            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1)
                return;
            float distance = Vector3.Dot(edge2, q) / determinant;
            if (distance >= 0.5f && distance < closest)
            {
                closest = distance;
                result = item;
            }
        }
    }
}
