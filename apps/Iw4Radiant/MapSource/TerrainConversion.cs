using System.Numerics;

namespace Iw4Radiant.MapSource;

internal static class TerrainConversion
{
    internal static MapTerrain FromFace(MapPolygon polygon, int verticesPerSide)
    {
        if (polygon.Vertices.Length != 4)
            throw new ArgumentException("This face has more or fewer than four corners. Choose a four-sided brush face.");
        if (verticesPerSide is < 2 or > 16)
            throw new ArgumentException("Choose 2 to 16 terrain vertices per side.");
        var mapping = SurfaceProjection.Parse(polygon.Face.Projection).GetMapping(polygon.Face.Normal);
        Vector3[] corners = polygon.Vertices;
        int count = verticesPerSide * verticesPerSide;
        var terrain = new MapTerrain
        {
            Material = polygon.Face.Material, Width = verticesPerSide, Height = verticesPerSide,
            Vertices = new Vector3[count], TextureCoordinates = new Vector2[count],
            LightmapCoordinates = new Vector2[count], Colors = new Vector4[count], EdgeFlags = new int[count]
        };
        for (int x = 0; x < verticesPerSide; x++)
        for (int y = 0; y < verticesPerSide; y++)
        {
            int index = x * verticesPerSide + y;
            float u = (float)x / (verticesPerSide - 1), v = (float)y / (verticesPerSide - 1);
            Vector3 point = Vector3.Lerp(Vector3.Lerp(corners[0], corners[1], u),
                Vector3.Lerp(corners[3], corners[2], u), v);
            terrain.Vertices[index] = point;
            terrain.TextureCoordinates[index] = new Vector2(
                (float)(BrushGeometry.Dot(mapping.U, point) + mapping.Offset.X),
                (float)(BrushGeometry.Dot(mapping.V, point) + mapping.Offset.Y));
            terrain.LightmapCoordinates[index] = new Vector2(u, v);
            terrain.Colors[index] = Vector4.One;
            terrain.EdgeFlags[index] = 1;
        }
        return terrain;
    }

    internal static MapTerrain[] FromCurve(MapTerrain curve, int samplesPerSpan)
    {
        if (!curve.IsCurve || curve.Width > 15 || curve.Height > 15)
            throw new ArgumentException("Choose a curved patch with at most 15 controls per direction.");
        if (samplesPerSpan is not (4 or 8 or 16))
            throw new ArgumentException("Choose light, balanced, or fine curve detail.");
        foreach (Vector4 color in curve.Colors)
            if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) ||
                !float.IsFinite(color.W) || color.X < 0 || color.X > 1 || color.Y < 0 || color.Y > 1 ||
                color.Z < 0 || color.Z > 1 || color.W < 0 || color.W > 1)
                throw new ArgumentException("Curve vertex colors must be within 0–1 before conversion.");
        MapTerrain sampled = PatchGeometry.Evaluate(curve, samplesPerSpan);
        return ToNativeTiles(sampled);
    }

    internal static MapTerrain[] ToNativeTiles(MapTerrain sampled)
    {
        List<MapTerrain> columns = SplitToNativeSize(sampled, columns: true);
        return columns.SelectMany(column => SplitToNativeSize(column, columns: false)).ToArray();
    }

    private static List<MapTerrain> SplitToNativeSize(MapTerrain sampled, bool columns)
    {
        var result = new List<MapTerrain>();
        int length = columns ? sampled.Width : sampled.Height;
        while (length > 16)
        {
            // Spread seams evenly so a final one-cell sliver is not left behind.
            int pieces = (length - 1 + 14) / 15;
            int seam = (length - 1 + pieces - 1) / pieces;
            (MapTerrain first, MapTerrain second) = TerrainSplit.AtGridLine(sampled, columns, seam);
            result.Add(first);
            sampled = second;
            length = columns ? sampled.Width : sampled.Height;
        }
        result.Add(sampled);
        return result;
    }
}
