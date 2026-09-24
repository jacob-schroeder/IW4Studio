using System.Numerics;
using Iw4Radiant.Compilation;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal enum GeometryShape { Patch, Bevel, EndCap, Cylinder, Arch, Stairs }

internal static class GeometryEditing
{
    internal static TerrainBridgeEdge[] BridgeEdges(MapDocument document, object selected)
    {
        if (selected is MapTerrain terrain && document.World.Terrains.Contains(terrain))
        {
            MapSurfaceCompiler.ValidateTerrain(terrain);
            return TerrainBridge.Edges(terrain);
        }
        MapPolygon? face = selected switch
        {
            BrushFaceSelection chosen when document.World.Brushes.Contains(chosen.Brush) =>
                chosen.Brush.GetPolygons().FirstOrDefault(polygon => ReferenceEquals(polygon.Face, chosen.Face)),
            MapBrush brush when document.World.Brushes.Contains(brush) =>
                brush.GetPolygons().Where(polygon => polygon.Face.Normal.Z >= 0.5f)
                    .OrderByDescending(polygon => polygon.Face.Normal.Z)
                    .ThenByDescending(polygon => polygon.Vertices.Average(vertex => vertex.Z)).FirstOrDefault(),
            _ => null
        };
        return face is not null ? TerrainBridge.Edges(face) :
            throw new ArgumentException("Select two whole world curved patches, terrains, or brush faces. A whole brush uses its top face; use Face mode for another face.");
    }

    internal static void CreateBridge(EditorSession session, int firstEdge, int secondEdge, int rows, float rise)
    {
        if (session.Selection.Count != 2)
            throw new ArgumentException("Select exactly two source surfaces for a bridge.");
        object[] selected = session.Selection.Items.ToArray();
        if (ReferenceEquals(EditorSelection.Owner(selected[0]), EditorSelection.Owner(selected[1])))
            throw new ArgumentException("Choose boundaries on two different source surfaces.");
        TerrainBridgeEdge[] first = BridgeEdges(session.Document, selected[0]);
        TerrainBridgeEdge[] second = BridgeEdges(session.Document, selected[1]);
        if ((uint)firstEdge >= (uint)first.Length || (uint)secondEdge >= (uint)second.Length)
            throw new ArgumentException("Choose one boundary edge on each selected surface.");
        string material = first[firstEdge].Material;
        if (CaulkMaterial.IsCaulk(material) || ClipBrushMaterial.IsPlayerClip(material))
            throw new ArgumentException("The first surface must have a visible material. Choose a textured terrain or brush face first.");
        MapTerrain bridge = TerrainBridge.Create(first[firstEdge], second[secondEdge], rows, rise);
        MapSurfaceCompiler.ValidateTerrain(bridge);
        _ = TerrainContents.ReadNonColliding(bridge);
        session.Edit(() =>
        {
            session.Document.World.Terrains.Add(bridge);
            session.Tool = EditorTool.Select;
            session.Selection.Set(bridge);
        });
    }

    internal static int CreateRope(EditorSession session, float thickness, float slackPercent, int segments)
    {
        MapEntity[] endpoints = session.Selection.Items.OfType<MapEntity>().ToArray();
        if (session.Selection.Count != 2 || endpoints.Length != 2 ||
            endpoints.Any(entity => entity.ClassName != "info_null" || !session.Document.Entities.Contains(entity)))
            throw new ArgumentException("Select exactly two info_null point entities to set the rope endpoints.");
        if (endpoints.Any(entity => entity.Brushes.Count > 0 || entity.Terrains.Count > 0 ||
                entity.Properties.Keys.Any(key => key is not ("classname" or "origin" or "angles" or "angle"))))
            throw new ArgumentException("Use two temporary, unlinked info_null markers. Named or linked entities are preserved for their existing purpose.");
        if (!endpoints[0].TryGetOrigin(out Vector3 start) || !endpoints[1].TryGetOrigin(out Vector3 end))
            throw new ArgumentException("Both info_null endpoints need finite origin coordinates.");
        MapTerrain[] pieces = RopeGeometry.Create(start, end, thickness, slackPercent, segments, session.Material);
        foreach (MapTerrain piece in pieces)
        {
            MapSurfaceCompiler.ValidateTerrain(piece);
            _ = TerrainContents.ReadNonColliding(piece);
        }
        session.Edit(() =>
        {
            foreach (MapEntity endpoint in endpoints) session.Document.Entities.Remove(endpoint);
            session.Document.World.Terrains.AddRange(pieces);
            session.Tool = EditorTool.Select;
            session.Selection.SetRange(pieces);
        });
        return pieces.Length;
    }

