using IW4.AssetExchange.SourceFormat.Material;
using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

internal sealed class SceneGeometry
{
    internal SceneVertex[] Vertices { get; }
    internal Dictionary<int, Vector3> SurfaceCenters { get; } = [];
    internal List<(string Material, int Start, int Count, int WireStart, int WireCount)> Batches { get; } = [];
    internal int GlyphStart { get; }
    internal int GlyphCount { get; }
    internal int GridStart { get; }
    internal int GridCount { get; }
    internal int HighlightStart { get; }
    internal int HighlightCount { get; }
    internal int OutlineStart { get; }
    internal int OutlineCount { get; }
    internal int AxesStart { get; }
    internal int AxesCount { get; }

    internal SceneGeometry(EditorScene editor, TransformMode transformMode, EditorTool tool,
        Func<string, MaterialSource?>? resolveMaterial)
    {
        MapDocument document = editor.Document;
        var shore = new WaterShoreGeometry(document, name => resolveMaterial?.Invoke(name));
        var staticBrushes = document.Entities.Where(entity => entity.ClassName is "worldspawn" or "func_group")
            .SelectMany(entity => entity.Brushes).ToHashSet();
        EditorSelection selection = editor.Selection;
        var selectedObjects = new HashSet<object>(selection.Items, ReferenceEqualityComparer.Instance);
        foreach (MapEntity entity in selection.Items.OfType<MapEntity>())
        {
            foreach (MapBrush brush in entity.Brushes) selectedObjects.Add(brush);
            foreach (MapTerrain terrain in entity.Terrains) selectedObjects.Add(terrain);
        }
        var selectedFaces = selection.Items.OfType<BrushFaceSelection>().Select(face => face.Face).ToHashSet();
        var materials = new Dictionary<string, (List<SceneVertex> Triangles, List<SceneVertex> Lines, List<(int Start, int Count, Vector3 Center)> Surfaces)>(StringComparer.Ordinal);
        var highlights = new List<SceneVertex>();
        var outlines = new List<SceneVertex>();
        var highlight = new Vector3(1, 0.65f, 0.18f);
        var clipColor = new Vector3(0.85f, 0.35f, 0.85f);
        var caulkColor = new Vector3(0.45f, 0.68f, 0.72f);
        var wireColor = new Vector3(0.48f, 0.51f, 0.55f);
        foreach (var brush in document.Brushes)
        foreach (var polygon in brush.GetPolygons())
        {
            bool faceSelected = selectedFaces.Contains(polygon.Face);
            bool selected = selectedObjects.Contains(brush) || faceSelected;
            MaterialSource? source = resolveMaterial?.Invoke(polygon.Face.Material);
            if (faceSelected) AddHighlight(polygon);
            if (ClipBrushMaterial.IsPlayerClip(polygon.Face.Material) || CaulkMaterial.IsCaulk(polygon.Face.Material) ||
                !OceanSurfaceGeometry.IsVisibleSurface(polygon, source?.IsWater == true))
            {
                for (int index = 0; index < polygon.Vertices.Length; index++)
                    AddLine(outlines, polygon.Vertices[index], polygon.Vertices[(index + 1) % polygon.Vertices.Length],
                        selected ? highlight : CaulkMaterial.IsCaulk(polygon.Face.Material) ? caulkColor :
                            source?.IsWater == true ? wireColor : clipColor);
                continue;
            }
            var geometry = GetMaterialGeometry(polygon.Face.Material);
            int start = geometry.Triangles.Count;
            AddPolygon(polygon, geometry.Triangles, Vector3.One, selected ? highlight : null, geometry.Lines,
                source?.Ocean, source?.IsWater == true && staticBrushes.Contains(brush) ? shore.Contacts(polygon) : null);
            geometry.Surfaces.Add((start, geometry.Triangles.Count - start,
                polygon.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) / polygon.Vertices.Length));
        }
        foreach (var terrain in document.Terrains)
        {
            MapTerrain surface = terrain.GetSurface();
            var geometry = GetMaterialGeometry(terrain.Material);
            foreach (var (a, b, c) in surface.GetTriangles())
            {
                Vector3 p0 = surface.Vertices[a], p1 = surface.Vertices[b], p2 = surface.Vertices[c];
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
                    Vector4 color = surface.Colors[index];
                    Vector2 uv = surface.TextureCoordinates[index];
                    geometry.Triangles.Add(new SceneVertex(surface.Vertices[index], normal, uv, color));
                }
            }
        }
        foreach (MapEntity entity in document.Entities.Where(XModelGeometry.IsModel))
            if (editor.ResolveModel?.Invoke(entity.Properties["model"]) is { } model)
                foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                {
                    var geometry = GetMaterialGeometry(triangle.Material);
                    geometry.Triangles.AddRange([triangle.A, triangle.B, triangle.C]);
                    foreach (var edge in new[] { (triangle.A.Position, triangle.B.Position),
                                 (triangle.B.Position, triangle.C.Position), (triangle.C.Position, triangle.A.Position) })
                    {
                        AddLine(geometry.Lines, edge.Item1, edge.Item2, wireColor);
                        if (selectedObjects.Contains(entity)) AddLine(outlines, edge.Item1, edge.Item2, highlight);
                    }
                }
        var all = new List<SceneVertex>();
        foreach (var material in materials)
        {
            int start = all.Count;
            all.AddRange(material.Value.Triangles);
            foreach (var surface in material.Value.Surfaces)
                for (int vertex = surface.Start; vertex < surface.Start + surface.Count; vertex += 3)
                    SurfaceCenters.Add(start + vertex, surface.Center);
            int wireStart = all.Count;
            all.AddRange(material.Value.Lines);
            Batches.Add((material.Key, start, material.Value.Triangles.Count, wireStart, material.Value.Lines.Count));
        }
        GlyphStart = all.Count;
        foreach (var entity in document.Entities.Where(entity => PointEntityGeometry.IsPointEntity(entity) &&
                     (!XModelGeometry.IsModel(entity) || editor.ResolveModel?.Invoke(entity.Properties["model"]) is null)))
        {
            bool missingModel = XModelGeometry.IsModel(entity);
            Vector3 color = missingModel ? Vector3.UnitX :
                entity.ClassName == "light" ? new(1, 0.85f, 0.35f) : new(0.35f, 0.8f, 0.95f);
            if (!missingModel && entity.ClassName == "trigger_radius")
            {
                foreach (var line in PointEntityGeometry.GetRadiusLines(entity))
                    AddLine(outlines, line.A, line.B, selectedObjects.Contains(entity) ? highlight : color);
                continue;
            }
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
        HighlightStart = all.Count;
        all.AddRange(highlights);
        HighlightCount = all.Count - HighlightStart;
        OutlineStart = all.Count;
        all.AddRange(outlines);
        OutlineCount = all.Count - OutlineStart;
        AxesStart = all.Count;
        AddEntityConnections(all, document, selection, editor);
        foreach (MapEntity light in selection.Items.OfType<MapEntity>().Where(entity => entity.ClassName == "light"))
            foreach (var line in LightInfluenceGeometry.GetLines(editor, light))
                AddLine(all, line.A, line.B, new Vector3(1, 0.85f, 0.35f));
        if (tool == EditorTool.Vertex)
            foreach (object handle in SelectionGeometry.GetVertexHandles(selection))
            {
                if (SelectionGeometry.Bounds(handle) is not { } pointBounds) continue;
                Vector3 point = pointBounds.Min;
                Vector3 color = handle is TerrainVertexSelection terrainVertex && editor.IsPatchVertexLocked(terrainVertex)
                    ? new Vector3(1, 0.2f, 0.2f)
                    : selection.Contains(handle) ? highlight : new Vector3(0.65f, 0.9f, 1);
                const float radius = 3;
                AddLine(all, point - Vector3.UnitX * radius, point + Vector3.UnitX * radius, color);
                AddLine(all, point - Vector3.UnitY * radius, point + Vector3.UnitY * radius, color);
                AddLine(all, point - Vector3.UnitZ * radius, point + Vector3.UnitZ * radius, color);
            }
        if (tool is EditorTool.Select or EditorTool.Vertex && selection.Count > 0 &&
            selection.Items.All(SelectionGeometry.CanTransform) && editor.Bounds(selection.Items) is { } selectionBounds)
            foreach (var line in TransformGizmoGeometry.GetLines(selectionBounds, transformMode))
                AddLine(all, line.A, line.B, line.Color);
        AxesCount = all.Count - AxesStart;
        Vertices = all.ToArray();

        void AddPolygon(MapPolygon polygon, List<SceneVertex> vertices, Vector3 color, Vector3? outlineColor,
            List<SceneVertex>? wireframe = null, OceanWaveSettings? ocean = null,
            IReadOnlyList<(Vector3 A, Vector3 B)>? contacts = null)
        {
            Vector3 normal = polygon.Face.Normal;
            var projection = SurfaceProjection.Parse(polygon.Face.Projection).GetMapping(normal);
            foreach (MapPolygon tile in OceanSurfaceGeometry.Subdivide(polygon, ocean, contacts))
            for (int i = 1; i < tile.Vertices.Length - 1; i++)
            {
                Add(tile.Vertices[0]);
                Add(tile.Vertices[i]);
                Add(tile.Vertices[i + 1]);
            }
            for (int i = 0; i < polygon.Vertices.Length; i++)
            {
                Vector3 a = polygon.Vertices[i], b = polygon.Vertices[(i + 1) % polygon.Vertices.Length];
                if (outlineColor is { } lineColor)
                    AddLine(outlines, a, b, lineColor);
                if (wireframe is not null)
                    AddLine(wireframe, a, b, wireColor);
            }

            void Add(Vector3 position)
            {
                Vector4 vertexColor = ocean is null ? new Vector4(color, 1) : OceanSurfaceGeometry.VertexColor(polygon, ocean, position, contacts);
                if (contacts is not null) vertexColor.W = WaterShoreGeometry.VertexAlpha(position, contacts);
                vertices.Add(new SceneVertex(position, normal,
                    new Vector2(Vector3.Dot(position, projection.U), Vector3.Dot(position, projection.V)) + projection.Offset, vertexColor));
            }
        }

        void AddHighlight(MapPolygon polygon)
        {
            Vector3 normal = polygon.Face.Normal;
            Vector4 color = new(highlight, 0.32f);
            for (int index = 1; index < polygon.Vertices.Length - 1; index++)
            {
                Add(polygon.Vertices[0]);
                Add(polygon.Vertices[index]);
                Add(polygon.Vertices[index + 1]);
            }

            void Add(Vector3 position) => highlights.Add(new SceneVertex(position, normal, Vector2.Zero, color));
        }

        (List<SceneVertex> Triangles, List<SceneVertex> Lines, List<(int Start, int Count, Vector3 Center)> Surfaces) GetMaterialGeometry(string material)
        {
            if (!materials.TryGetValue(material, out var geometry))
                materials.Add(material, geometry = ([], [], []));
            return geometry;
        }

    }

    private static void AddLine(List<SceneVertex> vertices, Vector3 a, Vector3 b, Vector3 color)
    {
        vertices.Add(new SceneVertex(a, Vector3.UnitZ, Vector2.Zero, color));
        vertices.Add(new SceneVertex(b, Vector3.UnitZ, Vector2.Zero, color));
    }

    private static void AddEntityConnections(List<SceneVertex> vertices, MapDocument document, EditorSelection selection, EditorScene editor)
    {
        foreach (MapEntity source in document.Entities)
        {
            foreach (MapEntity destination in editor.ResolveTargets(source))
            {
                if (!selection.Contains(source) && !selection.Contains(destination)) continue;
                if (editor.Bounds(source) is not { } sourceBounds || editor.Bounds(destination) is not { } destinationBounds) continue;
                Vector3 start = sourceBounds.Min / 2 + sourceBounds.Max / 2, end = destinationBounds.Min / 2 + destinationBounds.Max / 2;
                Vector3 direction = end - start;
                float length = direction.Length();
                if (!float.IsFinite(length) || length < 0.001f) continue;
                direction /= length;
                Vector3 side = Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY));
                float arrow = Math.Min(12, length * 0.2f);
                Vector3 color = new(0.35f, 0.9f, 0.7f);
                AddLine(vertices, start, end, color);
                AddLine(vertices, end, end - direction * arrow + side * arrow * 0.4f, color);
                AddLine(vertices, end, end - direction * arrow - side * arrow * 0.4f, color);
            }
        }
    }

}
