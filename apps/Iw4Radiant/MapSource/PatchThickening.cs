using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class PatchThickening
{
    internal static (MapTerrain[] Top, MapTerrain[] Shell) Create(MapTerrain source, float thickness)
    {
        if (!float.IsFinite(thickness) || thickness <= 0 || thickness > 4096)
            throw new ArgumentException("Choose a thickness between 0 and 4096 map units.");
        if (source.IsCurve && (source.Width > 15 || source.Height > 15))
            throw new ArgumentException("Curves can have at most 15 controls in each direction.");
        if (!source.IsCurve && (source.Width > 16 || source.Height > 16))
            throw new ArgumentException("Choose a terrain patch with at most 16 vertices in each direction.");

        MapTerrain surface = source.IsCurve ? PatchGeometry.Evaluate(source) : source;
        Vector3 behind = Behind(surface, thickness);
        MapTerrain underside = Underside(surface, behind);
        var shell = new List<MapTerrain>();
        shell.AddRange(TerrainConversion.ToNativeTiles(underside));
        foreach (int[] boundary in Boundaries(surface))
            shell.AddRange(TerrainConversion.ToNativeTiles(Wall(surface, boundary, behind, thickness)));
        MapTerrain[] top = source.IsCurve ? TerrainConversion.ToNativeTiles(surface) : [source];
        return (top, shell.ToArray());
    }

    private static Vector3 Behind(MapTerrain surface, float thickness)
    {
        long count = (long)surface.Width * surface.Height;
        if (surface.Width < 2 || surface.Height < 2 || count != surface.Vertices.Length ||
            count != surface.TextureCoordinates.Length || count != surface.LightmapCoordinates.Length ||
            count != surface.Colors.Length || count != surface.EdgeFlags.Length)
            throw new ArgumentException("The selected patch has an incomplete vertex grid.");
        Vector3 summedNormal = Vector3.Zero;
        foreach (var (a, b, c) in surface.GetTriangles())
        {
            Vector3 normal = Vector3.Cross(surface.Vertices[b] - surface.Vertices[a],
                surface.Vertices[c] - surface.Vertices[a]);
            if (!Finite(normal))
                throw new ArgumentException("The selected patch has nonfinite or oversized coordinates.");
            summedNormal += normal;
        }
        if (!Finite(summedNormal) || summedNormal.LengthSquared() <= 1e-8f)
            throw new ArgumentException("The selected patch has no consistent front side to thicken behind.");
        int axis = MathF.Abs(summedNormal.X) > MathF.Abs(summedNormal.Y)
            ? (MathF.Abs(summedNormal.X) > MathF.Abs(summedNormal.Z) ? 0 : 2)
            : (MathF.Abs(summedNormal.Y) > MathF.Abs(summedNormal.Z) ? 1 : 2);
        float sign = MathF.Sign(axis switch { 0 => summedNormal.X, 1 => summedNormal.Y, _ => summedNormal.Z });
        Vector2[] projected = surface.Vertices.Select(point => axis switch
        {
            0 => new Vector2(point.Y, point.Z),
            1 => new Vector2(point.Z, point.X),
            _ => new Vector2(point.X, point.Y)
        }).ToArray();
        foreach (var (a, b, c) in surface.GetTriangles())
            if (sign * Orient(projected[a], projected[b], projected[c]) <= 1e-8)
                throw new ArgumentException("This patch folds or has a zero-area triangle when viewed from its front. Straighten the fold before thickening.");

        int[] perimeter = Perimeter(surface);
        for (int first = 0; first < perimeter.Length; first++)
        for (int second = first + 2; second < perimeter.Length; second++)
        {
            if (first == 0 && second == perimeter.Length - 1) continue;
            if (Intersects(projected[perimeter[first]], projected[perimeter[(first + 1) % perimeter.Length]],
                    projected[perimeter[second]], projected[perimeter[(second + 1) % perimeter.Length]]))
                throw new ArgumentException("This patch's boundary crosses or touches itself. Separate the overlapping edges before thickening.");
        }
        Vector3 behind = axis switch
        {
            0 => new Vector3(-sign * thickness, 0, 0),
            1 => new Vector3(0, -sign * thickness, 0),
            _ => new Vector3(0, 0, -sign * thickness)
        };
        if (surface.Vertices.Any(point => !Finite(point + behind)))
            throw new ArgumentException("The chosen thickness would move a vertex outside finite map coordinates.");
        return behind;
    }

    private static MapTerrain Underside(MapTerrain surface, Vector3 behind)
    {
        MapTerrain underside = surface.Clone();
        underside.Smoothing = "smoothing_hard";
        for (int x = 0; x < surface.Width; x++)
        for (int y = 0; y < surface.Height; y++)
        {
            int destination = x * surface.Height + y;
            int original = (surface.Width - 1 - x) * surface.Height + y;
            underside.Vertices[destination] = surface.Vertices[original] + behind;
            underside.TextureCoordinates[destination] = surface.TextureCoordinates[original];
            underside.LightmapCoordinates[destination] = surface.LightmapCoordinates[original];
            underside.Colors[destination] = surface.Colors[original];
            if (x < surface.Width - 1 && y < surface.Height - 1)
            {
                int originalCell = (surface.Width - 2 - x) * surface.Height + y;
                underside.EdgeFlags[destination] = surface.EdgeFlags[originalCell] == 1 ? 0 : 1;
            }
        }
        return underside;
    }

    private static MapTerrain Wall(MapTerrain surface, int[] boundary, Vector3 behind, float thickness)
    {
        int count = boundary.Length * 2;
        var wall = new MapTerrain
        {
            Material = surface.Material, Lightmap = surface.Lightmap, Smoothing = "smoothing_hard",
            Width = boundary.Length, Height = 2, LightmapSize = surface.LightmapSize, Subdivision = surface.Subdivision,
            Vertices = new Vector3[count], TextureCoordinates = new Vector2[count],
            LightmapCoordinates = new Vector2[count], Colors = new Vector4[count], EdgeFlags = new int[count]
        };
        wall.Directives.AddRange(surface.Directives);
        float length = 0;
        for (int x = 0; x < boundary.Length; x++)
        {
            int original = boundary[x], top = x * 2;
            if (x > 0)
                length += Vector3.Distance(surface.Vertices[boundary[x - 1]], surface.Vertices[original]);
            if (!float.IsFinite(length))
                throw new ArgumentException("This patch boundary is too large to texture safely.");
            wall.Vertices[top] = surface.Vertices[original];
            wall.Vertices[top + 1] = surface.Vertices[original] + behind;
            wall.TextureCoordinates[top] = new Vector2(length / 128, 0);
            wall.TextureCoordinates[top + 1] = new Vector2(length / 128, thickness / 128);
            wall.LightmapCoordinates[top] = new Vector2((float)x / (boundary.Length - 1), 0);
            wall.LightmapCoordinates[top + 1] = new Vector2((float)x / (boundary.Length - 1), 1);
            wall.Colors[top] = wall.Colors[top + 1] = surface.Colors[original];
            wall.EdgeFlags[top] = wall.EdgeFlags[top + 1] = 1;
        }
        return wall;
    }

    private static int[][] Boundaries(MapTerrain surface) =>
    [
        Enumerable.Range(0, surface.Width).Reverse().Select(x => x * surface.Height).ToArray(),
        Enumerable.Range(0, surface.Height).Select(y => y).ToArray(),
        Enumerable.Range(0, surface.Width).Select(x => x * surface.Height + surface.Height - 1).ToArray(),
        Enumerable.Range(0, surface.Height).Reverse().Select(y => (surface.Width - 1) * surface.Height + y).ToArray()
    ];

    private static int[] Perimeter(MapTerrain surface) =>
    [
        ..Enumerable.Range(0, surface.Width).Select(x => x * surface.Height),
        ..Enumerable.Range(1, surface.Height - 1).Select(y => (surface.Width - 1) * surface.Height + y),
        ..Enumerable.Range(0, surface.Width - 1).Reverse().Select(x => x * surface.Height + surface.Height - 1),
        ..Enumerable.Range(1, surface.Height - 2).Reverse().Select(y => y)
    ];

    private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        double abC = Orient(a, b, c), abD = Orient(a, b, d);
        double cdA = Orient(c, d, a), cdB = Orient(c, d, b);
        if ((abC > 0 && abD < 0 || abC < 0 && abD > 0) &&
            (cdA > 0 && cdB < 0 || cdA < 0 && cdB > 0)) return true;
        return OnSegment(a, b, c, abC) || OnSegment(a, b, d, abD) ||
               OnSegment(c, d, a, cdA) || OnSegment(c, d, b, cdB);
    }

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 point, double orientation) =>
        Math.Abs(orientation) <= 1e-8 &&
        point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X) &&
        point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);

    private static double Orient(Vector2 a, Vector2 b, Vector2 c) =>
        ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
