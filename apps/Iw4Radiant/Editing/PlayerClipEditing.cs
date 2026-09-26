using System.Numerics;
using JoltPhysicsSharp;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal static class PlayerClipEditing
{
    private const int MaximumModels = 128;
    private const int MaximumInputVertices = 20_000;
    private const int MaximumInputTriangles = 40_000;
    private const int MaximumHullFaces = 64;
    private const int MaximumSimplifiedFaces = 40;

    internal static bool CanApply(EditorSession session) => session.Selection.Count > 0 &&
        session.Selection.Items.All(item => item is MapBrush brush && session.Document.World.Brushes.Contains(brush) &&
            session.Visibility.CanSelect(session.Document, brush));

    internal static void Apply(EditorSession session)
    {
        if (!CanApply(session))
            throw new ArgumentException("Select whole world brushes to make player clip. Draw and shape a separate brush around the area that should block players.");
        SelectionEditing.ApplyMaterial(session, ClipBrushMaterial.PlayerClip);
    }

    internal static bool CanGenerateFromModels(EditorSession session) =>
        session.Selection.Count is > 0 and <= MaximumModels && session.Scene.ResolveModel is not null &&
        session.Selection.Items.All(item =>
            item is MapEntity entity && entity.ClassName == "misc_model" &&
            entity.Brushes.Count == 0 && entity.Terrains.Count == 0 && entity.PreservedPrimitives.Count == 0 &&
            session.Document.Entities.Contains(entity) && session.Visibility.CanSelect(session.Document, entity) &&
            entity.Properties.TryGetValue("model", out string? name) && !string.IsNullOrWhiteSpace(name));

    internal static async Task<(int Count, int SimplifiedCount)> GenerateFromModelsAsync(EditorSession session)
    {
        if (session.Selection.Count > MaximumModels)
            throw new NotSupportedException($"Select at most {MaximumModels} models for one clip operation.");
        if (!CanGenerateFromModels(session))
            throw new ArgumentException("Select whole, visible misc_model entities with available mesh geometry.");

        MapDocument document = session.Document;
        long revision = session.ContentRevision;
        MapEntity[] models = session.Selection.Items.Cast<MapEntity>().ToArray();
        Func<string, XModelSource?> resolve = session.Scene.ResolveModel
            ?? throw new ArgumentException("Load a raw model asset folder first.");
        var inputs = new (Vector3[] Vertices, int[] Indices, Matrix4x4 Transform, string Layer, string Name)[models.Length];
        for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
        {
            MapEntity model = models[modelIndex];
            XModelSource source = resolve(model.Properties["model"])
                ?? throw new ArgumentException($"Model '{model.Properties["model"]}' is unavailable. Load its raw asset folder first.");
            var mesh = source.Document;
            if (mesh.Vertices.Count > MaximumInputVertices || mesh.Triangles.Count > MaximumInputTriangles)
                throw TooComplex(source.Name);
            Matrix4x4 transform = XModelGeometry.Transform(model);
            if (!Matrix4x4.Invert(transform, out _) || !float.IsFinite(transform.GetDeterminant()))
                throw new ArgumentException($"Model '{source.Name}' has an invalid transform.");
            Vector3[] vertices = mesh.Vertices.Select(vertex => vertex.Position).ToArray();
            int[] indices = new int[mesh.Triangles.Count * 3];
            for (int triangle = 0; triangle < mesh.Triangles.Count; triangle++)
            {
                int first = triangle * 3;
                indices[first] = mesh.Triangles[triangle].First.VertexIndex;
                indices[first + 1] = mesh.Triangles[triangle].Second.VertexIndex;
                indices[first + 2] = mesh.Triangles[triangle].Third.VertexIndex;
            }
            inputs[modelIndex] = (vertices, indices, transform, MapOrganization.Layer(model), source.Name);
        }

        var results = await Task.Run(() => inputs.Select(input =>
            CreateHullBrush(input.Vertices, input.Indices, input.Transform, input.Name)).ToArray());
        MapBrush[] clips = results.Select(result => result.Brush).ToArray();
        for (int index = 0; index < clips.Length; index++)
            MapOrganization.Assign(clips[index], inputs[index].Layer);

        if (!ReferenceEquals(document, session.Document) || revision != session.ContentRevision ||
            !models.SequenceEqual(session.Selection.Items) || !CanGenerateFromModels(session))
            throw new InvalidOperationException("The selected models changed while the clips were being generated. Try again.");
        session.Edit(() =>
        {
            document.World.Brushes.AddRange(clips);
            session.Tool = EditorTool.Select;
            session.Selection.SetRange(clips);
        });
        return (clips.Length, results.Count(result => result.Simplified));
    }

    private static (MapBrush Brush, bool Simplified) CreateHullBrush(Vector3[] vertices, int[] indices,
        Matrix4x4 transform, string name)
    {
        var used = new HashSet<int>(indices);
        if (used.Count < 4)
            throw new ArgumentException($"Model '{name}' has too few mesh vertices for a solid clip.");
        Vector3[] points = new Vector3[used.Count];
        int next = 0;
        foreach (int index in used)
        {
            if ((uint)index >= (uint)vertices.Length)
                throw new InvalidDataException($"Model '{name}' has an invalid mesh vertex index.");
            Vector3 point = Vector3.Transform(vertices[index], transform);
            if (!BrushGeometry.IsFinite(point))
                throw new ArgumentException($"Model '{name}' has nonfinite mesh coordinates.");
            points[next++] = point;
        }
        // Keep native hull arithmetic close to the origin, then restore map coordinates.
        Vector3 origin = points[0];
        Vector3[] relative = points.Select(point => point - origin).ToArray();
        if (relative.Any(point => !BrushGeometry.IsFinite(point)))
            throw new ArgumentException("The model mesh spans coordinates too large for a clip brush.");
        using var settings = new ConvexHullShapeSettings(relative, 0);
        if (settings.Handle == 0)
            throw new ArgumentException("The model mesh cannot form a solid convex clip.");
        using var hull = new ConvexHullShape(settings);
        if (hull.Handle == 0)
            throw new ArgumentException("The model mesh cannot form a solid convex clip.");
        uint faceCount = hull.GetNumFaces(), pointCount = hull.GetNumPoints();
        if (faceCount < 4 || faceCount > MaximumInputVertices * 2 ||
            pointCount < 4 || pointCount > MaximumInputVertices)
            throw new ArgumentException("The model mesh cannot form a solid convex clip.");

        Vector3 offset = origin + hull.CenterOfMass;
        Vector3[] hullVertices = new Vector3[(int)pointCount];
        for (uint index = 0; index < pointCount; index++)
        {
            hullVertices[(int)index] = hull.GetPoint(index) + offset;
            if (!BrushGeometry.IsFinite(hullVertices[(int)index]))
                throw new ArgumentException("The model hull has nonfinite coordinates.");
        }
        Vector3 center = hullVertices[0] + hullVertices.Aggregate(Vector3.Zero,
            (sum, point) => sum + (point - hullVertices[0])) / hullVertices.Length;
        if (!BrushGeometry.IsFinite(center))
            throw new ArgumentException("The model hull spans coordinates too large for a clip brush.");
        bool exact = faceCount <= MaximumHullFaces;
        int directedEdges = 0;
        if (exact)
        {
            for (uint face = 0; face < faceCount; face++)
            {
                uint count = hull.GetNumVerticesInFace(face);
                if (count < 3) throw new ArgumentException("The model hull has a degenerate face.");
                if (count > byte.MaxValue || directedEdges + count > byte.MaxValue) { exact = false; break; }
                directedEdges += (int)count;
            }
        }
        MapBrush brush = exact ? ExactBrush(hull, hullVertices, center, faceCount) :
            SimplifiedBrush(hull, hullVertices, center, faceCount, points, name);
        if (exact && brush.GetPolygons().Sum(polygon => polygon.Vertices.Length) > byte.MaxValue)
        {
            exact = false;
            brush = SimplifiedBrush(hull, hullVertices, center, faceCount, points, name);
        }
        ValidateClipBrush(brush, points, name);
        return (brush, !exact);
    }

    private static MapBrush ExactBrush(ConvexHullShape hull, Vector3[] hullVertices, Vector3 center, uint faceCount)
    {
        var brush = new MapBrush();
        for (uint face = 0; face < faceCount; face++)
        {
            var plane = NativeFacePlane(hull, hullVertices, center, face);
            brush.Faces.Add(new MapFace { A = plane.A, B = plane.B, C = plane.C,
                Material = ClipBrushMaterial.PlayerClip });
        }
        return brush;
    }

    private static (Vector3 A, Vector3 B, Vector3 C, Vector3 Normal) NativeFacePlane(
        ConvexHullShape hull, Vector3[] hullVertices, Vector3 center, uint face)
    {
        uint count = hull.GetNumVerticesInFace(face);
        if (count < 3 || count > hullVertices.Length)
            throw new ArgumentException("The model hull has invalid face vertices.");
        uint[] indices = new uint[(int)count];
        if (hull.GetFaceVertices(face, count, indices) != count || indices.Any(index => index >= hullVertices.Length))
            throw new ArgumentException("The model hull has invalid face vertices.");
        Vector3 a = hullVertices[(int)indices[0]], b = default, c = default;
        bool plane = false;
        for (int second = 1; second < indices.Length - 1 && !plane; second++)
        for (int third = second + 1; third < indices.Length; third++)
        {
            b = hullVertices[(int)indices[second]];
            c = hullVertices[(int)indices[third]];
            if (Vector3.Cross(b - a, c - a).LengthSquared() > 1e-12f) { plane = true; break; }
        }
        if (!plane) throw new ArgumentException("The model hull has a degenerate face.");
        Vector3 normal = BrushGeometry.FaceNormal(a, b, c);
        if (!BrushGeometry.IsFinite(normal))
            throw new ArgumentException("The model hull has a degenerate face.");
        if (BrushGeometry.Dot(normal, a - center) < 0)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        return (a, b, c, normal);
    }

    private static MapBrush SimplifiedBrush(ConvexHullShape hull, Vector3[] hullVertices, Vector3 center,
        uint faceCount, Vector3[] points, string name)
    {
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitZ, -Vector3.UnitZ];
        var candidates = new List<Vector3>((int)faceCount);
        for (uint face = 0; face < faceCount; face++)
            candidates.Add(NativeFacePlane(hull, hullVertices, center, face).Normal);

        // Farthest-direction sampling covers the hull without favoring densely tessellated areas.
        var directions = new List<Vector3>(axes);
        float[] proximity = candidates.Select(candidate => axes.Max(axis => Vector3.Dot(axis, candidate))).ToArray();
        while (directions.Count < MaximumSimplifiedFaces)
        {
            int best = -1;
            float mostDistant = 0.995f;
            for (int index = 0; index < candidates.Count; index++)
                if (proximity[index] < mostDistant) { mostDistant = proximity[index]; best = index; }
            if (best < 0) break;
            Vector3 direction = candidates[best];
            directions.Add(direction);
            for (int index = 0; index < candidates.Count; index++)
                proximity[index] = MathF.Max(proximity[index], Vector3.Dot(candidates[index], direction));
        }
        if (directions.Count == axes.Length) throw TooComplex(name);

        Vector3 minimum = points.Aggregate(Vector3.Min), maximum = points.Aggregate(Vector3.Max);
        Vector3 size = maximum - minimum;
        float span = MathF.Max(1, MathF.Max(size.X, MathF.Max(size.Y, size.Z)));
        if (!float.IsFinite(span)) throw TooComplex(name);
        var proposed = new MapBrush();
        foreach (Vector3 direction in directions)
        {
            Vector3 support = points[0];
            double maximumProjection = BrushGeometry.Dot(direction, support);
            foreach (Vector3 point in points)
            {
                double projection = BrushGeometry.Dot(direction, point);
                if (projection > maximumProjection) { maximumProjection = projection; support = point; }
            }
            Vector3 a = support + direction * BrushGeometry.PlaneTolerance;
            Vector3 basis = MathF.Abs(direction.X) <= MathF.Abs(direction.Y) &&
                MathF.Abs(direction.X) <= MathF.Abs(direction.Z) ? Vector3.UnitX :
                MathF.Abs(direction.Y) <= MathF.Abs(direction.Z) ? Vector3.UnitY : Vector3.UnitZ;
            Vector3 u = Vector3.Normalize(Vector3.Cross(basis, direction));
            Vector3 v = Vector3.Cross(direction, u);
            var face = new MapFace { A = a, B = a + v * span, C = a + u * span,
                Material = ClipBrushMaterial.PlayerClip };
            if (!BrushGeometry.IsFinite(face.A) || !BrushGeometry.IsFinite(face.B) ||
                !BrushGeometry.IsFinite(face.C) || !BrushGeometry.IsFinite(face.Normal))
                throw TooComplex(name);
            proposed.Faces.Add(face);
        }

        return CleanSupportPlanes(proposed.Faces, axes.Length, name);
    }

    private static MapBrush CleanSupportPlanes(IReadOnlyList<MapFace> proposed, int axisCount, string name)
    {
        // Near-coincident support planes can make edges that collapse under the brush's vertex tolerance.
        // Drop one implicated non-axis plane per pass; this only enlarges the hull and is bounded by the initial face count.
        var faces = new List<MapFace>(proposed);
        var axes = new HashSet<MapFace>(proposed.Take(axisCount), ReferenceEqualityComparer.Instance);
        float shortEdgeSquared = 4 * BrushGeometry.PointTolerance * BrushGeometry.PointTolerance;
        while (true)
        {
            var brush = new MapBrush();
            brush.Faces.AddRange(faces);
            IReadOnlyList<MapPolygon> polygons = brush.GetPolygons();
            var active = new HashSet<MapFace>(polygons.Select(polygon => polygon.Face), ReferenceEqualityComparer.Instance);
            MapFace? remove = faces.FirstOrDefault(face => !axes.Contains(face) && !active.Contains(face));
            if (remove is null && axes.Any(axis => !active.Contains(axis)))
            {
                MapFace missingAxis = axes.First(axis => !active.Contains(axis));
                remove = faces.Skip(axisCount).MaxBy(face => BrushGeometry.Dot(face.Normal, missingAxis.Normal));
            }

            if (remove is null)
            {
                IReadOnlyList<Vector3> vertices = BrushGeometry.Vertices(polygons);
                float shortest = shortEdgeSquared;
                var owners = new Dictionary<(int, int), List<(MapFace Face, int Direction, Vector3 Midpoint)>>();
                foreach (MapPolygon polygon in polygons)
                for (int edge = 0; edge < polygon.Vertices.Length; edge++)
                {
                    Vector3 first = polygon.Vertices[edge];
                    Vector3 second = polygon.Vertices[(edge + 1) % polygon.Vertices.Length];
                    int a = BrushGeometry.FindVertex(vertices, first);
                    int b = BrushGeometry.FindVertex(vertices, second);
                    Vector3 midpoint = (first + second) * 0.5f;
                    float lengthSquared = Vector3.DistanceSquared(first, second);
                    if (a == b || lengthSquared < shortEdgeSquared)
                    {
                        float score = a == b ? -1 : lengthSquared;
                        MapFace? candidate = RemovableFace(polygon.Face, midpoint, faces, axes);
                        if (candidate is not null && score < shortest) { shortest = score; remove = candidate; }
                    }
                    if (a == b || a < 0 || b < 0) continue;
                    var key = a < b ? (a, b) : (b, a);
                    if (!owners.TryGetValue(key, out var neighbors)) owners.Add(key, neighbors = []);
                    neighbors.Add((polygon.Face, a < b ? 1 : -1, midpoint));
                }
                if (remove is null)
                    foreach (var neighbors in owners.Values)
                    {
                        if (neighbors.Count == 2 && neighbors[0].Direction != neighbors[1].Direction) continue;
                        remove = neighbors.Select(owner => RemovableFace(owner.Face, owner.Midpoint, faces, axes))
                            .FirstOrDefault(face => face is not null);
                        if (remove is not null) break;
                    }
            }

            if (remove is null) return brush;
            if (faces.Count <= axisCount + 1) throw TooComplex(name);
            faces.Remove(remove);
        }
    }

    private static MapFace? RemovableFace(MapFace owner, Vector3 midpoint, IReadOnlyList<MapFace> faces,
        HashSet<MapFace> axes)
    {
        if (!axes.Contains(owner)) return owner;
        MapFace? nearest = faces.Where(face => !axes.Contains(face)).MinBy(face =>
            Math.Abs(BrushGeometry.Dot(face.Normal, midpoint) - BrushGeometry.Dot(face.Normal, face.A)));
        if (nearest is null) return null;
        Vector3 normal = nearest.Normal;
        double separation = Math.Abs(BrushGeometry.Dot(normal, midpoint) - BrushGeometry.Dot(normal, nearest.A));
        return separation <= 2 * BrushGeometry.PlaneTolerance ? nearest : null;
    }

    private static void ValidateClipBrush(MapBrush brush, Vector3[] points, string name)
    {
        foreach (MapFace face in brush.Faces)
        {
            Vector3 normal = face.Normal;
            double distance = BrushGeometry.Dot(normal, face.A);
            if (points.Any(point => BrushGeometry.Dot(normal, point) > distance + BrushGeometry.PlaneTolerance))
                throw new ArgumentException($"The convex hull does not fully enclose model '{name}'. Draw the clip manually.");
        }
        BrushGeometry.Validate(brush);
        if (brush.GetPolygons().Sum(polygon => polygon.Vertices.Length) > byte.MaxValue)
            throw TooComplex(name);
    }

    private static NotSupportedException TooComplex(string name) => new(
        $"Model '{name}' is too complex for one editable player clip. Draw the clip manually.");
}
