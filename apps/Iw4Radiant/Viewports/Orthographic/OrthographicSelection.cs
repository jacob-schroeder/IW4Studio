using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Orthographic;

internal static class OrthographicSelection
{
    internal sealed record VertexHandle(Vector2 Position, object[] Vertices, bool IsEdge)
    {
        internal float Depth(OrthographicProjection projection) =>
            Vertices.Select(SelectionGeometry.Bounds).OfType<(Vector3 Min, Vector3 Max)>()
                .Select(bounds => projection.MissingAxis(bounds.Min)).DefaultIfEmpty(float.NegativeInfinity).Max();
    }

    internal static VertexHandle? HitTestVertexHandle(EditorSession session, OrthographicProjection projection, Point point)
    {
        VertexHandle? closest = null;
        double distance = 8;
        float depth = float.NegativeInfinity;
        foreach (VertexHandle handle in GetVertexHandles(session, projection))
        {
            double candidate = OrthographicGeometry.Distance(point, projection.ToScreen(handle.Position));
            float candidateDepth = handle.Depth(projection);
            if (candidate > distance || Math.Abs(candidate - distance) < 0.001 && candidateDepth <= depth) continue;
            closest = handle;
            distance = candidate;
            depth = candidateDepth;
        }
        return closest;
    }

    internal static IEnumerable<VertexHandle> GetVertexHandles(EditorSession session, OrthographicProjection projection)
    {
        object[] vertices = SelectionGeometry.GetVertexHandles(session.Selection)
            .Where(handle => session.Visibility.CanSelect(session.Document, handle)).ToArray();
        foreach (var owner in vertices.GroupBy(EditorSelection.Owner, ReferenceEqualityComparer.Instance))
        {
            var corners = new List<(Vector2 Position, List<object> Vertices)>();
            foreach (object vertex in owner)
            {
                if (SelectionGeometry.Bounds(vertex) is not { } bounds) continue;
                Vector2 position = projection.Project(bounds.Min);
                int index = corners.FindIndex(corner =>
                    Vector2.DistanceSquared(corner.Position, position) < BrushGeometry.PointTolerance * BrushGeometry.PointTolerance);
                if (index < 0) corners.Add((position, [vertex]));
                else corners[index].Vertices.Add(vertex);
            }
            foreach (var corner in corners)
                yield return new(corner.Position, corner.Vertices.ToArray(), IsEdge: false);

            if (owner.Key is not MapBrush brush) continue;
            var edges = new HashSet<(int A, int B)>();
            foreach (MapPolygon polygon in brush.GetPolygons())
            for (int index = 0; index < polygon.Vertices.Length; index++)
            {
                int a = FindCorner(projection.Project(polygon.Vertices[index]));
                int b = FindCorner(projection.Project(polygon.Vertices[(index + 1) % polygon.Vertices.Length]));
                if (a < 0 || b < 0 || a == b) continue;
                if (a > b) (a, b) = (b, a);
                if (!edges.Add((a, b))) continue;
                yield return new((corners[a].Position + corners[b].Position) / 2,
                    corners[a].Vertices.Concat(corners[b].Vertices).ToArray(), IsEdge: true);
            }

            int FindCorner(Vector2 position) => corners.FindIndex(corner =>
                Vector2.DistanceSquared(corner.Position, position) < BrushGeometry.PointTolerance * BrushGeometry.PointTolerance);
        }
    }

    internal static object? HitTest(EditorSession session, OrthographicProjection projection, Point point)
    {
        if (session.Tool == EditorTool.Vertex)
            return HitTestVertexHandle(session, projection, point)?.Vertices.FirstOrDefault();
        if (session.Tool != EditorTool.Face) return OrthographicGeometry.HitTest(session, projection, point);
        object? best = null;
        double bestDistance = double.PositiveInfinity;
        float bestDepth = float.NegativeInfinity;
        foreach (MapBrush brush in session.Scene.Document.Brushes)
        foreach (MapPolygon polygon in brush.GetPolygons())
        {
            if (!session.Scene.CanSelect(brush)) continue;
            Point[] points = polygon.Vertices.Select(projection.ToScreen).ToArray();
            double distance = OrthographicGeometry.Contains(points, point) ? 0 : points.Select((p, i) =>
                OrthographicGeometry.DistanceToSegment(point, p, points[(i + 1) % points.Length])).DefaultIfEmpty(double.PositiveInfinity).Min();
            if (distance > 7 || distance > bestDistance) continue;
            Vector3 normal = polygon.Face.Normal;
            float denominator = projection.MissingAxis(normal);
            float depth = Math.Abs(denominator) > 0.00001f ?
                Vector3.Dot(normal, polygon.Face.A - projection.Unproject(projection.ToWorld(point), 0)) / denominator :
                polygon.Vertices.Select(projection.MissingAxis).Average();
            if (distance == bestDistance && depth <= bestDepth) continue;
            best = session.Scene.Owner(new BrushFaceSelection(brush, polygon.Face));
            bestDistance = distance;
            bestDepth = depth;
        }
        return best;
    }

