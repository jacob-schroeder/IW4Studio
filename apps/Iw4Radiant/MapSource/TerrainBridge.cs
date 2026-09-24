using System.Globalization;
using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed record TerrainBridgeEdge(string Label, string Material, Vector3 Normal,
    Vector3[] Positions, Vector2[] TextureCoordinates, Vector4[] Colors,
    float LightmapSize, string? Smoothing, bool IsCurve = false, bool IsClosed = false,
    Vector3[]? SegmentNormals = null);

internal static class TerrainBridge
{
    internal static TerrainBridgeEdge[] Edges(MapTerrain terrain)
    {
        if (terrain.IsCurve)
            return CurveEdges(terrain);
        Vector3 normal = SurfaceNormal(terrain);
        int[][] boundaries =
        [
            Enumerable.Range(0, terrain.Height).ToArray(),
            Enumerable.Range(0, terrain.Height).Select(row => (terrain.Width - 1) * terrain.Height + row).ToArray(),
            Enumerable.Range(0, terrain.Width).Select(column => column * terrain.Height).ToArray(),
            Enumerable.Range(0, terrain.Width).Select(column => column * terrain.Height + terrain.Height - 1).ToArray()
        ];
        string[] names = ["First column", "Last column", "First row", "Last row"];
        return boundaries.Select((indices, index) => new TerrainBridgeEdge(
            Describe(names[index], terrain.Vertices[indices[0]], terrain.Vertices[indices[^1]]),
            terrain.Material, normal, indices.Select(i => terrain.Vertices[i]).ToArray(),
            indices.Select(i => terrain.TextureCoordinates[i]).ToArray(),
            indices.Select(i => terrain.Colors[i]).ToArray(), terrain.LightmapSize, terrain.Smoothing)).ToArray();
    }

    internal static TerrainBridgeEdge[] Edges(MapPolygon polygon)
    {
        if (polygon.Vertices.Length < 3)
            throw new ArgumentException("Choose a brush face with at least three corners.");
        var mapping = SurfaceProjection.Parse(polygon.Face.Projection).GetMapping(polygon.Face.Normal);
        Vector2 Uv(Vector3 point) => new(
            (float)(BrushGeometry.Dot(mapping.U, point) + mapping.Offset.X),
            (float)(BrushGeometry.Dot(mapping.V, point) + mapping.Offset.Y));
        return Enumerable.Range(0, polygon.Vertices.Length).Select(index =>
        {
            Vector3 a = polygon.Vertices[index], b = polygon.Vertices[(index + 1) % polygon.Vertices.Length];
            return new TerrainBridgeEdge(Describe($"Face edge {index + 1}", a, b), polygon.Face.Material,
                polygon.Face.Normal, [a, b], [Uv(a), Uv(b)], [Vector4.One, Vector4.One], 16, null);
        }).ToArray();
    }

    internal static (int First, int Second)? Suggest(IReadOnlyList<TerrainBridgeEdge> first,
        IReadOnlyList<TerrainBridgeEdge> second)
    {
        (int First, int Second)? best = null;
        float distance = float.PositiveInfinity;
        for (int a = 0; a < first.Count; a++)
        for (int b = 0; b < second.Count; b++)
        {
            TerrainBridgeEdge start = first[a], end = second[b];
            if (start.IsCurve != end.IsCurve) continue;
            float score;
            if (start.IsCurve)
            {
                if (!CompatibleCurveEdges(start, end)) continue;
                try { score = AlignCurveEdge(start, end).Distance; }
                catch (ArgumentException) { continue; }
            }
            else
            {
                if (!CompatibleCounts(start.Positions.Length, end.Positions.Length) ||
                    Vector3.Dot(start.Normal, end.Normal) <= 0.25f) continue;
                float forward = Vector3.Distance(start.Positions[0], end.Positions[0]) +
                    Vector3.Distance(start.Positions[^1], end.Positions[^1]);
                float reverse = Vector3.Distance(start.Positions[0], end.Positions[^1]) +
                    Vector3.Distance(start.Positions[^1], end.Positions[0]);
                score = Math.Min(forward, reverse);
            }
            if (score >= distance) continue;
            best = (a, b);
            distance = score;
        }
        return best;
    }

