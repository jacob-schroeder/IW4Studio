using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;
using Vector = Avalonia.Vector;

namespace Iw4Radiant.Viewports.Orthographic;

internal static class OrthographicGeometry
{
    internal static object? HitTest(EditorSession? session, OrthographicProjection projection, Point point, bool terrainsOnly = false)
    {
        if (session is null) return null;
        object? best = null;
        double bestScore = double.PositiveInfinity;
        foreach ((object item, Point[] polygon) in ProjectedPolygons(session, projection, terrainsOnly))
        {
            double distance = double.PositiveInfinity;
            for (int i = 0; i < polygon.Length; i++)
                distance = Math.Min(distance, DistanceToSegment(point, polygon[i], polygon[(i + 1) % polygon.Length]));
            double score = distance <= 7 ? distance : Contains(polygon, point) ? 12 + Math.Min(2, distance / 1000) : double.PositiveInfinity;
            if (score < bestScore)
            {
                bestScore = score;
                best = item;
            }
        }
        return best;
    }

    internal static IEnumerable<object> HitTestSegment(EditorSession session, OrthographicProjection projection, Point start, Point end)
    {
        var hits = new HashSet<object>();
        foreach ((object item, Point[] polygon) in ProjectedPolygons(session, projection, terrainsOnly: false))
        {
            if (hits.Contains(item)) continue;
            bool hit = Contains(polygon, start) || Contains(polygon, end);
            for (int i = 0; i < polygon.Length && !hit; i++)
            {
                Point a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                hit = Intersects(start, end, a, b) || DistanceToSegment(start, a, b) <= 7 ||
                    DistanceToSegment(end, a, b) <= 7 || DistanceToSegment(a, start, end) <= 7 ||
                    DistanceToSegment(b, start, end) <= 7;
            }
            if (hit && hits.Add(item)) yield return item;
        }
    }

    private static bool Intersects(Point start, Point end, Point a, Point b)
    {
        Vector path = end - start, edge = b - a, offset = a - start;
        double cross = path.X * edge.Y - path.Y * edge.X;
        if (Math.Abs(cross) < 0.000001) return false;
        double t = (offset.X * edge.Y - offset.Y * edge.X) / cross;
        double u = (offset.X * path.Y - offset.Y * path.X) / cross;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
    }

    private static IEnumerable<(object Item, Point[] Polygon)> ProjectedPolygons(EditorSession session,
        OrthographicProjection projection, bool terrainsOnly)
    {
        EditorScene scene = session.Scene;
        if (!terrainsOnly)
            foreach (var brush in scene.Document.Brushes)
            {
                if (!scene.CanSelect(brush)) continue;
                yield return (scene.Owner(brush), ConvexHull(brush.GetPolygons()
                    .SelectMany(polygon => polygon.Vertices).Select(projection.ToScreen)));
            }
        foreach (var terrain in scene.Document.Terrains)
        {
            if (!scene.CanSelect(terrain)) continue;
            if (terrainsOnly && scene.Owner(terrain) is not MapTerrain) continue;
            if (terrainsOnly && terrain.IsCurve && session.SculptMode is not (TerrainSculptMode.PaintColor or TerrainSculptMode.PaintAlpha)) continue;
            MapTerrain surface = terrain.GetSurface();
            // Triangles also pick folded or nonrectangular native terrain correctly.
            foreach (var (a, b, c) in surface.GetTriangles())
                yield return (scene.Owner(terrain), [projection.ToScreen(surface.Vertices[a]),
                    projection.ToScreen(surface.Vertices[b]), projection.ToScreen(surface.Vertices[c])]);
        }
        if (!terrainsOnly)
            foreach (var entity in scene.Document.Entities.Where(PointEntityGeometry.IsPointEntity))
            {
                if (!scene.CanSelect(entity)) continue;
                bool modelEntity = XModelGeometry.IsModel(entity);
                if (modelEntity && scene.ResolveModel?.Invoke(entity.Properties["model"]) is { } model)
                    foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                        yield return (scene.Owner(entity), [projection.ToScreen(triangle.A.Position),
                            projection.ToScreen(triangle.B.Position), projection.ToScreen(triangle.C.Position)]);
                else if (!modelEntity && entity.ClassName == "trigger_radius")
                    yield return (scene.Owner(entity), ConvexHull(PointEntityGeometry.GetRadiusLines(entity)
                        .SelectMany(line => new[] { projection.ToScreen(line.A), projection.ToScreen(line.B) })));
                else if (scene.Bounds(entity) is { } bounds)
                    yield return (scene.Owner(entity), Corners(projection.ScreenBounds(bounds.Min, bounds.Max)));
            }
    }

    internal static Rect Rectangle(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    internal static Point[] Corners(Rect rect) => [rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft];
    internal static double DistanceToSegment(Point point, Point a, Point b)
    {
        Vector edge = b - a;
        double lengthSquared = edge.X * edge.X + edge.Y * edge.Y;
        if (lengthSquared < 0.000001) return Distance(point, a);
        Vector relative = point - a;
        double t = Math.Clamp((relative.X * edge.X + relative.Y * edge.Y) / lengthSquared, 0, 1);
        return Distance(point, a + edge * t);
    }

    internal static double Distance(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    internal static bool Contains(Point[] polygon, Point point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                inside = !inside;
        return inside;
    }

    internal static Point[] ConvexHull(IEnumerable<Point> input)
    {
        Point[] points = input.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (points.Length < 3) return points;
        static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var hull = new List<Point>();
        foreach (Point point in points)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        int lowerCount = hull.Count;
        for (int i = points.Length - 2; i >= 0; i--)
        {
            Point point = points[i];
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }
}
