using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Rendering;

internal sealed class SceneGeometry
{
    internal SceneVertex[] Vertices { get; }
    internal List<(string Material, int Start, int Count, int WireStart, int WireCount)> Batches { get; } = [];
    internal int GlyphStart { get; }
    internal int GlyphCount { get; }
    internal int GridStart { get; }
    internal int GridCount { get; }
    internal int OutlineStart { get; }
    internal int OutlineCount { get; }
    internal int AxesStart { get; }
    internal int AxesCount { get; }

    internal SceneGeometry(MapDocument document, EditorSelection selection, TransformMode transformMode, EditorTool tool)
    {
        var selectedObjects = new HashSet<object>(selection.Items, ReferenceEqualityComparer.Instance);
        foreach (MapEntity entity in selection.Items.OfType<MapEntity>())
        {
            foreach (MapBrush brush in entity.Brushes) selectedObjects.Add(brush);
            foreach (MapTerrain terrain in entity.Terrains) selectedObjects.Add(terrain);
        }
        var selectedFaces = selection.Items.OfType<BrushFaceSelection>().Select(face => face.Face).ToHashSet();
        var materials = new Dictionary<string, (List<SceneVertex> Triangles, List<SceneVertex> Lines)>(StringComparer.Ordinal);
        var outlines = new List<SceneVertex>();
        var highlight = new Vector3(1, 0.65f, 0.18f);
        var wireColor = new Vector3(0.48f, 0.51f, 0.55f);
        foreach (var brush in document.Brushes)
        foreach (var polygon in brush.GetPolygons())
        {
            var geometry = GetMaterialGeometry(polygon.Face.Material);
            AddPolygon(polygon, geometry.Triangles, Vector3.One,
                selectedObjects.Contains(brush) || selectedFaces.Contains(polygon.Face) ? highlight : null, geometry.Lines);
        }
        foreach (var terrain in document.Terrains)
        {
            var geometry = GetMaterialGeometry(terrain.Material);
            foreach (var (a, b, c) in terrain.GetTriangles())
            {
                Vector3 p0 = terrain.Vertices[a], p1 = terrain.Vertices[b], p2 = terrain.Vertices[c];
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.LengthSquared() < 0.000001f)
                    continue;
                Vector3 normal = Vector3.Normalize(cross);
                Add(a);
                Add(b);
                Add(c);
                AddEdge(p0, p1);
                AddEdge(p1, p2);
                AddEdge(p2, p0);

                void AddEdge(Vector3 a, Vector3 b)
                {
                    AddLine(geometry.Lines, a, b, wireColor);
                    if (selectedObjects.Contains(terrain))
                        AddLine(outlines, a, b, highlight);
                }

                void Add(int index)
                {
                    Vector4 color = terrain.Colors.Length > index ? terrain.Colors[index] : Vector4.One;
                    Vector2 uv = terrain.TextureCoordinates.Length > index ? terrain.TextureCoordinates[index] : Vector2.Zero;
                    geometry.Triangles.Add(new SceneVertex(terrain.Vertices[index], normal, uv, new Vector3(color.X, color.Y, color.Z)));
                }
            }
        }
        var all = new List<SceneVertex>();
        foreach (var material in materials)
        {
            int start = all.Count;
            all.AddRange(material.Value.Triangles);
            int wireStart = all.Count;
            all.AddRange(material.Value.Lines);
            Batches.Add((material.Key, start, material.Value.Triangles.Count, wireStart, material.Value.Lines.Count));
        }
        GlyphStart = all.Count;
        foreach (var entity in document.Entities.Where(PointEntityGeometry.IsPointEntity))
        {
            Vector3 color = entity.ClassName == "light" ? new(1, 0.85f, 0.35f) : new(0.35f, 0.8f, 0.95f);
            foreach (var polygon in PointEntityGeometry.CreateBrush(entity).GetPolygons())
                AddPolygon(polygon, all, color, selectedObjects.Contains(entity) ? highlight : color * 0.6f);
        }
        GlyphCount = all.Count - GlyphStart;
        GridStart = all.Count;
        for (int offset = -8192; offset <= 8192; offset += 128)
        {
            Vector3 color = offset % 1024 == 0 ? new(0.2f, 0.22f, 0.25f) : new(0.13f, 0.15f, 0.18f);
            AddLine(all, new Vector3(offset, -8192, 0), new Vector3(offset, 8192, 0),
                offset == 0 ? new Vector3(0.22f, 0.4f, 0.27f) : color);
            AddLine(all, new Vector3(-8192, offset, 0), new Vector3(8192, offset, 0),
                offset == 0 ? new Vector3(0.45f, 0.23f, 0.23f) : color);
        }
        GridCount = all.Count - GridStart;
        OutlineStart = all.Count;
        all.AddRange(outlines);
        OutlineCount = all.Count - OutlineStart;
        AxesStart = all.Count;
        foreach (MapEntity light in selection.Items.OfType<MapEntity>().Where(entity => entity.ClassName == "light"))
            foreach (var line in LightInfluenceGeometry.GetLines(document, light))
                AddLine(all, line.A, line.B, new Vector3(1, 0.85f, 0.35f));
        if (tool == EditorTool.Vertex)
            foreach (object handle in SelectionGeometry.GetVertexHandles(selection))
            {
                if (SelectionGeometry.Bounds(handle) is not { } pointBounds) continue;
                Vector3 point = pointBounds.Min;
                Vector3 color = selection.Contains(handle) ? highlight : new Vector3(0.65f, 0.9f, 1);
                const float radius = 3;
                AddLine(all, point - Vector3.UnitX * radius, point + Vector3.UnitX * radius, color);
                AddLine(all, point - Vector3.UnitY * radius, point + Vector3.UnitY * radius, color);
                AddLine(all, point - Vector3.UnitZ * radius, point + Vector3.UnitZ * radius, color);
            }
        if (tool is EditorTool.Select or EditorTool.Vertex && selection.Count > 0 &&
            selection.Items.All(SelectionGeometry.CanTransform) && SelectionGeometry.Bounds(selection.Items) is { } selectionBounds)
            foreach (var line in TransformGizmoGeometry.GetLines(selectionBounds, transformMode))
                AddLine(all, line.A, line.B, line.Color);
        AxesCount = all.Count - AxesStart;
        Vertices = all.ToArray();

