using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class CsgEditing
{
    internal static bool CanMerge(EditorSession session) => SelectedBrushes(session).Length == 2;
    internal static bool CanHollow(EditorSession session) => SelectedBrushes(session).Length == 1;

    internal static void Merge(EditorSession session)
    {
        MapBrush[] brushes = SelectedBrushes(session);
        if (brushes.Length != 2)
            throw new ArgumentException("Select exactly two whole convex brushes to merge.");
        MapEntity owner = Owner(session, brushes[0]);
        if (!ReferenceEquals(owner, Owner(session, brushes[1])))
            throw new ArgumentException("CSG merge requires both brushes to have the same entity owner.");
        if (!brushes[0].Directives.SequenceEqual(brushes[1].Directives, StringComparer.Ordinal))
            throw new ArgumentException("CSG merge requires matching brush contents and directives.");

        Vector3[] vertices = brushes.SelectMany(brush => brush.GetVertices()).ToArray();
        var planes = new List<MapFace>();
        foreach (MapFace source in brushes.SelectMany(brush => brush.Faces))
        {
            Vector3 normal = source.Normal;
            double distance = BrushGeometry.Dot(normal, source.A);
            if (vertices.Any(point => BrushGeometry.Dot(normal, point) > distance + BrushGeometry.PlaneTolerance))
                continue;
            MapFace? existing = planes.FirstOrDefault(face =>
                Vector3.DistanceSquared(face.Normal, normal) < 0.000001f &&
                Math.Abs(BrushGeometry.Dot(face.Normal, face.A) - distance) <= BrushGeometry.PlaneTolerance);
            if (existing is not null)
            {
                if (existing.Material != source.Material || existing.Projection != source.Projection)
                    throw new ArgumentException("Coplanar exterior faces must have matching materials and texture projections before merging.");
                continue;
            }
            planes.Add(source.Clone());
        }
        var merged = new MapBrush();
        merged.Directives.AddRange(brushes[0].Directives);
        merged.Faces.AddRange(planes);
        BrushGeometry.Validate(merged);
        double sourceVolume = brushes.Sum(Volume), mergedVolume = Volume(merged);
        double tolerance = Math.Max(0.01, sourceVolume * 0.0001);
        if (Math.Abs(sourceVolume - mergedVolume) > tolerance)
            throw new ArgumentException("The selected brushes do not form one convex, non-overlapping volume. Merge only adjacent pieces whose union is convex.");

        session.Edit(() =>
        {
            int index = Math.Min(owner.Brushes.IndexOf(brushes[0]), owner.Brushes.IndexOf(brushes[1]));
            owner.Brushes.Remove(brushes[0]);
            owner.Brushes.Remove(brushes[1]);
            owner.Brushes.Insert(index, merged);
            session.Selection.Set(merged);
        });
    }

    internal static void Hollow(EditorSession session)
    {
        MapBrush[] brushes = SelectedBrushes(session);
        if (brushes.Length != 1)
            throw new ArgumentException("Select exactly one whole axis-aligned box brush to hollow.");
        MapBrush source = brushes[0];
        BrushGeometry.Validate(source);
        var byNormal = new Dictionary<int, MapFace>();
        foreach (MapFace face in source.Faces)
        {
            int direction = CardinalDirection(face.Normal);
            if (direction < 0 || byNormal.ContainsKey(direction))
                throw new ArgumentException("CSG hollow currently requires one six-sided axis-aligned box brush.");
            byNormal.Add(direction, face);
        }
        if (byNormal.Count != 6 || source.GetVertices().Count != 8)
            throw new ArgumentException("CSG hollow currently requires one six-sided axis-aligned box brush.");
        var bounds = source.GetBounds();
        Vector3 size = bounds.Max - bounds.Min;
        float thickness = session.GridSize;
        if (!float.IsFinite(thickness) || thickness <= 0 || size.X <= thickness * 2 || size.Y <= thickness * 2 || size.Z <= thickness * 2)
            throw new ArgumentException("The selected box must be more than two grid units wide on every axis.");

        Vector3 min = bounds.Min, max = bounds.Max;
        MapBrush[] pieces =
        [
            Box(new(min.X, min.Y, min.Z), new(max.X, max.Y, min.Z + thickness)),
            Box(new(min.X, min.Y, max.Z - thickness), new(max.X, max.Y, max.Z)),
            Box(new(min.X, min.Y, min.Z + thickness), new(min.X + thickness, max.Y, max.Z - thickness)),
            Box(new(max.X - thickness, min.Y, min.Z + thickness), new(max.X, max.Y, max.Z - thickness)),
            Box(new(min.X + thickness, min.Y, min.Z + thickness), new(max.X - thickness, min.Y + thickness, max.Z - thickness)),
            Box(new(min.X + thickness, max.Y - thickness, min.Z + thickness), new(max.X - thickness, max.Y, max.Z - thickness))
        ];
        MapEntity owner = Owner(session, source);
        session.Edit(() =>
        {
            int index = owner.Brushes.IndexOf(source);
            owner.Brushes.RemoveAt(index);
            owner.Brushes.InsertRange(index, pieces);
            session.Selection.SetRange(pieces);
        });

        MapBrush Box(Vector3 a, Vector3 b)
        {
            MapBrush result = MapBrush.CreateBox(a, b, byNormal[0].Material);
            result.Directives.AddRange(source.Directives);
            foreach (MapFace face in result.Faces)
            {
                MapFace template = byNormal[CardinalDirection(face.Normal)];
                face.Material = template.Material;
                face.Projection = template.Projection;
            }
            BrushGeometry.Validate(result);
            return result;
        }
    }

    private static MapBrush[] SelectedBrushes(EditorSession session) => session.Selection.Items
        .Select(EditorSelection.Owner).OfType<MapBrush>().Distinct(ReferenceEqualityComparer.Instance)
        .Where(brush => session.Visibility.CanSelect(session.Document, brush)).ToArray();

    private static MapEntity Owner(EditorSession session, MapBrush brush) =>
        session.Document.Entities.First(entity => entity.Brushes.Contains(brush));

    private static int CardinalDirection(Vector3 normal)
    {
        if (Vector3.DistanceSquared(normal, -Vector3.UnitX) < 0.000001f) return 0;
        if (Vector3.DistanceSquared(normal, Vector3.UnitX) < 0.000001f) return 1;
        if (Vector3.DistanceSquared(normal, -Vector3.UnitY) < 0.000001f) return 2;
        if (Vector3.DistanceSquared(normal, Vector3.UnitY) < 0.000001f) return 3;
        if (Vector3.DistanceSquared(normal, -Vector3.UnitZ) < 0.000001f) return 4;
        if (Vector3.DistanceSquared(normal, Vector3.UnitZ) < 0.000001f) return 5;
        return -1;
    }

    private static double Volume(MapBrush brush)
    {
        IReadOnlyList<Vector3> vertices = brush.GetVertices();
        Vector3 center = vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point / vertices.Count);
        double volume = 0;
        foreach (MapPolygon polygon in brush.GetPolygons())
        for (int index = 1; index < polygon.Vertices.Length - 1; index++)
        {
            Vector3 a = polygon.Vertices[0] - center, b = polygon.Vertices[index] - center,
                c = polygon.Vertices[index + 1] - center;
            volume += a.X * ((double)b.Y * c.Z - (double)b.Z * c.Y) +
                      a.Y * ((double)b.Z * c.X - (double)b.X * c.Z) +
                      a.Z * ((double)b.X * c.Y - (double)b.Y * c.X);
        }
        return volume / 6;
    }
}
