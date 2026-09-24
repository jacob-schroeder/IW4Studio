using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class TerrainSplit
{
    internal static (MapTerrain First, MapTerrain Second) AtGridLine(MapTerrain source, bool columns, int seam)
    {
        Validate(source);
        if (source.IsCurve)
            throw new ArgumentException("Choose a terrain mesh to split at a grid line.");
        int length = columns ? source.Width : source.Height;
        if (seam < 1 || seam >= length - 1)
            throw new ArgumentException("Choose an interior terrain column or row.");

        MapTerrain first = Allocate(source, columns ? seam + 1 : source.Width, columns ? source.Height : seam + 1);
        MapTerrain second = Allocate(source, columns ? length - seam : source.Width,
            columns ? source.Height : length - seam);
        CopyGrid(first, 0);
        CopyGrid(second, seam);
        EnsureArea(first);
        EnsureArea(second);
        return (first, second);

        void CopyGrid(MapTerrain part, int offset)
        {
            for (int x = 0; x < part.Width; x++)
            for (int y = 0; y < part.Height; y++)
                Copy(source, (columns ? x + offset : x) * source.Height + (columns ? y : y + offset),
                    part, x * part.Height + y);
        }
    }

    internal static (MapTerrain First, MapTerrain Second) ThroughCurveSpan(MapTerrain source, bool columns, int span)
    {
        Validate(source);
        if (!source.IsCurve)
            throw new ArgumentException("Choose a curved patch to split through a span.");
        int length = columns ? source.Width : source.Height;
        if (span < 1 || span > (length - 1) / 2)
            throw new ArgumentException("Choose a curve span within the selected patch.");

        int start = (span - 1) * 2;
        MapTerrain first = Allocate(source, columns ? start + 3 : source.Width,
            columns ? source.Height : start + 3);
        MapTerrain second = Allocate(source, columns ? length - start : source.Width,
            columns ? source.Height : length - start);
        int transverseLength = columns ? source.Height : source.Width;
        for (int transverse = 0; transverse < transverseLength; transverse++)
        {
            int a = Index(source, columns, start, transverse);
            int b = Index(source, columns, start + 1, transverse);
            int c = Index(source, columns, start + 2, transverse);
            for (int axis = 0; axis < (columns ? first.Width : first.Height); axis++)
            {
                int destination = Index(first, columns, axis, transverse);
                if (axis <= start) Copy(source, Index(source, columns, axis, transverse), first, destination);
                else if (axis == start + 1) Blend(source, a, b, c, first, destination, 0.5f, 0.5f, 0);
                else Blend(source, a, b, c, first, destination, 0.25f, 0.5f, 0.25f);
            }
            for (int axis = 0; axis < (columns ? second.Width : second.Height); axis++)
            {
                int destination = Index(second, columns, axis, transverse);
                if (axis == 0) Blend(source, a, b, c, second, destination, 0.25f, 0.5f, 0.25f);
                else if (axis == 1) Blend(source, a, b, c, second, destination, 0, 0.5f, 0.5f);
                else Copy(source, Index(source, columns, start + axis, transverse), second, destination);
            }
        }
        EnsureArea(first);
        EnsureArea(second);
        return (first, second);
    }

    private static MapTerrain Allocate(MapTerrain source, int width, int height)
    {
        MapTerrain part = source.Clone();
        int count = checked(width * height);
        part.Width = width;
        part.Height = height;
        part.Vertices = new Vector3[count];
        part.TextureCoordinates = new Vector2[count];
        part.LightmapCoordinates = new Vector2[count];
        part.Colors = new Vector4[count];
        part.EdgeFlags = new int[count];
        return part;
    }

    private static int Index(MapTerrain surface, bool columns, int axis, int transverse) =>
        columns ? axis * surface.Height + transverse : transverse * surface.Height + axis;

    private static void Copy(MapTerrain source, int from, MapTerrain target, int to)
    {
        target.Vertices[to] = source.Vertices[from];
        target.TextureCoordinates[to] = source.TextureCoordinates[from];
        target.LightmapCoordinates[to] = source.LightmapCoordinates[from];
        target.Colors[to] = source.Colors[from];
        target.EdgeFlags[to] = source.EdgeFlags[from];
    }

    private static void Blend(MapTerrain source, int a, int b, int c, MapTerrain target, int to,
        float wa, float wb, float wc)
    {
        target.Vertices[to] = source.Vertices[a] * wa + source.Vertices[b] * wb + source.Vertices[c] * wc;
        target.TextureCoordinates[to] = source.TextureCoordinates[a] * wa + source.TextureCoordinates[b] * wb +
            source.TextureCoordinates[c] * wc;
        target.LightmapCoordinates[to] = source.LightmapCoordinates[a] * wa + source.LightmapCoordinates[b] * wb +
            source.LightmapCoordinates[c] * wc;
        target.Colors[to] = source.Colors[a] * wa + source.Colors[b] * wb + source.Colors[c] * wc;
        target.EdgeFlags[to] = source.EdgeFlags[b];
    }

    private static void Validate(MapTerrain surface)
    {
        long count = (long)surface.Width * surface.Height;
        if (surface.Width < 2 || surface.Height < 2 || count != surface.Vertices.Length ||
            count != surface.TextureCoordinates.Length || count != surface.LightmapCoordinates.Length ||
            count != surface.Colors.Length || count != surface.EdgeFlags.Length ||
            surface.IsCurve && (surface.Width < 3 || surface.Height < 3 || surface.Width > 15 || surface.Height > 15 ||
                                surface.Width % 2 == 0 || surface.Height % 2 == 0))
            throw new ArgumentException("The selected surface has unsupported control dimensions or missing attributes.");
        for (int i = 0; i < surface.Vertices.Length; i++)
            if (!Finite(surface.Vertices[i]) || !Finite(surface.TextureCoordinates[i]) ||
                !Finite(surface.LightmapCoordinates[i]) || !Finite(surface.Colors[i]))
                throw new ArgumentException("The selected surface contains a nonfinite control value.");
    }

    private static void EnsureArea(MapTerrain part)
    {
        MapTerrain sampled = part.GetSurface();
        if (!sampled.GetTriangles().Any(triangle => Vector3.Cross(
                sampled.Vertices[triangle.B] - sampled.Vertices[triangle.A],
                sampled.Vertices[triangle.C] - sampled.Vertices[triangle.A]).LengthSquared() > 1e-8f))
            throw new ArgumentException("The split would create a surface with no area.");
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
                                                  float.IsFinite(value.Z) && float.IsFinite(value.W);
}
