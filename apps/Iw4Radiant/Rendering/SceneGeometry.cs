using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Material;
using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.Compilation;

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
    internal int LeakPathStart { get; }
    internal int LeakPathCount { get; }
    internal List<(int Start, int Count)> MovePreviewRanges { get; } = [];
    internal List<(MapEntity Source, MapEntity Target, int Start, int Count)> MoveConnectionRanges { get; } = [];
    internal List<(MapEntity Source, int Start, int Count)> LightInfluenceRanges { get; } = [];
    internal List<(MapEntity Source, string Material, int Start, int Count, int WireStart, int WireCount)> DestructibleRanges { get; } = [];
    internal List<(MapEntity Source, int Start, int Count)> DestructibleOutlines { get; } = [];
    internal List<(MapEntity Owner, string Material, int Start, int Count, int WireStart, int WireCount)> PhysicsBatches { get; } = [];
    internal List<(MapEntity Owner, int Start, int Count)> PhysicsOutlines { get; } = [];
    internal Dictionary<MapEntity, (Vector3 Min, Vector3 Max)> PhysicsBounds { get; } = new(ReferenceEqualityComparer.Instance);

    internal SceneGeometry(EditorScene editor, TransformMode transformMode, EditorTool tool,
        Func<string, MaterialSource?>? resolveMaterial, LeakPath? leakPath, int leakPointIndex,
        bool outlineFxMarkers, MapStageLighting? stages = null)
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
        IReadOnlyDictionary<MapEntity, MapEntity>? physicsPoses = editor.PhysicsPlacementPreview;
        bool movePreview = physicsPoses is null && selection.Count > 0 && selection.Items.All(item =>
            item is MapEntity entity && (entity.ClassName is "fx_origin" or "light" or "info_null" || XModelGeometry.IsModel(entity)));
        var modelPreviewRanges = new List<(string Material, int TriangleStart, int TriangleCount, int WireStart, int WireCount)>();
        var destructibleRanges = new List<(MapEntity Source, string Material, int TriangleStart, int TriangleCount, int WireStart, int WireCount)>();
        var destructibleOutlines = new List<(MapEntity Source, int Start, int Count)>();
        var outlinePreviewRanges = new List<(int Start, int Count)>();
        var materials = new Dictionary<string, (List<SceneVertex> Triangles, List<SceneVertex> Lines, List<(int Start, int Count, Vector3 Center)> Surfaces)>(StringComparer.Ordinal);
        var physicsMaterials = new Dictionary<MapEntity, Dictionary<string, (List<SceneVertex> Triangles, List<SceneVertex> Lines,
            List<(int Start, int Count, Vector3 Center)> Surfaces)>>(ReferenceEqualityComparer.Instance);
        var physicsOutlines = new Dictionary<MapEntity, List<SceneVertex>>(ReferenceEqualityComparer.Instance);
        var highlights = new List<SceneVertex>();
        var outlines = new List<SceneVertex>();
        var highlight = new Vector3(1, 0.65f, 0.18f);
        var clipColor = new Vector3(0.85f, 0.35f, 0.85f);
        var caulkColor = new Vector3(0.45f, 0.68f, 0.72f);
        var wireColor = new Vector3(0.48f, 0.51f, 0.55f);
        foreach (var brush in document.Brushes.Concat(editor.ShatterFragments?.Keys ?? []))
        {
            if (editor.IsShatterSource(brush)) continue;
            if (BrushGlass.IsGlass(brush) && TryAddGlassPane(brush)) continue;
            foreach (var polygon in brush.GetPolygons())
            {
                MapEntity? physicsOwner = editor.PhysicsPlacementOwner(brush) ?? editor.ShatterFragmentOwner(brush);
                List<SceneVertex> ownerOutlines = physicsOwner is null ? outlines : GetPhysicsOutlines(physicsOwner);
                bool faceSelected = selectedFaces.Contains(polygon.Face);
                bool selected = editor.ShatterFragmentOwner(brush) is null &&
                    (selectedObjects.Contains(brush) || faceSelected);
                MaterialSource? source = resolveMaterial?.Invoke(polygon.Face.Material);
                if (faceSelected && physicsOwner is null) AddHighlight(polygon);
                if (ClipBrushMaterial.IsPlayerClip(polygon.Face.Material) || CaulkMaterial.IsCaulk(polygon.Face.Material) ||
                    !OceanSurfaceGeometry.IsVisibleSurface(polygon, source?.IsWater == true))
                {
                    for (int index = 0; index < polygon.Vertices.Length; index++)
                        AddLine(ownerOutlines, polygon.Vertices[index], polygon.Vertices[(index + 1) % polygon.Vertices.Length],
                            selected ? highlight : CaulkMaterial.IsCaulk(polygon.Face.Material) ? caulkColor :
                                source?.IsWater == true ? wireColor : clipColor);
                    continue;
                }
                var geometry = GetMaterialGeometry(polygon.Face.Material, physicsOwner);
                int start = geometry.Triangles.Count;
                AddPolygon(polygon, geometry.Triangles, Vector3.One, selected ? highlight : null, geometry.Lines,
                    ownerOutlines,
                    source?.Ocean, source?.IsWater == true && staticBrushes.Contains(brush) ? shore.Contacts(polygon) : null);
                Vector3 center = polygon.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) /
                    polygon.Vertices.Length;
                StampSun(geometry.Triangles, start, stages?.SunIndexAt(center) ?? (byte)1);
                geometry.Surfaces.Add((start, geometry.Triangles.Count - start, center));
            }
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
                byte sunIndex = stages?.SunIndexAt((p0 + p1 + p2) / 3) ?? (byte)1;
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
                    geometry.Triangles.Add(new SceneVertex(surface.Vertices[index], normal, uv, color, sunIndex));
                }
            }
        }
        foreach (MapEntity entity in document.Entities.Where(XModelGeometry.IsModel))
            if (editor.ResolveModel?.Invoke(entity.Properties["model"]) is { } model)
            {
                MapEntity? physicsOwner = editor.PhysicsPlacementOwner(entity);
                MapEntity? destructibleSource = physicsOwner is null && editor.Owner(entity) is MapEntity source &&
                    DestructiblePresets.HasDiscoveryName(source) ? source : null;
                List<SceneVertex> ownerOutlines = physicsOwner is null ? outlines : GetPhysicsOutlines(physicsOwner);
                bool moving = movePreview && selectedObjects.Contains(entity);
                var starts = new Dictionary<string, (int Triangles, int Lines)>(StringComparer.Ordinal);
                int outlineStart = ownerOutlines.Count;
                byte modelSunIndex = stages?.SunIndexAt(Vector3.Transform(
                    model.Bounds.Min * 0.5f + model.Bounds.Max * 0.5f, XModelGeometry.Transform(entity))) ?? (byte)1;
                foreach (var triangle in XModelGeometry.GetTriangles(entity, model))
                {
                    var geometry = GetMaterialGeometry(triangle.Material, physicsOwner);
                    if (moving || destructibleSource is not null)
                        starts.TryAdd(triangle.Material, (geometry.Triangles.Count, geometry.Lines.Count));
                    geometry.Triangles.AddRange([triangle.A.WithSunIndex(modelSunIndex),
                        triangle.B.WithSunIndex(modelSunIndex), triangle.C.WithSunIndex(modelSunIndex)]);
                    foreach (var edge in new[] { (triangle.A.Position, triangle.B.Position),
                                 (triangle.B.Position, triangle.C.Position), (triangle.C.Position, triangle.A.Position) })
                    {
                        AddLine(geometry.Lines, edge.Item1, edge.Item2, wireColor);
                        if (selectedObjects.Contains(entity)) AddLine(ownerOutlines, edge.Item1, edge.Item2, highlight);
                    }
                }
                if (destructibleSource is not null)
                {
                    foreach (var (material, start) in starts)
                    {
                        var geometry = materials[material];
                        destructibleRanges.Add((destructibleSource, material, start.Triangles,
                            geometry.Triangles.Count - start.Triangles, start.Lines, geometry.Lines.Count - start.Lines));
                    }
                    destructibleOutlines.Add((destructibleSource, outlineStart, ownerOutlines.Count - outlineStart));
                }
                if (moving)
                {
                    foreach (var (material, start) in starts)
                    {
                        var geometry = materials[material];
                        modelPreviewRanges.Add((material, start.Triangles, geometry.Triangles.Count - start.Triangles,
                            start.Lines, geometry.Lines.Count - start.Lines));
                    }
                    AddOutlinePreviewRange(outlineStart, ownerOutlines.Count - outlineStart);
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
            foreach (var range in modelPreviewRanges.Where(range => range.Material == material.Key))
            {
                AddMovePreviewRange(start + range.TriangleStart, range.TriangleCount);
                AddMovePreviewRange(wireStart + range.WireStart, range.WireCount);
            }
            foreach (var range in destructibleRanges.Where(range => range.Material == material.Key))
                DestructibleRanges.Add((range.Source, material.Key, start + range.TriangleStart,
                    range.TriangleCount, wireStart + range.WireStart, range.WireCount));
        }
        foreach (var owner in physicsMaterials)
        foreach (var material in owner.Value)
        {
            int start = all.Count;
            all.AddRange(material.Value.Triangles);
            int wireStart = all.Count;
            all.AddRange(material.Value.Lines);
            PhysicsBatches.Add((owner.Key, material.Key, start, material.Value.Triangles.Count,
                wireStart, material.Value.Lines.Count));
            IncludePhysicsBounds(owner.Key, material.Value.Triangles);
            IncludePhysicsBounds(owner.Key, material.Value.Lines);
        }
        GlyphStart = all.Count;
        foreach (var entity in document.Entities.Where(entity => PointEntityGeometry.IsPointEntity(entity) &&
                     (!XModelGeometry.IsModel(entity) || editor.ResolveModel?.Invoke(entity.Properties["model"]) is null)))
        {
            bool selected = selectedObjects.Contains(entity);
            if (MistPainting.IsPainted(entity) && !selected)
            {
                if (editor.MistPaintingActive)
                    foreach (var line in PointEntityGeometry.GetMistGuideLines(entity))
                        AddLine(outlines, line.A, line.B, new Vector3(0.55f, 0.78f, 0.82f));
                continue;
            }
            bool missingModel = XModelGeometry.IsModel(entity);
            bool moving = movePreview && selected;
            int glyphStart = all.Count, outlineStart = outlines.Count;
            Vector3 color = missingModel ? Vector3.UnitX :
                entity.ClassName == "light" ? new(1, 0.85f, 0.35f) :
                entity.ClassName == "fx_origin" && entity.Properties.GetValueOrDefault("is_sound") == "1"
                    ? new(0.48f, 0.74f, 0.88f) :
                entity.ClassName == "fx_origin" ? new(0.91f, 0.71f, 0.42f) : new(0.35f, 0.8f, 0.95f);
            if (!missingModel && entity.ClassName == "trigger_radius")
            {
                foreach (var line in PointEntityGeometry.GetRadiusLines(entity))
                    AddLine(outlines, line.A, line.B, selectedObjects.Contains(entity) ? highlight : color);
                continue;
            }
            if (selectedObjects.Contains(entity))
                foreach (var line in PointEntityGeometry.GetSoundRangeLines(entity))
                    AddLine(outlines, line.A, line.B, color);
            foreach (var polygon in PointEntityGeometry.CreateBrush(entity).GetPolygons())
            {
                if (outlineFxMarkers && entity.ClassName == "fx_origin" &&
                    entity.Properties.GetValueOrDefault("is_sound") != "1")
                {
                    for (int index = 0; index < polygon.Vertices.Length; index++)
                        AddLine(outlines, polygon.Vertices[index],
                            polygon.Vertices[(index + 1) % polygon.Vertices.Length],
                            selectedObjects.Contains(entity) ? highlight : color);
                }
                else AddPolygon(polygon, all, color, selectedObjects.Contains(entity) ? highlight : color * 0.6f);
            }
            if (moving)
            {
                AddMovePreviewRange(glyphStart, all.Count - glyphStart);
                AddOutlinePreviewRange(outlineStart, outlines.Count - outlineStart);
            }
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
        foreach (var range in destructibleOutlines)
            DestructibleOutlines.Add((range.Source, OutlineStart + range.Start, range.Count));
        foreach (var range in outlinePreviewRanges)
            AddMovePreviewRange(OutlineStart + range.Start, range.Count);
        foreach (var owner in physicsOutlines)
        {
            int start = all.Count;
            all.AddRange(owner.Value);
            PhysicsOutlines.Add((owner.Key, start, owner.Value.Count));
            IncludePhysicsBounds(owner.Key, owner.Value);
        }
        AxesStart = all.Count;
        MapEntity? selectedVehicle = selection.Items.OfType<MapEntity>().FirstOrDefault(VehiclePathPreview.IsNode);
        AddEntityConnections(all, document, selection, editor, selectedVehicle is not null,
            movePreview && selection.Items.OfType<MapEntity>().Any(entity => entity.ClassName is "light" or "info_null"));
        if (selectedVehicle is not null) AddVehiclePathPreview(all, document, selectedVehicle);
        foreach (MapEntity light in selection.Items.OfType<MapEntity>().Where(entity => entity.ClassName == "light"))
        {
            int start = all.Count;
            AddLightInfluence(all, editor, light);
            if (editor.Owner(light) is MapEntity { ClassName: "light" } source)
                LightInfluenceRanges.Add((source, start, all.Count - start));
        }
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
        int gizmoStart = all.Count;
        if (physicsPoses is null && tool is (EditorTool.Select or EditorTool.Vertex) && selection.Count > 0 &&
            selection.Items.All(SelectionGeometry.CanTransform) && editor.Bounds(selection.Items) is { } selectionBounds)
            foreach (var line in TransformGizmoGeometry.GetLines(selectionBounds, transformMode))
                AddLine(all, line.A, line.B, line.Color);
        if (movePreview) AddMovePreviewRange(gizmoStart, all.Count - gizmoStart);
        AxesCount = all.Count - AxesStart;
        LeakPathStart = all.Count;
        if (leakPath is { } path)
        {
            Vector3 red = new(1, 0.12f, 0.12f);
            for (int index = 1; index < path.Points.Count; index++)
                AddLine(all, path.Points[index - 1], path.Points[index], red);
            Vector3 point = path.Points[leakPointIndex];
            const float radius = 8;
            AddLine(all, point - Vector3.UnitX * radius, point + Vector3.UnitX * radius, red);
            AddLine(all, point - Vector3.UnitY * radius, point + Vector3.UnitY * radius, red);
            AddLine(all, point - Vector3.UnitZ * radius, point + Vector3.UnitZ * radius, red);
        }
        LeakPathCount = all.Count - LeakPathStart;
        Vertices = all.ToArray();

        static void StampSun(List<SceneVertex> vertices, int start, byte sunIndex)
        {
            for (int index = start; index < vertices.Count; index++)
                vertices[index] = vertices[index].WithSunIndex(sunIndex);
        }

        void AddMovePreviewRange(int start, int count)
        {
            if (count > 0) MovePreviewRanges.Add((start, count));
        }

        void AddOutlinePreviewRange(int start, int count)
        {
            if (count > 0) outlinePreviewRanges.Add((start, count));
        }

        bool TryAddGlassPane(MapBrush brush)
        {
            MapPolygon pane;
            float thickness;
            try { (pane, thickness) = BrushGlass.ReadPane(brush); }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or NotSupportedException)
            {
                // Keep unsupported panes editable with their ordinary brush geometry.
                return false;
            }

            MapEntity? owner = editor.PhysicsPlacementOwner(brush);
            var geometry = GetMaterialGeometry(pane.Face.Material, owner);
            int start = geometry.Triangles.Count;
            Vector3 offset = -pane.Face.Normal * (thickness * 0.5f);
            // Native glass keeps its face UVs on a centered 2D outline, with depth stored separately.
            AddPolygon(pane, geometry.Triangles, Vector3.One, null);
            int end = geometry.Triangles.Count;
            for (int index = start; index < end; index++)
            {
                SceneVertex vertex = geometry.Triangles[index];
                geometry.Triangles[index] = new SceneVertex(vertex.Position + offset, vertex.Normal, vertex.Uv, vertex.Color);
            }
            MaterialSurfaceState surface = resolveMaterial?.Invoke(pane.Face.Material)?.Surface ?? MaterialSurfaceState.Opaque;
            if (surface.CullFace != GfxCullFace.None)
                for (int index = start; index < end; index += 3)
                    for (int corner = 2; corner >= 0; corner--)
                    {
                        SceneVertex vertex = geometry.Triangles[index + corner];
                        geometry.Triangles.Add(new SceneVertex(vertex.Position, -vertex.Normal, vertex.Uv, vertex.Color));
                    }
            Vector3 center = pane.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) /
                pane.Vertices.Length + offset;
            StampSun(geometry.Triangles, start, stages?.SunIndexAt(center) ?? (byte)1);
            geometry.Surfaces.Add((start, geometry.Triangles.Count - start,
                center));
            for (int index = 0; index < pane.Vertices.Length; index++)
                AddLine(geometry.Lines, pane.Vertices[index] + offset,
                    pane.Vertices[(index + 1) % pane.Vertices.Length] + offset, wireColor);

            // Retain the authored bounds and individual face highlights for brush editing.
            List<SceneVertex> ownerOutlines = owner is null ? outlines : GetPhysicsOutlines(owner);
            foreach (MapPolygon polygon in brush.GetPolygons())
            {
                bool faceSelected = selectedFaces.Contains(polygon.Face);
                if (faceSelected && owner is null) AddHighlight(polygon);
                if (!selectedObjects.Contains(brush) && !faceSelected) continue;
                for (int index = 0; index < polygon.Vertices.Length; index++)
                    AddLine(ownerOutlines, polygon.Vertices[index],
                        polygon.Vertices[(index + 1) % polygon.Vertices.Length], highlight);
            }
            return true;
        }

        void AddPolygon(MapPolygon polygon, List<SceneVertex> vertices, Vector3 color, Vector3? outlineColor,
            List<SceneVertex>? wireframe = null, List<SceneVertex>? ownerOutline = null, OceanWaveSettings? ocean = null,
            IReadOnlyList<(Vector3 A, Vector3 B)>? contacts = null)
        {
            Vector3 normal = polygon.Face.Normal;
            var projection = SurfaceProjection.Parse(polygon.Face.Projection).GetMapping(normal);
            foreach (MapPolygon tile in OceanSurfaceGeometry.Subdivide(polygon, ocean, contacts, contacts is null ? null : shore))
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
                    AddLine(ownerOutline ?? outlines, a, b, lineColor);
                if (wireframe is not null)
                    AddLine(wireframe, a, b, wireColor);
            }

            void Add(Vector3 position)
            {
                Vector4 vertexColor = ocean is null ? new Vector4(color, 1) : OceanSurfaceGeometry.VertexColor(polygon, ocean, position, contacts is null ? null : shore);
                if (contacts is not null && ocean is null) vertexColor.W = WaterShoreGeometry.VertexAlpha(position, contacts);
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

        List<SceneVertex> GetPhysicsOutlines(MapEntity owner)
        {
            if (!physicsOutlines.TryGetValue(owner, out var vertices))
                physicsOutlines.Add(owner, vertices = []);
            return vertices;
        }

        void IncludePhysicsBounds(MapEntity owner, List<SceneVertex> vertices)
        {
            foreach (SceneVertex vertex in vertices)
                PhysicsBounds[owner] = PhysicsBounds.TryGetValue(owner, out var bounds)
                    ? (Vector3.Min(bounds.Min, vertex.Position), Vector3.Max(bounds.Max, vertex.Position))
                    : (vertex.Position, vertex.Position);
        }

        (List<SceneVertex> Triangles, List<SceneVertex> Lines, List<(int Start, int Count, Vector3 Center)> Surfaces) GetMaterialGeometry(
            string material, MapEntity? physicsOwner = null)
        {
            var target = materials;
            if (physicsOwner is not null)
            {
                if (!physicsMaterials.TryGetValue(physicsOwner, out target))
                    physicsMaterials.Add(physicsOwner, target = new(StringComparer.Ordinal));
            }
            if (!target.TryGetValue(material, out var geometry))
                target.Add(material, geometry = ([], [], []));
            return geometry;
        }

    }

    internal static void AddLightInfluence(List<SceneVertex> vertices, EditorScene scene, MapEntity light)
    {
        foreach (var line in LightInfluenceGeometry.GetLines(scene, light))
            AddLine(vertices, line.A, line.B, line.Color);
    }

    private static void AddLine(List<SceneVertex> vertices, Vector3 a, Vector3 b, Vector3 color)
    {
        vertices.Add(new SceneVertex(a, Vector3.UnitZ, Vector2.Zero, color));
        vertices.Add(new SceneVertex(b, Vector3.UnitZ, Vector2.Zero, color));
    }

    private void AddEntityConnections(List<SceneVertex> vertices, MapDocument document, EditorSelection selection,
        EditorScene editor, bool vehiclePreview, bool trackMove)
    {
        foreach (MapEntity source in document.Entities)
        {
            if (vehiclePreview && VehiclePathPreview.IsNode(source)) continue;
            foreach (MapEntity destination in editor.ResolveTargets(source))
            {
                if (!selection.Contains(source) && !selection.Contains(destination)) continue;
                int start = vertices.Count;
                AddEntityConnection(vertices, editor, source, destination);
                if (trackMove && vertices.Count > start)
                    MoveConnectionRanges.Add((source, destination, start, vertices.Count - start));
            }
        }
    }

    internal static void AddEntityConnection(List<SceneVertex> vertices, EditorScene editor,
        MapEntity source, MapEntity destination)
    {
        if (editor.Bounds(source) is not { } sourceBounds || editor.Bounds(destination) is not { } destinationBounds) return;
        Vector3 start = sourceBounds.Min / 2 + sourceBounds.Max / 2, end = destinationBounds.Min / 2 + destinationBounds.Max / 2;
        Vector3 direction = end - start;
        float length = direction.Length();
        if (!float.IsFinite(length)) return;
        Vector3 side = Vector3.Zero;
        if (length >= 0.001f)
        {
            direction /= length;
            side = Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY));
        }
        else end = start; // Keep the same slots while a dragged light crosses its aim target.
        float arrow = length >= 0.001f ? Math.Min(12, length * 0.2f) : 0;
        Vector3 color = new(0.35f, 0.9f, 0.7f);
        AddLine(vertices, start, end, color);
        AddLine(vertices, end, end - direction * arrow + side * arrow * 0.4f, color);
        AddLine(vertices, end, end - direction * arrow - side * arrow * 0.4f, color);
    }

    private static void AddVehiclePathPreview(List<SceneVertex> vertices, MapDocument document, MapEntity selectedEntity)
    {
        var preview = new VehiclePathPreview(document);
        if (preview.Find(selectedEntity) is not { } selected) return;
        HashSet<VehiclePathPreview.Node> connected = preview.ConnectedTo(selected);
        Vector3 pathColor = new(0.45f, 0.78f, 0.69f), cycleColor = new(0.91f, 0.66f, 0.36f),
            issueColor = new(0.92f, 0.47f, 0.45f), headingColor = new(0.71f, 0.63f, 0.92f);
        foreach (VehiclePathPreview.Node node in connected)
        {
            if (!node.HasPosition) continue;
            if (node.Next is { HasPosition: true } next && connected.Contains(next))
                AddArrow(node.Position, next.Position, node.Cycle && next.Cycle ? cycleColor : pathColor);
            if (node.Cycle) AddMarker(node.Position, 9, cycleColor);
            if (node.Warnings.Count > 0) AddMarker(node.Position, 13, issueColor);
        }
        if (!selected.HasPosition) return;
        if (selected.Heading is { } heading)
            AddArrow(selected.Position, selected.Position + heading * 40, headingColor);
        IReadOnlyList<(Vector3 Start, Vector3 End)> lookahead = preview.LookaheadSegments(selected);
        foreach (var segment in lookahead)
            for (int step = 0; step < 8; step += 2)
                AddLine(vertices, Vector3.Lerp(segment.Start, segment.End, step / 8f),
                    Vector3.Lerp(segment.Start, segment.End, (step + 1) / 8f), headingColor);
        if (lookahead.Count > 0) AddMarker(lookahead[^1].End, 5, headingColor);

        void AddMarker(Vector3 position, float radius, Vector3 color)
        {
            AddLine(vertices, position + new Vector3(-radius, -radius, 0), position + new Vector3(radius, radius, 0), color);
            AddLine(vertices, position + new Vector3(-radius, radius, 0), position + new Vector3(radius, -radius, 0), color);
        }

        void AddArrow(Vector3 start, Vector3 end, Vector3 color)
        {
            Vector3 difference = end - start;
            float length = difference.Length();
            if (!float.IsFinite(length) || length < 0.001f) return;
            Vector3 direction = difference / length;
            Vector3 side = Vector3.Normalize(Vector3.Cross(direction,
                MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY));
            float arrow = Math.Min(12, length * 0.2f);
            AddLine(vertices, start, end, color);
            AddLine(vertices, end, end - direction * arrow + side * arrow * 0.4f, color);
            AddLine(vertices, end, end - direction * arrow - side * arrow * 0.4f, color);
        }
    }

}
