using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class PatchGeometry
{
    internal static MapTerrain Evaluate(MapTerrain patch)
    {
        Validate(patch);
        // Display sampling only: saving always writes the original native control grid.
        const int samplesPerSpan = 8;
        int columns = (patch.Width - 1) / 2, rows = (patch.Height - 1) / 2;
        MapTerrain surface = Allocate(patch, columns * samplesPerSpan + 1, rows * samplesPerSpan + 1);
        surface.IsCurve = false;
        for (int x = 0; x < surface.Width; x++)
        for (int y = 0; y < surface.Height; y++)
        {
            int column = Math.Min(x / samplesPerSpan, columns - 1);
            int row = Math.Min(y / samplesPerSpan, rows - 1);
            Vector3 u = Basis((float)(x - column * samplesPerSpan) / samplesPerSpan);
            Vector3 v = Basis((float)(y - row * samplesPerSpan) / samplesPerSpan);
            int target = x * surface.Height + y;
            for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                Accumulate(surface, target, patch, (column * 2 + a) * patch.Height + row * 2 + b, u[a] * v[b]);
            surface.Colors[target] = Vector4.Clamp(surface.Colors[target], Vector4.Zero, Vector4.One);
            surface.EdgeFlags[target] = 1;
        }
        return surface;
    }

    internal static MapTerrain Refine(MapTerrain patch, bool columns)
    {
        Validate(patch);
        int length = columns ? patch.Width : patch.Height;
        int refinedLength = (length - 1) * 2 + 1;
        if (refinedLength > 15)
            throw new ArgumentException("Refining would exceed Radiant's 15 control points per curve direction. Refine the other direction instead.");
        MapTerrain refined = Allocate(patch, columns ? refinedLength : patch.Width, columns ? patch.Height : refinedLength);
        for (int transverse = 0; transverse < (columns ? patch.Height : patch.Width); transverse++)
        for (int span = 0; span < (length - 1) / 2; span++)
        for (int point = 0; point < 5; point++)
        {
            // Exact de Casteljau split: the visible curve and all interpolated attributes stay fixed.
            Vector3 weights = point switch
            {
                0 => new(1, 0, 0), 1 => new(0.5f, 0.5f, 0),
                2 => new(0.25f, 0.5f, 0.25f), 3 => new(0, 0.5f, 0.5f), _ => new(0, 0, 1)
            };
            int destination = columns ? (span * 4 + point) * refined.Height + transverse :
                transverse * refined.Height + span * 4 + point;
            // Shared endpoints are assigned once, rather than accumulating both adjacent spans.
            if (span > 0 && point == 0) continue;
            for (int sourcePoint = 0; sourcePoint < 3; sourcePoint++)
            {
                int source = columns ? (span * 2 + sourcePoint) * patch.Height + transverse :
                    transverse * patch.Height + span * 2 + sourcePoint;
                Accumulate(refined, destination, patch, source, weights[sourcePoint]);
            }
            refined.Colors[destination] = Vector4.Clamp(refined.Colors[destination], Vector4.Zero, Vector4.One);
        }
        return refined;
    }

    internal static MapTerrain Invert(MapTerrain patch)
    {
        Validate(patch);
        MapTerrain inverted = Allocate(patch, patch.Width, patch.Height);
        for (int x = 0; x < patch.Width; x++)
        for (int y = 0; y < patch.Height; y++)
        {
            int target = x * patch.Height + y, source = (patch.Width - 1 - x) * patch.Height + y;
            Accumulate(inverted, target, patch, source, 1);
            inverted.EdgeFlags[target] = patch.EdgeFlags[source];
        }
        return inverted;
    }

    internal static MapTerrain[] CapEnds(MapTerrain patch)
    {
        Validate(patch);
        var caps = new MapTerrain[2];
        for (int end = 0; end < 2; end++)
        {
            int row = end == 0 ? 0 : patch.Height - 1;
            int[] anchors = Enumerable.Range(0, (patch.Width + 1) / 2).Select(x => x * 2 * patch.Height + row).ToArray();
            bool closed = Vector3.DistanceSquared(patch.Vertices[anchors[0]], patch.Vertices[anchors[^1]]) < 0.0001f;
            if (closed)
                anchors = anchors[..^1];
            if (anchors.Length < 2)
                throw new ArgumentException("The selected patch has no open ends to cap.");
            Vector3 center = Vector3.Zero;
            Vector2 texture = Vector2.Zero, lightmap = Vector2.Zero;
            Vector4 color = Vector4.Zero;
            // Closed ends use each anchor once; open arcs include their middle handles.
            int[] centerIndices = closed ? anchors : Enumerable.Range(0, patch.Width).Select(x => x * patch.Height + row).ToArray();
            foreach (int source in centerIndices)
            {
                center += patch.Vertices[source] / centerIndices.Length;
                texture += patch.TextureCoordinates[source] / centerIndices.Length;
                lightmap += patch.LightmapCoordinates[source] / centerIndices.Length;
                color += patch.Colors[source] / centerIndices.Length;
            }
            if (!Enumerable.Range(0, patch.Width - 1).Any(x =>
                    Vector3.Cross(patch.Vertices[x * patch.Height + row] - center,
                        patch.Vertices[(x + 1) * patch.Height + row] - center).LengthSquared() > 0.0001f))
                throw new ArgumentException("The selected patch's end is a straight line. Bevels, end caps, and cylinders can be capped.");
            MapTerrain cap = Allocate(patch, patch.Width, 3);
            for (int x = 0; x < patch.Width; x++)
            for (int y = 0; y < 3; y++)
            {
                int destination = x * 3 + y, source = x * patch.Height + row;
                float t = y * 0.5f;
                cap.Vertices[destination] = Vector3.Lerp(patch.Vertices[source], center, t);
                cap.TextureCoordinates[destination] = Vector2.Lerp(patch.TextureCoordinates[source], texture, t);
                cap.LightmapCoordinates[destination] = Vector2.Lerp(patch.LightmapCoordinates[source], lightmap, t);
                cap.Colors[destination] = Vector4.Clamp(Vector4.Lerp(patch.Colors[source], color, t), Vector4.Zero, Vector4.One);
            }
            caps[end] = end == 0 ? Invert(cap) : cap;
        }
        return caps;
    }

    private static Vector3 Basis(float t) => new((1 - t) * (1 - t), 2 * t * (1 - t), t * t);

    private static MapTerrain Allocate(MapTerrain source, int width, int height)
    {
        int count = checked(width * height);
        var patch = new MapTerrain
        {
            IsCurve = source.IsCurve, Material = source.Material, Lightmap = source.Lightmap, Smoothing = source.Smoothing,
            Width = width, Height = height, LightmapSize = source.LightmapSize, Subdivision = source.Subdivision,
            Vertices = new Vector3[count], TextureCoordinates = new Vector2[count], LightmapCoordinates = new Vector2[count],
            Colors = new Vector4[count], EdgeFlags = new int[count]
        };
        patch.Directives.AddRange(source.Directives);
        return patch;
    }

    private static void Accumulate(MapTerrain target, int destination, MapTerrain source, int index, float weight)
    {
        target.Vertices[destination] += source.Vertices[index] * weight;
        target.TextureCoordinates[destination] += source.TextureCoordinates[index] * weight;
        target.LightmapCoordinates[destination] += source.LightmapCoordinates[index] * weight;
        target.Colors[destination] += source.Colors[index] * weight;
    }

    private static void Validate(MapTerrain patch)
    {
        if (!patch.IsCurve || patch.Width < 3 || patch.Height < 3 || patch.Width % 2 == 0 || patch.Height % 2 == 0 ||
            (long)patch.Width * patch.Height != patch.Vertices.Length || patch.TextureCoordinates.Length != patch.Vertices.Length ||
            patch.LightmapCoordinates.Length != patch.Vertices.Length || patch.Colors.Length != patch.Vertices.Length ||
            patch.EdgeFlags.Length != patch.Vertices.Length)
            throw new ArgumentException("A curve needs a complete odd control grid of at least 3 by 3 points.");
    }
}
