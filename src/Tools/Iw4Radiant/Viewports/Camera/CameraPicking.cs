using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal static class CameraPicking
{
    internal static object? Pick(MapDocument document, CameraNavigation camera, Point point, Size size, EditorTool tool)
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
            Consider(tool == EditorTool.Face ? new BrushFaceSelection(brush, polygon.Face) : brush,
                polygon.Vertices[0], polygon.Vertices[i], polygon.Vertices[i + 1]);
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

    internal static object? PickVertex(EditorSelection selection, CameraNavigation camera, Point point, Size size)
    {
        float closestDepth = float.PositiveInfinity;
        double closestScreen = 9 * 9;
        object? result = null;
        foreach (object handle in SelectionGeometry.GetVertexHandles(selection))
            if (SelectionGeometry.Bounds(handle) is { } bounds) Consider(handle, bounds.Min);
        return result;

        void Consider(object item, Vector3 position)
        {
            if (!Project(camera, position, size, out Point screen, out float depth)) return;
            double distance = ((Avalonia.Vector)(screen - point)).SquaredLength;
            if (distance > closestScreen || Math.Abs(distance - closestScreen) < 0.25 && depth >= closestDepth) return;
            closestScreen = distance;
            closestDepth = depth;
            result = item;
        }
    }

    internal static int PickGizmo((Vector3 Min, Vector3 Max) bounds, TransformMode mode,
        CameraNavigation camera, Point point, Size size)
    {
        double closest = 9;
        float closestDepth = float.PositiveInfinity;
        int result = 0;
        Vector3 center = bounds.Min + (bounds.Max - bounds.Min) / 2;
        if (mode != TransformMode.Rotate && Project(camera, center, size, out Point screenCenter, out _) &&
            ((Avalonia.Vector)(point - screenCenter)).Length < 10)
            return 0;
        foreach (var line in TransformGizmoGeometry.GetLines(bounds, mode))
        {
            if (!Project(camera, line.A, size, out Point a, out float depthA) ||
                !Project(camera, line.B, size, out Point b, out float depthB)) continue;
            Avalonia.Vector segment = b - a;
            double squaredLength = segment.SquaredLength;
            if (squaredLength < 0.01) continue;
            double t = Math.Clamp(((point.X - a.X) * segment.X + (point.Y - a.Y) * segment.Y) / squaredLength, 0, 1);
            double distance = ((Avalonia.Vector)(point - (a + segment * t))).Length;
            float depth = depthA + (depthB - depthA) * (float)t;
            if (distance > closest || Math.Abs(distance - closest) < 0.25 && depth >= closestDepth) continue;
            closest = distance;
            closestDepth = depth;
            result = line.Axis;
        }
        return result;
    }

    internal static bool Project(CameraNavigation camera, Vector3 position, Size size, out Point point, out float depth)
    {
        point = default;
        depth = 0;
        if (size.Width <= 0 || size.Height <= 0) return false;
        Vector4 clip = Vector4.Transform(new Vector4(position, 1), camera.ViewProjection((float)(size.Width / size.Height)));
        if (!float.IsFinite(clip.W) || clip.W <= 0) return false;
        depth = clip.Z / clip.W;
        double x = clip.X / clip.W, y = clip.Y / clip.W;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !float.IsFinite(depth) || depth < -1 || depth > 1) return false;
        point = new Point((x + 1) * size.Width / 2, (1 - y) * size.Height / 2);
        return true;
    }
}
