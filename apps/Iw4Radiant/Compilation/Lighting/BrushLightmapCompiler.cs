using System.Buffers;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Codecs.GfxMap;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushLightmapCompiler
{
    // One secondary luxel covers four map units; primary sun visibility has twice the resolution.
    private const float LuxelSize = 4;
    private const int Border = 2;

    internal static (IReadOnlyList<GfxLightmapArray> Lightmaps, Vector2[][] FaceUvs, byte[] FaceLightmapIndices)
        BakeLightmaps(BrushLightingScene scene)
    {
        var lightmaps = new List<GfxLightmapArray>();
        var faceUvs = new Vector2[scene.Polygons.Count][];
        var faceIndices = new byte[scene.Polygons.Count];
        byte[] primary = new byte[GfxLightmapCodec.PrimaryWidth * GfxLightmapCodec.PrimaryHeight];
        byte[] secondary = new byte[GfxLightmapCodec.SecondaryWidth * GfxLightmapCodec.SecondaryHeight * 4];
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = scene.CancellationToken,
            // Leave CPU capacity for the editor and other applications during a bake.
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 2)
        };
        using var traversal = scene.CreateGpuTraversal();
        int cursorX = 0, cursorY = 0, rowHeight = 0;
        for (int faceIndex = 0; faceIndex < scene.Polygons.Count; faceIndex++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            MapRenderSurface polygon = scene.Polygons[faceIndex];
            faceUvs[faceIndex] = new Vector2[polygon.Vertices.Length];
            if (scene.IsSky(faceIndex))
            {
                faceIndices[faceIndex] = 31;
                continue;
            }
            Vector3 normal = polygon.Normal, anchor = polygon.Vertices[0];
            Vector3 uAxis = Vector3.Normalize(polygon.Vertices[1] - anchor);
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));
            Vector2[] coordinates = polygon.Vertices.Select(point => new Vector2(
                Vector3.Dot(point - anchor, uAxis), Vector3.Dot(point - anchor, vAxis))).ToArray();
            Vector2 minimum = coordinates.Aggregate(Vector2.Min), maximum = coordinates.Aggregate(Vector2.Max);
            int width = checked((int)MathF.Ceiling((maximum.X - minimum.X) / LuxelSize) + 1 + Border * 2);
            int height = checked((int)MathF.Ceiling((maximum.Y - minimum.Y) / LuxelSize) + 1 + Border * 2);
            if (width > GfxLightmapCodec.SecondaryWidth || height > GfxLightmapCodec.SecondaryPlaneHeight)
                throw new NotSupportedException($"Surface {faceIndex} exceeds one baked lightmap tile. Split the surface into smaller brushes or mesh cells.");
            if (cursorX + width > GfxLightmapCodec.SecondaryWidth)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }
            if (cursorY + height > GfxLightmapCodec.SecondaryPlaneHeight)
            {
                Flush();
                primary = new byte[primary.Length];
                secondary = new byte[secondary.Length];
                cursorX = cursorY = rowHeight = 0;
            }
            faceIndices[faceIndex] = checked((byte)lightmaps.Count);
            for (int vertex = 0; vertex < coordinates.Length; vertex++)
                faceUvs[faceIndex][vertex] = new Vector2(
                    (cursorX + Border + (coordinates[vertex].X - minimum.X) / LuxelSize + 0.5f) / GfxLightmapCodec.SecondaryWidth,
                    (cursorY + Border + (coordinates[vertex].Y - minimum.Y) / LuxelSize + 0.5f) / GfxLightmapCodec.SecondaryPlaneHeight);

            if (traversal is null)
                Parallel.For(0, height, parallelOptions, y => BakeRow(y, null, null, 0));
            else
            {
                const int bandHeight = 8;
                int capacity = width * Math.Min(bandHeight, height) * 4;
                int sunCapacity = capacity * 4;
                var points = ArrayPool<Vector3>.Shared.Rent(capacity);
                var normals = ArrayPool<Vector3>.Shared.Rent(capacity);
                var irradiances = ArrayPool<Vector3>.Shared.Rent(capacity);
                var sunPoints = ArrayPool<Vector3>.Shared.Rent(sunCapacity);
                var sunVisibility = ArrayPool<float>.Shared.Rent(sunCapacity);
                try
                {
                    for (int firstRow = 0; firstRow < height; firstRow += bandHeight)
                    {
                        int lastRow = Math.Min(firstRow + bandHeight, height);
                        Parallel.For(firstRow, lastRow, parallelOptions, y =>
                        {
                            for (int x = 0; x < width; x++)
                            for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                            {
                                int index = ((y - firstRow) * width + x) * 4 + sy * 2 + sx;
                                Vector3 point = Position(x + (sx - 0.5f) * 0.5f, y + (sy - 0.5f) * 0.5f);
                                points[index] = point;
                                normals[index] = polygon.Sample(point).Normal;
                            }
                            for (int x = 0; x < width; x++)
                            for (int py = 0; py < 2; py++)
                            for (int px = 0; px < 2; px++)
                            for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                            {
                                int index = (((y - firstRow) * width + x) * 4 + py * 2 + px) * 4 + sy * 2 + sx;
                                sunPoints[index] = Position(x + (px - 0.5f) * 0.5f + (sx - 0.5f) * 0.25f,
                                    y + (py - 0.5f) * 0.5f + (sy - 0.5f) * 0.25f);
                            }
                        });
                        int luxelCount = (lastRow - firstRow) * width;
                        scene.BakeDiffuseSamples(points, normals, irradiances, luxelCount * 4,
                            traversal, parallelOptions);
                        scene.BakeSunSamples(sunPoints, normal, sunVisibility, luxelCount * 16,
                            traversal, parallelOptions);
                        Parallel.For(firstRow, lastRow, parallelOptions,
                            y => BakeRow(y, irradiances, sunVisibility, firstRow));
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(sunVisibility);
                    ArrayPool<Vector3>.Shared.Return(sunPoints);
                    ArrayPool<Vector3>.Shared.Return(irradiances);
                    ArrayPool<Vector3>.Shared.Return(normals);
                    ArrayPool<Vector3>.Shared.Return(points);
                }
            }
            cursorX += width;
            rowHeight = Math.Max(rowHeight, height);

            void BakeRow(int y, Vector3[]? baked, float[]? bakedSun, int firstRow)
            {
                scene.CancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    Vector3 irradiance = Vector3.Zero;
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        if (baked is not null)
                            irradiance += baked[((y - firstRow) * width + x) * 4 + sy * 2 + sx];
                        else
                        {
                            Vector3 point = Position(x + (sx - 0.5f) * 0.5f, y + (sy - 0.5f) * 0.5f);
                            irradiance += scene.DiffuseIrradiance(point, polygon.Sample(point).Normal);
                        }
                    }
                    // Filter irradiance in linear light, before the native square-root encoding.
                    irradiance *= 0.25f;
                    int upper = ((cursorY + y) * GfxLightmapCodec.SecondaryWidth + cursorX + x) * 4;
                    int lower = upper + GfxLightmapCodec.SecondaryWidth * GfxLightmapCodec.SecondaryPlaneHeight * 4;
                    // Native lm_* reconstructs upper.rgb * normal.z plus lower.rgb *
                    // directional weight, then squares the result. Sun is added separately
                    // using the primary visibility image. This bake stores diffuse sky
                    // and local lights in the upper term; the directional term is empty.
                    secondary[upper] = EncodeIrradiance(irradiance.X);
                    secondary[upper + 1] = EncodeIrradiance(irradiance.Y);
                    secondary[upper + 2] = EncodeIrradiance(irradiance.Z);
                    secondary[upper + 3] = secondary[lower + 3] = 128;
                    for (int py = 0; py < 2; py++)
                    for (int px = 0; px < 2; px++)
                    {
                        float visibility = 0;
                        for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            if (bakedSun is not null)
                                visibility += bakedSun[(((y - firstRow) * width + x) * 4 + py * 2 + px) * 4 + sy * 2 + sx];
                            else
                            {
                                Vector3 sample = Position(x + (px - 0.5f) * 0.5f + (sx - 0.5f) * 0.25f,
                                    y + (py - 0.5f) * 0.5f + (sy - 0.5f) * 0.25f);
                                visibility += scene.SunVisibility(sample, normal);
                            }
                        }
                        int offset = ((cursorY + y) * 2 + py) * GfxLightmapCodec.PrimaryWidth + (cursorX + x) * 2 + px;
                        primary[offset] = (byte)Math.Clamp(MathF.Round(visibility * 0.25f * 255), 0, 255);
                    }
                }
            }

            Vector3 Position(float x, float y)
            {
                Vector3 point = anchor + uAxis * (minimum.X + (x - Border) * LuxelSize) +
                    vAxis * (minimum.Y + (y - Border) * LuxelSize);
                bool inside = true;
                Vector3 closest = point;
                float closestDistance = float.PositiveInfinity;
                for (int edge = 0; edge < polygon.Vertices.Length; edge++)
                {
                    Vector3 a = polygon.Vertices[edge], b = polygon.Vertices[(edge + 1) % polygon.Vertices.Length];
                    Vector3 segment = b - a;
                    if (Vector3.Dot(Vector3.Cross(segment, point - a), normal) < 0) inside = false;
                    Vector3 candidate = a + segment * Math.Clamp(Vector3.Dot(point - a, segment) / segment.LengthSquared(), 0, 1);
                    float distance = Vector3.DistanceSquared(candidate, point);
                    if (distance < closestDistance) { closestDistance = distance; closest = candidate; }
                }
                return inside ? point : closest;
            }
        }
        Flush();
        return (lightmaps, faceUvs, faceIndices);

        void Flush()
        {
            if (lightmaps.Count >= 31)
                throw new NotSupportedException("The baked world exceeds the 31-lightmap v22 limit.");
            lightmaps.Add(GfxLightmapCodec.Create(lightmaps.Count, primary, secondary));
        }
    }

    private static byte EncodeIrradiance(float value)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new NotSupportedException("Calculated diffuse lighting exceeds the native lightmap's encoded range.");
        return (byte)MathF.Round(255 * MathF.Sqrt(Math.Clamp(value, 0, 1)));
    }
}
