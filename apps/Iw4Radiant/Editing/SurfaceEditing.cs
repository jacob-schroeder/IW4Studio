using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal static class SurfaceEditing
{
    internal enum TextureTransform { FlipU, FlipV, Rotate90 }

    internal static IEnumerable<BrushFaceSelection> GetFaces(EditorSession session)
    {
        var seen = new HashSet<MapFace>(ReferenceEqualityComparer.Instance);
        foreach (object item in session.Selection.Items)
        {
            IEnumerable<BrushFaceSelection> faces = item switch
            {
                BrushFaceSelection face when face.Brush.Faces.Contains(face.Face) => [face],
                MapBrush brush => brush.Faces.Select(face => new BrushFaceSelection(brush, face)),
                MapEntity entity => entity.Brushes.SelectMany(brush =>
                    brush.Faces.Select(face => new BrushFaceSelection(brush, face))),
                _ => []
            };
            foreach (var face in faces)
                if (session.Visibility.CanSelect(session.Document, face.Brush) && seen.Add(face.Face)) yield return face;
        }
    }

    internal static void ApplyProjection(EditorSession session, SurfaceProjection edits)
    {
        var faces = GetFaces(session).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes to edit their surface projection.");
        var changes = faces.Select(selection =>
        {
            var projection = edits with { Suffix = SurfaceProjection.Parse(selection.Face.Projection).Suffix };
            projection.GetMapping(selection.Face.Normal);
            return (selection.Face, Projection: projection.Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }

    internal static void Fit(EditorSession session, float repeatsX, float repeatsY)
    {
        var faces = GetFaces(session).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes to fit their textures.");
        var changes = faces.Select(selection =>
        {
            var polygon = selection.Brush.GetPolygons().FirstOrDefault(polygon => ReferenceEquals(polygon.Face, selection.Face))
                ?? throw new ArgumentException("Cannot fit a texture to a face without a valid polygon.");
            return (selection.Face, Projection: SurfaceProjection.Parse(selection.Face.Projection).Fit(polygon, repeatsX, repeatsY).Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }

    internal static void Axial(EditorSession session)
    {
        MapFace[] faces = GetFaces(session).Select(selection => selection.Face).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes to align their textures axially.");
        var changes = faces.Select(face =>
        {
            SurfaceProjection projection = SurfaceProjection.Parse(face.Projection) with
                { ShiftX = 0, ShiftY = 0, Rotation = 0, Skew = 0 };
            _ = projection.GetMapping(face.Normal);
            return (Face: face, Projection: projection.Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }

    internal static int AutoCaulk(EditorSession session, Func<string, MaterialSource?> resolveMaterial)
    {
        var owners = session.Document.Entities.SelectMany(entity => entity.Brushes.Select(brush => (brush, entity)))
            .ToDictionary(item => item.brush, item => item.entity);
        MapBrush[] selected = session.Selection.Items.SelectMany(SelectedBrushes)
            .Where(brush => owners.ContainsKey(brush) && session.Visibility.CanSelect(session.Document, brush))
            .Distinct().Where(IsEligibleBrush).ToArray();
        if (selected.Length < 2) return 0;

        var polygons = selected.ToDictionary(brush => brush,
            brush => brush.GetPolygons().ToArray());
        var changes = new HashSet<MapFace>(ReferenceEqualityComparer.Instance);
        foreach (MapBrush brush in selected)
        foreach (MapPolygon candidate in polygons[brush])
        {
            if (CaulkMaterial.IsCaulk(candidate.Face.Material)) continue;
            Vector3 normal = candidate.Face.Normal;
            double distance = BrushGeometry.Dot(normal, candidate.Vertices[0]);
            MapPolygon[] covers = selected.Where(other => !ReferenceEquals(other, brush) &&
                    ReferenceEquals(owners[other], owners[brush]))
                .SelectMany(other => polygons[other])
                .Where(other => !CaulkMaterial.IsCaulk(other.Face.Material) &&
                    Vector3.Dot(normal, other.Face.Normal) <= -0.99999f &&
                    other.Vertices.All(point => Math.Abs(BrushGeometry.Dot(normal, point) - distance) <=
                        BrushGeometry.PlaneTolerance))
                .ToArray();
            if (covers.Length == 0) continue;
            var (u, v) = PlaneAxes(normal);
            List<Vector2> target = CounterClockwise(candidate.Vertices.Select(Project).ToList());
            List<List<Vector2>> coverPolygons = covers.Select(cover =>
                CounterClockwise(cover.Vertices.Select(Project).ToList())).ToList();
            if (IsFullyCovered(target, coverPolygons)) changes.Add(candidate.Face);

            Vector2 Project(Vector3 point) => new((float)BrushGeometry.Dot(point, u), (float)BrushGeometry.Dot(point, v));
        }
        if (changes.Count == 0) return 0;
        session.Edit(() =>
        {
            foreach (MapFace face in changes) face.Material = CaulkMaterial.Name;
        });
        return changes.Count;

        bool IsEligibleBrush(MapBrush brush) => brush.Faces.All(face =>
        {
            if (CaulkMaterial.IsCaulk(face.Material)) return true;
            if (ClipBrushMaterial.IsPlayerClip(face.Material)) return false;
            MaterialSource? material = resolveMaterial(face.Material);
            return material is not null && !material.IsSky && !material.IsWater &&
                !material.Surface.IsBlended && material.Surface.AlphaTest is null && material.Surface.DepthWrite;
        });

        static IEnumerable<MapBrush> SelectedBrushes(object item) => item switch
        {
            MapBrush brush => [brush],
            MapEntity entity => entity.Brushes,
            _ => []
        };
    }

    internal static void TransformTexture(EditorSession session, TextureTransform transform)
    {
        MapFace[] faces = GetFaces(session).Select(selection => selection.Face).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes before transforming their textures.");
        var changes = faces.Select(face =>
        {
            SurfaceProjection projection = SurfaceProjection.Parse(face.Projection);
            projection = transform switch
            {
                TextureTransform.FlipU => projection with { Width = projection.Width == 0 ? -128 : -projection.Width },
                TextureTransform.FlipV => projection with { Height = projection.Height == 0 ? -128 : -projection.Height },
                TextureTransform.Rotate90 => projection with { Rotation = NormalizeRotation(projection.Rotation + 90) },
                _ => throw new ArgumentOutOfRangeException(nameof(transform))
            };
            _ = projection.GetMapping(face.Normal);
            return (Face: face, Projection: projection.Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }

    private static float NormalizeRotation(float rotation)
    {
        rotation %= 360;
        return rotation < 0 ? rotation + 360 : rotation;
    }

    private static (Vector3 U, Vector3 V) PlaneAxes(Vector3 normal)
    {
        Vector3 reference = MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(reference, normal));
        return (u, Vector3.Cross(normal, u));
    }

    private static List<Vector2> CounterClockwise(List<Vector2> polygon)
    {
        if (SignedArea(polygon) < 0) polygon.Reverse();
        return polygon;
    }

    private static bool IsFullyCovered(List<Vector2> target, IReadOnlyList<List<Vector2>> covers)
    {
        double targetArea = Math.Abs(SignedArea(target));
        if (target.Count < 3 || targetArea <= 0) return false;
        double tolerance = Math.Max(0.000001, targetArea * 0.0000001);
        List<List<Vector2>> remaining = [target];
        foreach (List<Vector2> cover in covers)
        {
            if (cover.Count < 3 || Math.Abs(SignedArea(cover)) <= tolerance) continue;
            remaining = remaining.SelectMany(piece => Subtract(piece, cover, tolerance))
                .Where(piece => Math.Abs(SignedArea(piece)) > tolerance).ToList();
            if (remaining.Count == 0) return true;
        }
        return remaining.Sum(piece => Math.Abs(SignedArea(piece))) <= tolerance;
    }

    private static IEnumerable<List<Vector2>> Subtract(List<Vector2> subject, List<Vector2> clip, double tolerance)
    {
        List<Vector2> inside = subject;
        for (int index = 0; index < clip.Count && inside.Count >= 3; index++)
        {
            Vector2 a = clip[index], b = clip[(index + 1) % clip.Count];
            List<Vector2> outside = ClipHalfPlane(inside, a, b, keepInside: false);
            if (outside.Count >= 3 && Math.Abs(SignedArea(outside)) > tolerance) yield return outside;
            inside = ClipHalfPlane(inside, a, b, keepInside: true);
        }
    }

    private static List<Vector2> ClipHalfPlane(IReadOnlyList<Vector2> polygon, Vector2 a, Vector2 b, bool keepInside)
    {
        const double boundary = -0.00001;
        var result = new List<Vector2>();
        if (polygon.Count == 0) return result;
        Vector2 edge = b - a;
        double edgeLength = edge.Length();
        if (edgeLength <= 0) return result;
        double Distance(Vector2 point) => ((double)edge.X * (point.Y - a.Y) - (double)edge.Y * (point.X - a.X)) / edgeLength;
        bool Keep(double distance) => keepInside ? distance >= boundary : distance <= boundary;
        Vector2 previous = polygon[^1];
        double previousDistance = Distance(previous);
        bool previousKept = Keep(previousDistance);
        foreach (Vector2 current in polygon)
        {
            double currentDistance = Distance(current);
            bool currentKept = Keep(currentDistance);
            if (currentKept != previousKept)
            {
                double denominator = currentDistance - previousDistance;
                double amount = denominator == 0 ? 0 : (boundary - previousDistance) / denominator;
                result.Add(previous + (current - previous) * (float)Math.Clamp(amount, 0, 1));
            }
            if (currentKept) result.Add(current);
            previous = current;
            previousDistance = currentDistance;
            previousKept = currentKept;
        }
        return result;
    }

    private static double SignedArea(IReadOnlyList<Vector2> polygon)
    {
        double area = 0;
        for (int index = 0; index < polygon.Count; index++)
        {
            Vector2 a = polygon[index], b = polygon[(index + 1) % polygon.Count];
            area += (double)a.X * b.Y - (double)a.Y * b.X;
        }
        return area / 2;
    }
}