        void AddPolygon(MapPolygon polygon, List<SceneVertex> vertices, Vector3 color, Vector3? outlineColor,
            List<SceneVertex>? wireframe = null)
        {
            Vector3 normal = polygon.Face.Normal;
            var projection = SurfaceProjection.Parse(polygon.Face.Projection).GetMapping(normal);
            for (int i = 1; i < polygon.Vertices.Length - 1; i++)
            {
                Add(polygon.Vertices[0]);
                Add(polygon.Vertices[i]);
                Add(polygon.Vertices[i + 1]);
            }
            for (int i = 0; i < polygon.Vertices.Length; i++)
            {
                Vector3 a = polygon.Vertices[i], b = polygon.Vertices[(i + 1) % polygon.Vertices.Length];
                if (outlineColor is { } lineColor)
                    AddLine(outlines, a, b, lineColor);
                if (wireframe is not null)
                    AddLine(wireframe, a, b, wireColor);
            }

            void Add(Vector3 position) => vertices.Add(new SceneVertex(position, normal,
                new Vector2(Vector3.Dot(position, projection.U), Vector3.Dot(position, projection.V)) + projection.Offset, color));
        }

        (List<SceneVertex> Triangles, List<SceneVertex> Lines) GetMaterialGeometry(string material)
        {
            if (!materials.TryGetValue(material, out var geometry))
                materials.Add(material, geometry = ([], []));
            return geometry;
        }

    }

    private static void AddLine(List<SceneVertex> vertices, Vector3 a, Vector3 b, Vector3 color)
    {
        vertices.Add(new SceneVertex(a, Vector3.UnitZ, Vector2.Zero, color));
        vertices.Add(new SceneVertex(b, Vector3.UnitZ, Vector2.Zero, color));
    }

}
