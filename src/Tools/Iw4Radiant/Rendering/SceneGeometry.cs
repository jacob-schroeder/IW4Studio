using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource;

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

    internal SceneGeometry(MapDocument document, object? selection)
    {
        var materials = new Dictionary<string, (List<SceneVertex> Triangles, List<SceneVertex> Lines)>(StringComparer.Ordinal);
        var outlines = new List<SceneVertex>();
        var highlight = new Vector3(1, 0.65f, 0.18f);
        var wireColor = new Vector3(0.55f, 0.6f, 0.66f);
        foreach (var brush in document.Brushes)
        foreach (var polygon in brush.GetPolygons())
        {
            var geometry = GetMaterialGeometry(polygon.Face.Material);
            AddPolygon(polygon, geometry.Triangles, Vector3.One,
                ReferenceEquals(selection, brush) ? highlight : null, geometry.Lines);
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
                    if (ReferenceEquals(selection, terrain))
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
                AddPolygon(polygon, all, color, ReferenceEquals(selection, entity) ? highlight : color * 0.6f);
        }
        GlyphCount = all.Count - GlyphStart;
        GridStart = all.Count;
        for (int offset = -8192; offset <= 8192; offset += 128)
        {
            Vector3 color = offset % 1024 == 0 ? new(0.22f, 0.25f, 0.28f) : new(0.14f, 0.17f, 0.2f);
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
        if (selection is MapBrush selectedBrush)
            AddAxes(selectedBrush.GetBounds());
        else if (selection is MapTerrain selectedTerrain && selectedTerrain.Vertices.Length != 0)
            AddAxes(selectedTerrain.GetBounds());
        else if (selection is MapEntity selectedEntity && PointEntityGeometry.IsPointEntity(selectedEntity))
            AddAxes(PointEntityGeometry.CreateBrush(selectedEntity).GetBounds());
        AxesCount = all.Count - AxesStart;
        Vertices = all.ToArray();

        void AddPolygon(MapPolygon polygon, List<SceneVertex> vertices, Vector3 color, Vector3? outlineColor,
            List<SceneVertex>? wireframe = null)
        {
            Vector3 normal = polygon.Face.Normal;
            var projection = BrushProjection(polygon.Face);
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

        void AddAxes((Vector3 Min, Vector3 Max) bounds)
        {
            Vector3 center = (bounds.Min + bounds.Max) / 2;
            float length = Math.Clamp((bounds.Max - bounds.Min).Length() * 0.35f, 48, 256);
            Axis(Vector3.UnitX, Vector3.UnitY, new Vector3(1, 0.25f, 0.25f));
            Axis(Vector3.UnitY, Vector3.UnitZ, new Vector3(0.3f, 0.95f, 0.35f));
            Axis(Vector3.UnitZ, Vector3.UnitX, new Vector3(0.3f, 0.6f, 1));
            void Axis(Vector3 direction, Vector3 side, Vector3 color)
            {
                Vector3 tip = center + direction * length;
                AddLine(all, center, tip, color);
                AddLine(all, tip, tip - direction * length * 0.2f + side * length * 0.08f, color);
                AddLine(all, tip, tip - direction * length * 0.2f - side * length * 0.08f, color);
            }
        }
    }

    private static void AddLine(List<SceneVertex> vertices, Vector3 a, Vector3 b, Vector3 color)
    {
        vertices.Add(new SceneVertex(a, Vector3.UnitZ, Vector2.Zero, color));
        vertices.Add(new SceneVertex(b, Vector3.UnitZ, Vector2.Zero, color));
    }

    private static (Vector3 U, Vector3 V, Vector2 Offset) BrushProjection(MapFace face)
    {
        Vector3 normal = Vector3.Abs(face.Normal);
        Vector3 u = normal.Z >= normal.X && normal.Z >= normal.Y ? Vector3.UnitX :
            normal.X >= normal.Y ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v = normal.Z >= normal.X && normal.Z >= normal.Y ? -Vector3.UnitY : -Vector3.UnitZ;
        string[] values = face.Projection.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 5)
            return (u / 64, v / 64, Vector2.Zero);
        float Read(int index, float fallback) => float.TryParse(values[index], NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : fallback;
        float width = Read(0, 64), height = Read(1, 64), angle = Read(4, 0) * MathF.PI / 180;
        return ((u * MathF.Cos(angle) - v * MathF.Sin(angle)) / (MathF.Abs(width) < 0.001f ? 64 : width),
            (u * MathF.Sin(angle) + v * MathF.Cos(angle)) / (MathF.Abs(height) < 0.001f ? 64 : height),
            new Vector2(Read(2, 0), Read(3, 0)));
    }
}