    internal static void Create(EditorSession session, GeometryShape shape, Vector3 center, Vector3 size,
        int segments, float thickness, Matrix4x4 orientation, bool replaceBrush)
    {
        if (!BrushGeometry.IsFinite(center) || !BrushGeometry.IsFinite(size) || size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new ArgumentException("Shape dimensions must be positive, and all coordinates must be finite.");
        if (string.IsNullOrWhiteSpace(session.Material))
            throw new ArgumentException("Choose a material in the browser before creating geometry.");
        MapBrush? replaced = replaceBrush && session.Selection.Count == 1 ? session.Selection.Active as MapBrush : null;
        if (replaceBrush && replaced is null)
            throw new ArgumentException("Select one whole brush to replace it with the new shape.");
        MapEntity owner = replaced is not null ? session.Document.Entities.First(entity => entity.Brushes.Contains(replaced)) : session.Document.World;
        var brushes = new List<MapBrush>();
        var patches = new List<MapTerrain>();
        if (shape is GeometryShape.Arch or GeometryShape.Stairs)
        {
            if (segments < 2 || segments > 32)
                throw new ArgumentException("Use between 2 and 32 arch segments or stair steps.");
            if (shape == GeometryShape.Stairs)
            {
                for (int step = 0; step < segments; step++)
                    brushes.Add(MapBrush.CreateBox(new(-size.X / 2 + size.X * step / segments, -size.Y / 2, -size.Z / 2),
                        new(-size.X / 2 + size.X * (step + 1) / segments, size.Y / 2, -size.Z / 2 + size.Z * (step + 1) / segments), session.Material));
            }
            else
            {
                if (!float.IsFinite(thickness) || thickness <= 0 || thickness >= Math.Min(size.X / 2, size.Z))
                    throw new ArgumentException("Arch thickness must be positive and smaller than half its width and its height.");
                for (int segment = 0; segment < segments; segment++)
                {
                    float a = MathF.PI * segment / segments, b = MathF.PI * (segment + 1) / segments;
                    Vector2[] section = [Point(a, size.X / 2, size.Z), Point(b, size.X / 2, size.Z),
                        Point(b, size.X / 2 - thickness, size.Z - thickness), Point(a, size.X / 2 - thickness, size.Z - thickness)];
                    brushes.Add(CreatePrism(section, size.Y, session.Material));
                }
                Vector2 Point(float angle, float radiusX, float radiusZ) =>
                    new(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusZ - size.Z / 2);
            }
        }
        else
            patches.Add(CreateCurve(shape, size, session.Material));

        Matrix4x4 transform = orientation * Matrix4x4.CreateTranslation(center);
        foreach (MapBrush brush in brushes) brush.Transform(transform, textureLock: true);
        foreach (MapTerrain patch in patches) patch.Transform(transform);
        if (replaced is not null)
        {
            foreach (MapBrush brush in brushes) brush.Directives.AddRange(replaced.Directives);
            foreach (MapTerrain patch in patches) patch.Directives.AddRange(replaced.Directives);
        }
        session.Edit(() =>
        {
            if (replaced is not null) owner.Brushes.Remove(replaced);
            owner.Brushes.AddRange(brushes);
            owner.Terrains.AddRange(patches);
            session.Tool = EditorTool.Select;
            session.Selection.SetRange(brushes.Cast<object>().Concat(patches));
        });
    }

    internal static void EditPatches(EditorSession session, Func<MapTerrain, MapTerrain> edit)
    {
        MapTerrain[] patches = SelectedPatches(session);
        if (patches.Length == 0) throw new ArgumentException("Select a curved patch or one of its control points.");
        var changes = patches.Select(patch => (Original: patch, Proposed: edit(patch))).ToArray();
        session.Edit(() =>
        {
            foreach (var change in changes)
            {
                change.Original.Width = change.Proposed.Width;
                change.Original.Height = change.Proposed.Height;
                change.Original.Vertices = change.Proposed.Vertices;
                change.Original.TextureCoordinates = change.Proposed.TextureCoordinates;
                change.Original.LightmapCoordinates = change.Proposed.LightmapCoordinates;
                change.Original.Colors = change.Proposed.Colors;
                change.Original.EdgeFlags = change.Proposed.EdgeFlags;
            }
            // Point indices can change after topology edits; keep their parent patches selected.
            session.Selection.SetRange(patches);
        });
    }

    internal static void CapEnds(EditorSession session)
    {
        MapTerrain[] patches = SelectedPatches(session);
        if (patches.Length == 0) throw new ArgumentException("Select a curved patch to cap its ends.");
        var additions = patches.Select(patch => (Owner: session.Document.Entities.First(entity => entity.Terrains.Contains(patch)),
            Caps: PatchGeometry.CapEnds(patch))).ToArray();
        session.Edit(() =>
        {
            foreach (var addition in additions) addition.Owner.Terrains.AddRange(addition.Caps);
            session.Selection.SetRange(additions.SelectMany(addition => addition.Caps));
        });
    }

    internal static void SplitSurface(EditorSession session, MapTerrain surface, bool columns, int position)
    {
        MapEntity owner = session.Document.Entities.FirstOrDefault(entity => entity.Terrains.Contains(surface))
            ?? throw new ArgumentException("Select a surface in the current map to split.");
        (MapTerrain first, MapTerrain second) = surface.IsCurve
            ? TerrainSplit.ThroughCurveSpan(surface, columns, position)
            : TerrainSplit.AtGridLine(surface, columns, position);
        session.Edit(() =>
        {
            int index = owner.Terrains.IndexOf(surface);
            owner.Terrains.RemoveAt(index);
            owner.Terrains.InsertRange(index, [first, second]);
            session.Tool = EditorTool.Select;
            session.Selection.SetRange([first, second]);
        });
    }

    internal static int ConvertCurveToTerrain(EditorSession session, MapTerrain curve, int samplesPerSpan)
    {
        if (session.Selection.Count != 1 || !ReferenceEquals(session.Selection.Active, curve) ||
            !session.Document.World.Terrains.Contains(curve))
            throw new ArgumentException("Select one whole world curve to convert to terrain.");
        MapTerrain[] pieces = TerrainConversion.FromCurve(curve, samplesPerSpan);
        foreach (MapTerrain piece in pieces)
        {
            MapSurfaceCompiler.ValidateTerrain(piece);
            _ = TerrainContents.ReadNonColliding(piece);
        }
        session.Edit(() =>
        {
            int index = session.Document.World.Terrains.IndexOf(curve);
            session.Document.World.Terrains.RemoveAt(index);
            session.Document.World.Terrains.InsertRange(index, pieces);
            session.Tool = EditorTool.Select;
            session.Selection.SetRange(pieces);
        });
        return pieces.Length;
    }

    internal static int ThickenSurface(EditorSession session, MapTerrain source, float thickness)
    {
        if (session.Selection.Count != 1 || !ReferenceEquals(session.Selection.Active, source) ||
            !session.Document.World.Terrains.Contains(source))
            throw new ArgumentException("Select one whole world terrain or curved patch to thicken.");
        (MapTerrain[] top, MapTerrain[] shell) = PatchThickening.Create(source, thickness);
        foreach (MapTerrain piece in top.Concat(shell))
        {
            MapSurfaceCompiler.ValidateTerrain(piece);
            _ = TerrainContents.ReadNonColliding(piece);
        }
        session.Edit(() =>
        {
            int index = session.Document.World.Terrains.IndexOf(source);
            if (source.IsCurve)
            {
                session.Document.World.Terrains.RemoveAt(index);
                session.Document.World.Terrains.InsertRange(index, top.Concat(shell));
            }
            else
                session.Document.World.Terrains.InsertRange(index + 1, shell);
            session.Tool = EditorTool.Select;
            session.Selection.SetRange(top.Concat(shell));
        });
        return top.Length + shell.Length;
    }

    internal static void ConvertFaceToTerrain(EditorSession session, BrushFaceSelection selected, int verticesPerSide)
    {
        if (session.Selection.Count != 1 || !Equals(session.Selection.Active, selected) ||
            !session.Document.World.Brushes.Contains(selected.Brush) || !selected.Brush.Faces.Contains(selected.Face))
            throw new ArgumentException("Select one face of a world brush to convert to terrain.");
        if (CaulkMaterial.IsCaulk(selected.Face.Material) || ClipBrushMaterial.IsPlayerClip(selected.Face.Material))
            throw new ArgumentException("Choose a visible, textured brush face to convert.");
        MapPolygon polygon = selected.Brush.GetPolygons().FirstOrDefault(item => ReferenceEquals(item.Face, selected.Face))
            ?? throw new ArgumentException("The selected brush face has no usable boundary.");
        MapTerrain terrain = TerrainConversion.FromFace(polygon, verticesPerSide);
        TerrainContents.SetNonColliding(terrain, true);
        MapSurfaceCompiler.ValidateTerrain(terrain);
        session.Edit(() =>
        {
            selected.Face.Material = CaulkMaterial.Name;
            session.Document.World.Terrains.Add(terrain);
            session.Tool = EditorTool.Select;
            session.Selection.Set(terrain);
        });
    }

    internal static MapTerrain[] SelectedPatches(EditorSession session) => session.Selection.Items
        .Select(EditorSelection.Owner).OfType<MapTerrain>().Where(patch => patch.IsCurve).Distinct().ToArray();

    private static MapTerrain CreateCurve(GeometryShape shape, Vector3 size, string material)
    {
        int width = shape switch { GeometryShape.EndCap => 5, GeometryShape.Cylinder => 9, _ => 3 };
        var patch = new MapTerrain
        {
            IsCurve = true, Material = material, Width = width, Height = 3,
            Vertices = new Vector3[width * 3], TextureCoordinates = new Vector2[width * 3],
            LightmapCoordinates = new Vector2[width * 3], Colors = new Vector4[width * 3], EdgeFlags = new int[width * 3]
        };
        Vector2[] circle = [new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0),
            new(-1, -1), new(0, -1), new(1, -1), new(1, 0)];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < 3; y++)
        {
            int index = x * 3 + y;
            float u = (float)x / (width - 1), v = y * 0.5f;
            patch.Vertices[index] = shape == GeometryShape.Patch ? new((u - 0.5f) * size.X, (v - 0.5f) * size.Y, 0) :
                new(circle[x].X * size.X / 2, circle[x].Y * size.Y / 2, (v - 0.5f) * size.Z);
            // Bevel and half-cylinder controls fill the requested footprint rather than only half of it.
            if (shape == GeometryShape.Bevel)
                patch.Vertices[index] = new((circle[x].X - 0.5f) * size.X, (circle[x].Y - 0.5f) * size.Y, (v - 0.5f) * size.Z);
            if (shape == GeometryShape.EndCap)
                patch.Vertices[index].Y = (circle[x].Y - 0.5f) * size.Y;
            patch.TextureCoordinates[index] = new(u * size.X / 128, v * (shape == GeometryShape.Patch ? size.Y : size.Z) / 128);
            patch.LightmapCoordinates[index] = new(u, v);
            patch.Colors[index] = Vector4.One;
        }
        return patch;
    }

    private static MapBrush CreatePrism(Vector2[] section, float depth, string material)
    {
        var vertices = section.Select(point => new Vector3(point.X, -depth / 2, point.Y))
            .Concat(section.Select(point => new Vector3(point.X, depth / 2, point.Y))).ToArray();
        Vector3 center = vertices.Aggregate(Vector3.Zero, (sum, point) => sum + point / vertices.Length);
        var brush = new MapBrush();
        Add(0, 1, 2);
        Add(4, 5, 6);
        for (int i = 0; i < 4; i++) Add(i, (i + 1) % 4, (i + 1) % 4 + 4);
        BrushGeometry.Validate(brush);
        return brush;

        void Add(int a, int b, int c)
        {
            var face = new MapFace { A = vertices[a], B = vertices[b], C = vertices[c], Material = material };
            if (Vector3.Dot(face.Normal, center - face.A) > 0) (face.B, face.C) = (face.C, face.B);
            brush.Faces.Add(face);
        }
    }
}
