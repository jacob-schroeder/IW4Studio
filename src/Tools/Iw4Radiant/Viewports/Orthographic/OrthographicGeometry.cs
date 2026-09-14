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
        EditorScene scene = session.Scene;
        object? best = null;
        double bestScore = double.PositiveInfinity;
        void Consider(object item, Point[] polygon)
        {
            if (polygon.Length == 0 || !scene.CanSelect(item)) return;
            double distance = double.PositiveInfinity;
            for (int i = 0; i < polygon.Length; i++)
                distance = Math.Min(distance, DistanceToSegment(point, polygon[i], polygon[(i + 1) % polygon.Length]));
            double score = distance <= 7 ? distance : Contains(polygon, point) ? 12 + Math.Min(2, distance / 1000) : double.PositiveInfinity;
            if (score < bestScore)
            {
                bestScore = score;
                best = scene.Owner(item);
            }
        }
        if (!terrainsOnly)
            foreach (var brush in scene.Document.Brushes)
                Consider(brush, ConvexHull(brush.GetPolygons().SelectMany(polygon => polygon.Vertices).Select(projection.ToScreen)));
        foreach (var terrain in scene.Document.Terrains)
        {
            if (terrainsOnly && scene.Owner(terrain) is not MapTerrain) continue;
            if (terrainsOnly && terrain.IsCurve && session.SculptMode is not (TerrainSculptMode.PaintColor or TerrainSculptMode.PaintAlpha)) continue;
            MapTerrain surface = terrain.GetSurface();
            // Triangles also pick folded or nonrectangular native terrain correctly.
            foreach (var (a, b, c) in surface.GetTriangles())
                Consider(terrain, [projection.ToScreen(surface.Vertices[a]), projection.ToScreen(surface.Vertices[b]), projection.ToScreen(surface.Vertices[c])]);
        }
        if (!terrainsOnly)
            foreach (var entity in scene.Document.Entities.Where(PointEntityGeometry.IsPointEntity))
            {
                if (XModelGeometry.IsModel(entity))
                {
                    if (scene.ResolveModel?.Invoke(entity.Properties["model"]) is { } model)
                        foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                            Consider(entity, [projection.ToScreen(triangle.A.Position), projection.ToScreen(triangle.B.Position), projection.ToScreen(triangle.C.Position)]);
                }
                else if (entity.ClassName == "trigger_radius")
                    Consider(entity, ConvexHull(PointEntityGeometry.GetRadiusLines(entity)
                        .SelectMany(line => new[] { projection.ToScreen(line.A), projection.ToScreen(line.B) })));
                else if (scene.Bounds(entity) is { } bounds)
                    Consider(entity, Corners(projection.ScreenBounds(bounds.Min, bounds.Max)));
            }
        return best;
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
