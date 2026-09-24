using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal static class CameraPicking
{
    internal static object? Pick(EditorSession session, CameraNavigation camera, Point point, Size size, EditorTool tool)
        => PickAll(session, camera, point, size, tool).FirstOrDefault().Item;

    internal static IReadOnlyList<(object Item, string Label)> PickAll(EditorSession session, CameraNavigation camera,
        Point point, Size size, EditorTool tool)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return [];
        float x = (float)(point.X / size.Width * 2 - 1), y = (float)(1 - point.Y / size.Height * 2);
        var (origin, direction) = camera.PickRay(x, y, (float)(size.Width / size.Height));
        var hits = new Dictionary<object, (float Distance, string Label)>();
        EditorScene scene = session.Scene;
        MapDocument document = scene.Document;
        foreach (var brush in document.Brushes)
        foreach (var polygon in brush.GetPolygons())
        for (int i = 1; i < polygon.Vertices.Length - 1; i++)
            Consider(tool == EditorTool.Face ? new BrushFaceSelection(brush, polygon.Face) : brush,
                polygon.Vertices[0], polygon.Vertices[i], polygon.Vertices[i + 1], polygon.Face.Material);
        foreach (var terrain in document.Terrains)
        {
            MapTerrain surface = terrain.GetSurface();
            foreach (var (a, b, c) in surface.GetTriangles())
                Consider(terrain, surface.Vertices[a], surface.Vertices[b], surface.Vertices[c], terrain.Material);
        }
        foreach (var entity in document.Entities.Where(PointEntityGeometry.IsPointEntity))
        {
            bool modelEntity = XModelGeometry.IsModel(entity);
            if (modelEntity && scene.ResolveModel?.Invoke(entity.Properties["model"]) is { } model)
                foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                    Consider(entity, triangle.A.Position, triangle.B.Position, triangle.C.Position);
            else if (!modelEntity && entity.ClassName == "trigger_radius")
                foreach (var triangle in PointEntityGeometry.GetRadiusTriangles(entity))
                    Consider(entity, triangle.A, triangle.B, triangle.C);
            else
                foreach (var polygon in PointEntityGeometry.CreateBrush(entity).GetPolygons())
                    for (int i = 1; i < polygon.Vertices.Length - 1; i++)
                        Consider(entity, polygon.Vertices[0], polygon.Vertices[i], polygon.Vertices[i + 1]);
        }
        return hits.OrderBy(hit => hit.Value.Distance).Select(hit => (hit.Key, hit.Value.Label)).ToArray();

        void Consider(object item, Vector3 a, Vector3 b, Vector3 c, string? material = null)
        {
            if (!scene.CanSelect(item)) return;
            object owner = scene.Owner(item);
            if (SurfaceRaycast.RayTriangle(origin, direction, a, b, c, out float distance) &&
                InCubicClip(session, origin + direction * distance, origin) &&
                distance >= camera.NearPlane && (!hits.TryGetValue(owner, out var previous) || distance < previous.Distance))
            {
                string label = owner switch
                {
                    MapEntity entity when PrefabLibrary.IsPrefab(entity) => $"Prefab — {entity.Properties.GetValueOrDefault("model", "")}",
                    MapEntity entity when XModelGeometry.IsModel(entity) => $"Model — {entity.Properties.GetValueOrDefault("model", "")}",
                    MapEntity entity => entity.ClassName + (material is null ? "" : $" — {material}"),
                    BrushFaceSelection => $"Face — {material}",
                    MapTerrain terrain => $"{(terrain.IsCurve ? "Patch" : "Terrain")} — {material}",
                    _ => $"Brush — {material}"
                };
                hits[owner] = (distance, label);
            }
        }
    }

    internal static object? PickVertex(EditorSession session, CameraNavigation camera, Point point, Size size)
    {
        float closestDepth = float.PositiveInfinity;
        double closestScreen = 9 * 9;
        object? result = null;
        foreach (object handle in SelectionGeometry.GetVertexHandles(session.Selection))
            if (session.Visibility.CanSelect(session.Document, handle) && SelectionGeometry.Bounds(handle) is { } bounds)
                Consider(handle, bounds.Min);
        return result;

        void Consider(object item, Vector3 position)
        {
            if (!Project(camera, position, size, out Point screen, out float depth)) return;
            if (!InCubicClip(session, position, camera.Eye)) return;
            double distance = ((Avalonia.Vector)(screen - point)).SquaredLength;
            if (distance > closestScreen || Math.Abs(distance - closestScreen) < 0.25 && depth >= closestDepth) return;
            closestScreen = distance;
            closestDepth = depth;
            result = item;
        }
    }

    internal static int PickGizmo(EditorSession session, (Vector3 Min, Vector3 Max) bounds, TransformMode mode,
        CameraNavigation camera, Point point, Size size)
    {
        double closest = 9;
        float closestDepth = float.PositiveInfinity;
        int result = 0;
        Vector3 center = bounds.Min + (bounds.Max - bounds.Min) / 2;
        if (mode != TransformMode.Rotate && InCubicClip(session, center, camera.Eye) &&
            Project(camera, center, size, out Point screenCenter, out _) &&
            ((Avalonia.Vector)(point - screenCenter)).Length < 10)
            return 0;
        foreach (var line in TransformGizmoGeometry.GetLines(bounds, mode))
        {
            if (!InCubicClip(session, line.A, camera.Eye) && !InCubicClip(session, line.B, camera.Eye)) continue;
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

    internal static bool InCubicClip(EditorSession session, Vector3 position, Vector3 center)
    {
        if (!session.CubicClipEnabled) return true;
        Vector3 offset = Vector3.Abs(position - center);
        return offset.X <= session.CubicClipDistance && offset.Y <= session.CubicClipDistance &&
            offset.Z <= session.CubicClipDistance;
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
