using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class BrushClipping
{
    // System.Numerics plane convention: Dot(Normal, point) + D <= 0 is Back.
    // An orthographic clip line supplies the same plane by crossing its direction
    // with the view normal; the geometry operation is independent of the viewport.
    internal static (MapBrush? Back, MapBrush? Front) Split(MapBrush brush, Plane plane)
    {
        double length = Math.Sqrt(BrushGeometry.Dot(plane.Normal, plane.Normal));
        if (!BrushGeometry.IsFinite(plane.Normal) || !float.IsFinite(plane.D) || length == 0)
            throw new ArgumentException("Clipping requires a finite plane with a nonzero normal.");
        Vector3 normal = new((float)(plane.Normal.X / length), (float)(plane.Normal.Y / length),
            (float)(plane.Normal.Z / length));
        double distance = -plane.D / length;
        BrushGeometry.Validate(brush);
        var vertices = brush.GetVertices();
        bool back = vertices.Any(point => BrushGeometry.Dot(normal, point) < distance - BrushGeometry.PlaneTolerance);
        bool front = vertices.Any(point => BrushGeometry.Dot(normal, point) > distance + BrushGeometry.PlaneTolerance);
        if (!front) return (brush.Clone(), null);
        if (!back) return (null, brush.Clone());

        List<Vector3> intersections = [];
        foreach (var polygon in brush.GetPolygons())
        for (int index = 0; index < polygon.Vertices.Length; index++)
        {
            Vector3 a = polygon.Vertices[index], b = polygon.Vertices[(index + 1) % polygon.Vertices.Length];
            double da = BrushGeometry.Dot(normal, a) - distance, db = BrushGeometry.Dot(normal, b) - distance;
            if (Math.Abs(da) <= BrushGeometry.PlaneTolerance)
                AddIntersection(a - normal * (float)da);
            if (da < 0 && db > 0 || da > 0 && db < 0)
            {
                double fraction = da / (da - db);
                AddIntersection(new((float)(a.X + ((double)b.X - a.X) * fraction),
                    (float)(a.Y + ((double)b.Y - a.Y) * fraction),
                    (float)(a.Z + ((double)b.Z - a.Z) * fraction)));
            }
        }
        if (intersections.Count < 3)
            throw new ArgumentException("The clipping plane does not produce a valid cut face.");
        int second = 1, third = 2;
        double largestArea = 0;
        for (int b = 1; b < intersections.Count; b++)
        for (int c = b + 1; c < intersections.Count; c++)
        {
            Vector3 cross = Vector3.Cross(intersections[b] - intersections[0], intersections[c] - intersections[0]);
            double area = BrushGeometry.Dot(cross, cross);
            if (area <= largestArea) continue;
            largestArea = area;
            second = b;
            third = c;
        }
        if (largestArea <= 1e-8)
            throw new ArgumentException("The clipping plane would create a degenerate cut face.");
        var cap = brush.Faces.MaxBy(face => Math.Abs(BrushGeometry.Dot(face.Normal, normal)))?.Clone()
            ?? throw new ArgumentException("Cannot clip an empty brush.");
        cap.A = intersections[0];
        cap.B = intersections[second];
        cap.C = intersections[third];
        if (BrushGeometry.Dot(cap.Normal, normal) < 0) (cap.B, cap.C) = (cap.C, cap.B);
        MapBrush backPiece = CreatePiece(brush, cap);
        var oppositeCap = cap.Clone();
        (oppositeCap.B, oppositeCap.C) = (oppositeCap.C, oppositeCap.B);
        return (backPiece, CreatePiece(brush, oppositeCap));

        void AddIntersection(Vector3 point)
        {
            if (BrushGeometry.FindVertex(intersections, point) < 0) intersections.Add(point);
        }
    }

    private static MapBrush CreatePiece(MapBrush source, MapFace cap)
    {
        var clipped = source.Clone();
        clipped.Faces.Add(cap);
        var piece = new MapBrush();
        piece.Directives.AddRange(source.Directives);
        piece.Faces.AddRange(clipped.GetPolygons().Select(polygon => polygon.Face));
        BrushGeometry.Validate(piece);
        return piece;
    }
}