    internal static MapTerrain Create(TerrainBridgeEdge first, TerrainBridgeEdge second, int rows, float rise)
    {
        if (rows is < 2 or > 16)
            throw new ArgumentException("Choose 2–16 bridge rows between the two edges.");
        if (!float.IsFinite(rise))
            throw new ArgumentException("Center rise must be a finite number of map units.");
        Validate(first);
        Validate(second);
        if (first.IsCurve || second.IsCurve)
        {
            if (!first.IsCurve || !second.IsCurve)
                throw new ArgumentException("Choose two curved patch edges, or two terrain/brush edges. Mixed curve and flat-surface bridging is not available.");
            return CreateCurve(first, second, rows, rise);
        }
        if (!CompatibleCounts(first.Positions.Length, second.Positions.Length))
            throw new ArgumentException("The edges need equal vertex counts or one count must subdivide the other evenly. Choose another edge or split a terrain patch.");
        if (Vector3.Dot(first.Normal, second.Normal) <= 0.25f)
            throw new ArgumentException("The two surfaces face different directions. Choose faces pointing to the same side of the gap.");

        int columns = Math.Max(first.Positions.Length, second.Positions.Length);
        (Vector3[] a, Vector2[] au, Vector4[] ac) = Sample(first, columns);
        (Vector3[] b, Vector2[] bu, Vector4[] bc) = Sample(second, columns);
        float forward = Vector3.DistanceSquared(a[0], b[0]) + Vector3.DistanceSquared(a[^1], b[^1]);
        float reverse = Vector3.DistanceSquared(a[0], b[^1]) + Vector3.DistanceSquared(a[^1], b[0]);
        if (reverse < forward)
        {
            Array.Reverse(b);
            Array.Reverse(bu);
            Array.Reverse(bc);
        }
        if (Vector3.Dot(Vector3.Cross(a[^1] - a[0], b[0] - a[0]), first.Normal) < 0)
        {
            Array.Reverse(a);
            Array.Reverse(au);
            Array.Reverse(ac);
            Array.Reverse(b);
            Array.Reverse(bu);
            Array.Reverse(bc);
        }
        if (Enumerable.Range(0, columns).Any(index => Vector3.DistanceSquared(a[index], b[index]) <= 0.0001f))
            throw new ArgumentException("The chosen edges touch or cross. Choose separated boundaries; use Stitch for an existing seam.");

        bool matchingMaterial = string.Equals(first.Material, second.Material, StringComparison.Ordinal);
        var terrain = new MapTerrain
        {
            Material = first.Material, Width = columns, Height = rows,
            LightmapSize = first.LightmapSize, Smoothing = first.Smoothing ?? "smoothing_smooth",
            Vertices = new Vector3[columns * rows], TextureCoordinates = new Vector2[columns * rows],
            LightmapCoordinates = new Vector2[columns * rows], Colors = new Vector4[columns * rows],
            EdgeFlags = new int[columns * rows]
        };
        for (int x = 0; x < columns; x++)
        for (int y = 0; y < rows; y++)
        {
            float t = (float)y / (rows - 1);
            int index = x * rows + y;
            terrain.Vertices[index] = Vector3.Lerp(a[x], b[x], t) +
                Vector3.UnitZ * (rise * MathF.Sin(MathF.PI * t));
            terrain.TextureCoordinates[index] = matchingMaterial
                ? Vector2.Lerp(au[x], bu[x], t)
                : au[x] + new Vector2(0, Vector3.Distance(a[x], b[x]) * t / 128);
            terrain.LightmapCoordinates[index] = new Vector2((float)x / (columns - 1), t);
            terrain.Colors[index] = Vector4.Lerp(ac[x], bc[x], t);
            terrain.EdgeFlags[index] = 1;
        }
        foreach ((int i, int j, int k) in terrain.GetTriangles())
        {
            Vector3 normal = Vector3.Cross(terrain.Vertices[j] - terrain.Vertices[i],
                terrain.Vertices[k] - terrain.Vertices[i]);
            if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z) ||
                Vector3.Dot(normal, first.Normal) <= 0.00000001f)
                throw new ArgumentException("These edges and rise would fold or flatten the bridge. Choose another pair or reduce the center rise.");
        }
        return terrain;
    }

    private static TerrainBridgeEdge[] CurveEdges(MapTerrain curve)
    {
        int width = curve.Width, height = curve.Height;
        Vector3 Vertex(int x, int y) => curve.Vertices[x * height + y];
        bool closedColumns = Enumerable.Range(0, height).All(y =>
            Vector3.DistanceSquared(Vertex(0, y), Vertex(width - 1, y)) <= 0.0001f);
        bool closedRows = Enumerable.Range(0, width).All(x =>
            Vector3.DistanceSquared(Vertex(x, 0), Vertex(x, height - 1)) <= 0.0001f);
        var edges = new List<TerrainBridgeEdge>();
        if (!closedColumns)
        {
            edges.Add(Edge("First column", Enumerable.Range(0, height).Select(y => y).ToArray(), false, 0));
            edges.Add(Edge("Last column", Enumerable.Range(0, height).Select(y => (width - 1) * height + y).ToArray(), false, width - 1));
        }
        if (!closedRows)
        {
            string first = closedColumns ? "First rim" : "First row";
            string last = closedColumns ? "Last rim" : "Last row";
            edges.Add(Edge(first, Enumerable.Range(0, width).Select(x => x * height).ToArray(), true, 0));
            edges.Add(Edge(last, Enumerable.Range(0, width).Select(x => x * height + height - 1).ToArray(), true, height - 1));
        }
        return edges.ToArray();

        TerrainBridgeEdge Edge(string name, int[] indices, bool alongColumns, int boundary)
        {
            Vector3[] positions = indices.Select(i => curve.Vertices[i]).ToArray();
            var normals = new Vector3[indices.Length - 1];
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 across = alongColumns
                    ? ((boundary == 0 ? Vertex(i, 1) - Vertex(i, 0) :
                        Vertex(i, height - 1) - Vertex(i, height - 2)) +
                       (boundary == 0 ? Vertex(i + 1, 1) - Vertex(i + 1, 0) :
                        Vertex(i + 1, height - 1) - Vertex(i + 1, height - 2))) * 0.5f
                    : ((boundary == 0 ? Vertex(1, i) - Vertex(0, i) :
                        Vertex(width - 1, i) - Vertex(width - 2, i)) +
                       (boundary == 0 ? Vertex(1, i + 1) - Vertex(0, i + 1) :
                        Vertex(width - 1, i + 1) - Vertex(width - 2, i + 1))) * 0.5f;
                Vector3 normal = alongColumns
                    ? Vector3.Cross(positions[i + 1] - positions[i], across)
                    : Vector3.Cross(across, positions[i + 1] - positions[i]);
                if (!Finite(normal) || normal.LengthSquared() <= 0.00000001f)
                    throw new ArgumentException("The selected curve has a flattened boundary. Adjust its controls before bridging.");
                normals[i] = Vector3.Normalize(normal);
            }
            bool closed = Vector3.DistanceSquared(positions[0], positions[^1]) <= 0.0001f;
            string label = closed
                ? $"{name} · closed loop · center {Point(Center(positions, true))}"
                : Describe(name, positions[0], positions[^1]);
            return new TerrainBridgeEdge(label, curve.Material, normals[0], positions,
                indices.Select(i => curve.TextureCoordinates[i]).ToArray(),
                indices.Select(i => curve.Colors[i]).ToArray(), curve.LightmapSize, curve.Smoothing,
                IsCurve: true, IsClosed: closed, SegmentNormals: normals);
        }
    }

    private static MapTerrain CreateCurve(TerrainBridgeEdge first, TerrainBridgeEdge second, int rows, float rise)
    {
        if (rows is < 3 or > 15 || rows % 2 == 0)
            throw new ArgumentException("A curved bridge needs 3, 5, 7, …, or 15 control rows.");
        if (!CompatibleCurveEdges(first, second))
            throw new ArgumentException("Choose curved boundaries with the same odd control count and matching open or closed rims.");
        var aligned = AlignCurveEdge(first, second);
        Vector3[] a = (Vector3[])first.Positions.Clone(), b = aligned.Positions;
        Vector2[] au = (Vector2[])first.TextureCoordinates.Clone(), bu = aligned.Texture;
        Vector4[] ac = (Vector4[])first.Colors.Clone(), bc = aligned.Colors;
        Vector3[] an = (Vector3[])first.SegmentNormals!.Clone(), bn = aligned.Normals;
        if (Enumerable.Range(0, a.Length).Any(index => Vector3.DistanceSquared(a[index], b[index]) <= 0.0001f))
            throw new ArgumentException("The chosen curve edges touch. Choose separated rims; use Stitch for an existing seam.");
        if (Vector3.Dot(Vector3.Cross(a[1] - a[0], b[0] - a[0]), an[0]) < 0)
        {
            Array.Reverse(a); Array.Reverse(au); Array.Reverse(ac); Array.Reverse(an);
            Array.Reverse(b); Array.Reverse(bu); Array.Reverse(bc); Array.Reverse(bn);
        }

        bool matchingMaterial = string.Equals(first.Material, second.Material, StringComparison.Ordinal);
        Vector3 firstCenter = first.IsClosed ? Center(a, true) : Vector3.Zero;
        Vector3 secondCenter = first.IsClosed ? Center(b, true) : Vector3.Zero;
        var bridge = new MapTerrain
        {
            IsCurve = true, Material = first.Material, Width = a.Length, Height = rows,
            LightmapSize = first.LightmapSize, Smoothing = first.Smoothing ?? "smoothing_smooth",
            Vertices = new Vector3[a.Length * rows], TextureCoordinates = new Vector2[a.Length * rows],
            LightmapCoordinates = new Vector2[a.Length * rows], Colors = new Vector4[a.Length * rows],
            EdgeFlags = new int[a.Length * rows]
        };
        for (int x = 0; x < a.Length; x++)
        {
            Vector3 lift = Vector3.UnitZ;
            if (first.IsClosed)
            {
                lift = (a[x] - firstCenter) + (b[x] - secondCenter);
                if (!Finite(lift) || lift.LengthSquared() <= 0.00000001f)
                    throw new ArgumentException("The curved rims have no consistent outward direction for center bulge.");
                lift = Vector3.Normalize(lift);
            }
            Vector2 endUv = matchingMaterial ? bu[x] :
                au[x] + new Vector2(0, Vector3.Distance(a[x], b[x]) / 128);
            for (int y = 0; y < rows; y++)
            {
                float t = (float)y / (rows - 1);
                float bulge = MathF.Sin(MathF.PI * t);
                if ((y & 1) != 0)
                {
                    float before = MathF.Sin(MathF.PI * (y - 1) / (rows - 1));
                    float after = MathF.Sin(MathF.PI * (y + 1) / (rows - 1));
                    bulge = 2 * bulge - (before + after) * 0.5f;
                }
                int index = x * rows + y;
                bridge.Vertices[index] = Vector3.Lerp(a[x], b[x], t) + lift * (rise * bulge);
                bridge.TextureCoordinates[index] = Vector2.Lerp(au[x], endUv, t);
                bridge.LightmapCoordinates[index] = new Vector2((float)x / (a.Length - 1), t);
                bridge.Colors[index] = Vector4.Lerp(ac[x], bc[x], t);
                bridge.EdgeFlags[index] = 1;
            }
        }
        MapTerrain surface = PatchGeometry.Evaluate(bridge);
        for (int x = 0; x < surface.Width - 1; x++)
        {
            Vector3 expected = an[x / 4] + bn[x / 4];
            if (!Finite(expected) || expected.LengthSquared() <= 0.00000001f)
                throw new ArgumentException("The curved rims face different directions.");
            for (int y = 0; y < surface.Height - 1; y++)
            {
                int i = x * surface.Height + y, j = i + surface.Height, k = j + 1, l = i + 1;
                Check(Vector3.Cross(surface.Vertices[j] - surface.Vertices[i], surface.Vertices[l] - surface.Vertices[i]));
                Check(Vector3.Cross(surface.Vertices[k] - surface.Vertices[j], surface.Vertices[l] - surface.Vertices[j]));
            }
            void Check(Vector3 normal)
            {
                if (!Finite(normal) || Vector3.Dot(normal, expected) <= 0.00000001f)
                    throw new ArgumentException("These curved rims and center shape would fold or flatten the bridge. Choose another pair or reduce the center rise.");
            }
        }
        return bridge;
    }

    private static bool CompatibleCurveEdges(TerrainBridgeEdge first, TerrainBridgeEdge second) =>
        first.IsCurve && second.IsCurve && first.IsClosed == second.IsClosed &&
        first.Positions.Length == second.Positions.Length &&
        first.Positions.Length is >= 3 and <= 15 && first.Positions.Length % 2 == 1;

    private static (Vector3[] Positions, Vector2[] Texture, Vector4[] Colors, Vector3[] Normals, float Distance)
        AlignCurveEdge(TerrainBridgeEdge first, TerrainBridgeEdge second)
    {
        int count = first.Positions.Length, unique = second.IsClosed ? count - 1 : count;
        float best = float.PositiveInfinity;
        Vector3[]? positions = null;
        Vector2[]? texture = null;
        Vector4[]? colors = null;
        Vector3[]? normals = null;
        for (int reversed = 0; reversed <= 1; reversed++)
        for (int shift = 0; shift < (second.IsClosed ? unique : 1); shift += 2)
        {
            var p = new Vector3[count];
            var uv = new Vector2[count];
            var c = new Vector4[count];
            var n = new Vector3[count - 1];
            bool facing = true;
            for (int i = 0; i < count; i++)
            {
                int source = second.IsClosed ? Mod(shift + (reversed == 0 ? i : -i), unique) :
                    reversed == 0 ? i : count - 1 - i;
                p[i] = second.Positions[source];
                c[i] = second.Colors[source];
                int wraps = second.IsClosed ? (shift + (reversed == 0 ? i : -i) - source) / unique : 0;
                uv[i] = second.TextureCoordinates[source] +
                    wraps * (second.TextureCoordinates[^1] - second.TextureCoordinates[0]);
                if (i == count - 1) continue;
                int segment = second.IsClosed ? Mod(shift + (reversed == 0 ? i : -i - 1), unique) :
                    reversed == 0 ? i : count - 2 - i;
                n[i] = second.SegmentNormals![segment];
                facing &= Vector3.Dot(first.SegmentNormals![i], n[i]) > 0.25f;
            }
            if (!facing) continue;
            float distance = 0;
            for (int i = 0; i < unique; i++)
                distance += Vector3.DistanceSquared(first.Positions[i], p[i]);
            if (distance >= best) continue;
            best = distance;
            positions = p; texture = uv; colors = c; normals = n;
        }
        if (positions is null || texture is null || colors is null || normals is null)
            throw new ArgumentException("The curved rims face different directions or cannot be aligned.");
        return (positions, texture, colors, normals, best);
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;

    private static Vector3 Center(Vector3[] positions, bool closed)
    {
        int count = closed ? positions.Length - 1 : positions.Length;
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < count; i++) sum += positions[i];
        return sum / count;
    }

    private static (Vector3[] Positions, Vector2[] Texture, Vector4[] Colors) Sample(TerrainBridgeEdge edge, int count)
    {
        int ratio = (count - 1) / (edge.Positions.Length - 1);
        var positions = new Vector3[count];
        var texture = new Vector2[count];
        var colors = new Vector4[count];
        for (int i = 0; i < count; i++)
        {
            int left = Math.Min(i / ratio, edge.Positions.Length - 2);
            float t = (float)(i - left * ratio) / ratio;
            positions[i] = Vector3.Lerp(edge.Positions[left], edge.Positions[left + 1], t);
            texture[i] = Vector2.Lerp(edge.TextureCoordinates[left], edge.TextureCoordinates[left + 1], t);
            colors[i] = Vector4.Lerp(edge.Colors[left], edge.Colors[left + 1], t);
        }
        return (positions, texture, colors);
    }

    private static bool CompatibleCounts(int a, int b) =>
        a is >= 2 and <= 16 && b is >= 2 and <= 16 &&
        ((a - 1) % (b - 1) == 0 || (b - 1) % (a - 1) == 0);

    private static void Validate(TerrainBridgeEdge edge)
    {
        int count = edge.Positions.Length;
        bool validCount = edge.IsCurve ? count is >= 3 and <= 15 && count % 2 == 1 : count is >= 2 and <= 16;
        if (!validCount || edge.TextureCoordinates.Length != count || edge.Colors.Length != count ||
            string.IsNullOrWhiteSpace(edge.Material) || !float.IsFinite(edge.LightmapSize) || edge.LightmapSize <= 0 ||
            !Finite(edge.Normal) || edge.Normal.LengthSquared() <= 0 ||
            (edge.IsCurve && (edge.SegmentNormals is null || edge.SegmentNormals.Length != count - 1 ||
                edge.SegmentNormals.Any(normal => !Finite(normal) || normal.LengthSquared() <= 0) ||
                edge.IsClosed && Vector3.DistanceSquared(edge.Positions[0], edge.Positions[^1]) > 0.0001f)))
            throw new ArgumentException("A bridge edge has invalid material, grid, or vertex attributes.");
        for (int i = 0; i < count; i++)
        {
            Vector2 uv = edge.TextureCoordinates[i];
            Vector4 color = edge.Colors[i];
            if (!Finite(edge.Positions[i]) || !float.IsFinite(uv.X) || !float.IsFinite(uv.Y) ||
                !float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) || !float.IsFinite(color.W) ||
                color.X < 0 || color.X > 1 || color.Y < 0 || color.Y > 1 ||
                color.Z < 0 || color.Z > 1 || color.W < 0 || color.W > 1)
                throw new ArgumentException("A bridge edge has nonfinite coordinates or invalid vertex color.");
        }
    }

    private static Vector3 SurfaceNormal(MapTerrain terrain)
    {
        Vector3 normal = Vector3.Zero;
        foreach ((int a, int b, int c) in terrain.GetTriangles())
            normal += Vector3.Cross(terrain.Vertices[b] - terrain.Vertices[a],
                terrain.Vertices[c] - terrain.Vertices[a]);
        if (!Finite(normal) || normal.LengthSquared() <= 0.00000001f)
            throw new ArgumentException("The selected terrain has no usable front side.");
        return Vector3.Normalize(normal);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static string Describe(string name, Vector3 first, Vector3 last) =>
        $"{name} · {Point(first)} → {Point(last)}";

    private static string Point(Vector3 value) =>
        $"({Number(value.X)}, {Number(value.Y)}, {Number(value.Z)})";

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