    internal static object[] MarqueeCandidates(EditorSession session)
    {
        IEnumerable<object> candidates = session.Tool switch
        {
            EditorTool.Vertex => SelectionGeometry.GetVertexHandles(session.Selection),
            EditorTool.Select => session.Scene.Document.Brushes.Cast<object>()
                .Concat(session.Scene.Document.Terrains)
                .Concat(session.Scene.Document.Entities.Where(PointEntityGeometry.IsPointEntity)),
            _ => session.Scene.Document.Brushes.SelectMany(brush => brush.GetPolygons()
                .Select(polygon => (object)new BrushFaceSelection(brush, polygon.Face)))
        };
        return candidates.Where(session.Scene.CanSelect).Select(session.Scene.Owner).Distinct().ToArray();
    }

    internal static IEnumerable<object> InRectangle(EditorSession session, IEnumerable<object> candidates,
        OrthographicProjection projection, Rect rectangle, SelectionVolumeMode mode = SelectionVolumeMode.PartialTall)
    {
        Vector2 worldA = projection.ToWorld(rectangle.TopLeft), worldB = projection.ToWorld(rectangle.BottomRight);
        Vector2 worldMin = Vector2.Min(worldA, worldB), worldMax = Vector2.Max(worldA, worldB);
        float depthMin = session.Snap(session.BrushBottom);
        float depthMax = Math.Max(depthMin + session.GridSize, session.Snap(depthMin + session.BrushHeight));
        Vector3 volumeMin = Vector3.Min(projection.Unproject(worldMin, depthMin), projection.Unproject(worldMax, depthMax));
        Vector3 volumeMax = Vector3.Max(projection.Unproject(worldMin, depthMin), projection.Unproject(worldMax, depthMax));
        foreach (object item in candidates)
        {
            if (!session.Scene.CanSelect(item) || session.Scene.Bounds(item) is not { } bounds) continue;
            Rect projected = projection.ScreenBounds(bounds.Min, bounds.Max);
            bool projectedIntersects = projected.Right >= rectangle.Left && projected.Left <= rectangle.Right &&
                projected.Bottom >= rectangle.Top && projected.Top <= rectangle.Bottom;
            bool projectedInside = projected.Left >= rectangle.Left && projected.Right <= rectangle.Right &&
                projected.Top >= rectangle.Top && projected.Bottom <= rectangle.Bottom;
            bool volumeIntersects = bounds.Max.X >= volumeMin.X && bounds.Min.X <= volumeMax.X &&
                bounds.Max.Y >= volumeMin.Y && bounds.Min.Y <= volumeMax.Y &&
                bounds.Max.Z >= volumeMin.Z && bounds.Min.Z <= volumeMax.Z;
            bool volumeInside = bounds.Min.X >= volumeMin.X && bounds.Max.X <= volumeMax.X &&
                bounds.Min.Y >= volumeMin.Y && bounds.Max.Y <= volumeMax.Y &&
                bounds.Min.Z >= volumeMin.Z && bounds.Max.Z <= volumeMax.Z;
            if (mode switch
                {
                    SelectionVolumeMode.CompleteTall => projectedInside,
                    SelectionVolumeMode.PartialTall => projectedIntersects,
                    SelectionVolumeMode.Touching => volumeIntersects,
                    SelectionVolumeMode.Inside => volumeInside,
                    _ => projectedIntersects
                })
                yield return item;
        }
    }
}
