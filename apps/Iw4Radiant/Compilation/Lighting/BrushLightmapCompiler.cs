using System.Buffers;
using System.Numerics;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Codecs.GfxMap;
using IW4.Render.WebGpu;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushLightmapCompiler
{
    // Brushes do not author a usable bake density; retain the existing four-unit baseline when capacity permits.
    private const float BrushLuxelSize = 4;
    private const int Border = 2;
    // v22 has 31 baked arrays; index 31 is reserved for unlightmapped sky/water faces.
    private const int LightmapLimit = 31;

    // Narrow faces need more rows to fill a ray batch. Keep the existing eight-row
    // minimum for wide faces and bound scratch space to about one sun batch (16 rays/luxel).
    // Diffuse work retains its own ray-capacity splitting and overlaps CPU shading.
    private static int GpuBandHeight(int width, int height)
        => Math.Min(height, Math.Max(8, WebGpuRayTraversal.MaximumRayCount / 16 / width));

    internal static (IReadOnlyList<GfxLightmapArray> Lightmaps, Vector2[][] FaceUvs, byte[] FaceLightmapIndices)
        BakeLightmaps(BrushLightingScene scene, IProgress<string>? progress = null)
    {
        var lightmaps = new List<GfxLightmapArray>();
        var rawPages = new List<(byte[] Primary, byte[] Secondary)>();
        var faceLayouts = new List<FaceLayout>();
        var faceUvs = new Vector2[scene.Polygons.Count][];
        var faceIndices = new byte[scene.Polygons.Count];
        float[] luxelSizes = ChooseLuxelSizes(scene, out long totalLuxels);
        progress?.Report($"Direct lighting: 0/{totalLuxels} luxels completed.");
        long directLuxels = 0;
        int directBucket = 0;
        byte[] primary = new byte[GfxLightmapCodec.PrimaryWidth * GfxLightmapCodec.PrimaryHeight];
        byte[] secondary = new byte[GfxLightmapCodec.SecondaryWidth * GfxLightmapCodec.SecondaryHeight * 4];
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = scene.CancellationToken,
            // Leave CPU capacity for the editor and other applications during a bake.
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 2)
        };
        using var traversal = scene.CreateGpuTraversal();
        int reservedRows = scene.HasPrimaryLocalLights ? 1 : 0;
        int cursorX = 0, cursorY = reservedRows, rowHeight = 0;
        for (int faceIndex = 0; faceIndex < scene.Polygons.Count; faceIndex++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            MapRenderSurface polygon = scene.Polygons[faceIndex];
            byte primaryLight = scene.PrimaryLightForFace(faceIndex);
            byte sunIndex = polygon.SunPrimaryLightIndex;
            faceUvs[faceIndex] = new Vector2[polygon.Vertices.Length];
            // Water shaders use native spectra, lights and reflection probes, not lightmaps.
            // Subdivision must not turn every wave cell into an unused CPU lighting bake.
            if (scene.IsSky(faceIndex) || scene.IsWater(faceIndex))
            {
                faceIndices[faceIndex] = LightmapLimit;
                continue;
            }
            Vector3 normal = polygon.Normal, anchor = polygon.Vertices[0];
            Vector3 uAxis = Vector3.Normalize(polygon.Vertices[1] - anchor);
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(normal, uAxis));
            var (coordinates, minimum, maximum) = Project(polygon);
            float luxelSize = luxelSizes[faceIndex];
            int width = TileLength(maximum.X - minimum.X, luxelSize);
            int height = TileLength(maximum.Y - minimum.Y, luxelSize);
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
                cursorX = rowHeight = 0;
                cursorY = reservedRows;
            }
            faceIndices[faceIndex] = checked((byte)rawPages.Count);
            var layout = new FaceLayout(rawPages.Count, cursorX, cursorY, width, height,
                polygon, anchor, uAxis, vAxis, minimum, luxelSize);
            faceLayouts.Add(layout);
            // The cache starts at the padded tile origin, using this compiler's border width.
            bool recordDirect = scene.BeginDirectFace(faceIndex, anchor, uAxis, vAxis,
                minimum - new Vector2(Border * luxelSize),
                luxelSize, width, height);
            for (int vertex = 0; vertex < coordinates.Length; vertex++)
                faceUvs[faceIndex][vertex] = new Vector2(
                    (cursorX + Border + (coordinates[vertex].X - minimum.X) / luxelSize + 0.5f) / GfxLightmapCodec.SecondaryWidth,
                    (cursorY + Border + (coordinates[vertex].Y - minimum.Y) / luxelSize + 0.5f) / GfxLightmapCodec.SecondaryPlaneHeight);

            if (traversal is null)
            {
                Parallel.For(0, height, parallelOptions, y => BakeRow(y, null, null, 0));
                directLuxels += (long)width * height;
                ReportProgress(progress, "Direct lighting", "luxels", directLuxels, totalLuxels, ref directBucket);
            }
            else
            {
                int bandHeight = GpuBandHeight(width, height);
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
                        scene.BakeDiffuseSamples(points, normals, irradiances, luxelCount * 4, sunIndex,
                            traversal, parallelOptions);
                        scene.BakeSunSamples(sunPoints, normal, sunVisibility, luxelCount * 16, sunIndex,
                            traversal, parallelOptions);
                        Parallel.For(firstRow, lastRow, parallelOptions,
                            y => BakeRow(y, irradiances, sunVisibility, firstRow));
                        directLuxels += (long)(lastRow - firstRow) * width;
                        ReportProgress(progress, "Direct lighting", "luxels", directLuxels, totalLuxels, ref directBucket);
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
                    Vector3 irradiance = Vector3.Zero, directForBounce = Vector3.Zero;
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        Vector3 point = Position(x + (sx - 0.5f) * 0.5f, y + (sy - 0.5f) * 0.5f);
                        Vector3 sampleNormal = polygon.Sample(point).Normal;
                        Vector3 direct = baked is not null
                            ? baked[((y - firstRow) * width + x) * 4 + sy * 2 + sx]
                            : scene.DiffuseIrradiance(point, sampleNormal, sunIndex);
                        directForBounce += direct;
                        irradiance += scene.SecondaryIrradiance(point, sampleNormal, direct, primaryLight, sunIndex);
                    }
                    // Filter irradiance in linear light, before the native square-root encoding.
                    irradiance *= 0.25f;
                    directForBounce *= 0.25f;
                    int upper = ((cursorY + y) * GfxLightmapCodec.SecondaryWidth + cursorX + x) * 4;
                    int lower = upper + GfxLightmapCodec.SecondaryWidth * GfxLightmapCodec.SecondaryPlaneHeight * 4;
                    // Native lm_* reconstructs upper.rgb * normal.z plus lower.rgb *
                    // directional weight, then squares the result. Only the assigned
                    // primary's direct term is separate; other direct light and bounce
                    // remain in secondary RGB. The directional term is empty.
                    secondary[upper] = EncodeIrradiance(irradiance.X);
                    secondary[upper + 1] = EncodeIrradiance(irradiance.Y);
                    secondary[upper + 2] = EncodeIrradiance(irradiance.Z);
                    secondary[upper + 3] = secondary[lower + 3] = 128;
                    float totalSunVisibility = 0;
                    for (int py = 0; py < 2; py++)
                    for (int px = 0; px < 2; px++)
                    {
                        float visibility = 0, sunVisibility = 0;
                        for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            Vector3 sample = Position(x + (px - 0.5f) * 0.5f + (sx - 0.5f) * 0.25f,
                                y + (py - 0.5f) * 0.5f + (sy - 0.5f) * 0.25f);
                            float sun = bakedSun is not null
                                ? bakedSun[(((y - firstRow) * width + x) * 4 + py * 2 + px) * 4 + sy * 2 + sx]
                                : scene.SunVisibility(sample, normal, sunIndex);
                            sunVisibility += sun;
                            visibility += primaryLight == sunIndex ? sun : scene.PrimaryVisibility(sample, normal, primaryLight);
                        }
                        totalSunVisibility += sunVisibility;
                        int offset = ((cursorY + y) * 2 + py) * GfxLightmapCodec.PrimaryWidth + (cursorX + x) * 2 + px;
                        primary[offset] = (byte)Math.Clamp(MathF.Round(visibility * 0.25f * 255), 0, 255);
                    }
                    if (recordDirect && BrushLightingScene.IsDirectCacheLuxel(x, y, width, height))
                        scene.RecordDirectLuxel(faceIndex, x, y, directForBounce, totalSunVisibility / 16);
                }
            }

            Vector3 Position(float x, float y) => layout.Position(x, y);
        }
        progress?.Report($"Direct lighting complete: {directLuxels}/{totalLuxels} luxels.");
        Flush();
        scene.FreezeDirectFaces();
        progress?.Report($"Bounced lighting: 0/{totalLuxels} luxels completed.");
        long bouncedLuxels = 0;
        int bounceBucket = 0;
        foreach (FaceLayout layout in faceLayouts)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            byte[] page = rawPages[layout.Page].Secondary;
            if (traversal is null)
            {
                Parallel.For(0, layout.Height, parallelOptions, y =>
                {
                    for (int x = 0; x < layout.Width; x++)
                    {
                        Vector3 point = layout.Position(x, y);
                        AddIndirect(layout, x, y,
                            scene.IndirectIrradiance(point, layout.Polygon.Sample(point).Normal,
                                layout.Polygon.SunPrimaryLightIndex), page);
                    }
                });
                bouncedLuxels += (long)layout.Width * layout.Height;
                ReportProgress(progress, "Bounced lighting", "luxels", bouncedLuxels, totalLuxels, ref bounceBucket);
            }
            else
            {
                int bandHeight = GpuBandHeight(layout.Width, layout.Height);
                int capacity = layout.Width * Math.Min(bandHeight, layout.Height);
                var points = ArrayPool<Vector3>.Shared.Rent(capacity);
                var normals = ArrayPool<Vector3>.Shared.Rent(capacity);
                var irradiances = ArrayPool<Vector3>.Shared.Rent(capacity);
                try
                {
                    for (int firstRow = 0; firstRow < layout.Height; firstRow += bandHeight)
                    {
                        int lastRow = Math.Min(firstRow + bandHeight, layout.Height);
                        Parallel.For(firstRow, lastRow, parallelOptions, y =>
                        {
                            for (int x = 0; x < layout.Width; x++)
                            {
                                int index = (y - firstRow) * layout.Width + x;
                                Vector3 point = layout.Position(x, y);
                                points[index] = point;
                                normals[index] = layout.Polygon.Sample(point).Normal;
                            }
                        });
                        scene.BakeDiffuseSamples(points, normals, irradiances,
                            (lastRow - firstRow) * layout.Width, layout.Polygon.SunPrimaryLightIndex,
                            traversal, parallelOptions, indirectOnly: true);
                        Parallel.For(firstRow, lastRow, parallelOptions, y =>
                        {
                            for (int x = 0; x < layout.Width; x++)
                                AddIndirect(layout, x, y,
                                    irradiances[(y - firstRow) * layout.Width + x], page);
                        });
                        bouncedLuxels += (long)(lastRow - firstRow) * layout.Width;
                        ReportProgress(progress, "Bounced lighting", "luxels", bouncedLuxels, totalLuxels, ref bounceBucket);
                    }
                }
                finally
                {
                    ArrayPool<Vector3>.Shared.Return(irradiances);
                    ArrayPool<Vector3>.Shared.Return(normals);
                    ArrayPool<Vector3>.Shared.Return(points);
                }
            }
        }
        progress?.Report($"Bounced lighting complete: {bouncedLuxels}/{totalLuxels} luxels.");
        progress?.Report($"Lightmap packing: 0/{rawPages.Count} pages completed.");
        int packingBucket = 0;
        for (int page = 0; page < rawPages.Count; page++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            var (rawPrimary, rawSecondary) = rawPages[page];
            lightmaps.Add(GfxLightmapCodec.Create(page, rawPrimary, rawSecondary));
            rawPages[page] = ([], []);
            ReportProgress(progress, "Lightmap packing", "pages", page + 1, rawPages.Count, ref packingBucket);
        }
        progress?.Report($"Lightmap packing complete: {rawPages.Count}/{rawPages.Count} pages.");
        return (lightmaps, faceUvs, faceIndices);

        void Flush()
        {
            if (rawPages.Count >= LightmapLimit)
                throw new NotSupportedException("The baked world exceeds the 31-lightmap v22 limit.");
            if (scene.HasPrimaryLocalLights) PrimaryLocalLightProfile.WriteLookupRow(secondary);
            rawPages.Add((primary, secondary));
        }
    }

    private static void AddIndirect(FaceLayout layout, int x, int y, Vector3 bounce, byte[] secondary)
    {
        if (bounce == Vector3.Zero) return;
        int upper = ((layout.AtlasY + y) * GfxLightmapCodec.SecondaryWidth + layout.AtlasX + x) * 4;
        // The direct term has already been quantized into native sqrt encoding.
        // Decode its current linear value, add one bounce, then encode once.
        secondary[upper] = EncodeIrradiance(DecodeIrradiance(secondary[upper]) + bounce.X);
        secondary[upper + 1] = EncodeIrradiance(DecodeIrradiance(secondary[upper + 1]) + bounce.Y);
        secondary[upper + 2] = EncodeIrradiance(DecodeIrradiance(secondary[upper + 2]) + bounce.Z);
    }

    private static float DecodeIrradiance(byte value)
    {
        float linear = value / 255f;
        return linear * linear;
    }

    private sealed record FaceLayout(int Page, int AtlasX, int AtlasY, int Width, int Height,
        MapRenderSurface Polygon, Vector3 Anchor, Vector3 UAxis, Vector3 VAxis, Vector2 Minimum, float LuxelSize)
    {
        internal Vector3 Position(float x, float y)
        {
            Vector3 point = Anchor + UAxis * (Minimum.X + (x - Border) * LuxelSize) +
                VAxis * (Minimum.Y + (y - Border) * LuxelSize);
            bool inside = true;
            Vector3 closest = point;
            float closestDistance = float.PositiveInfinity;
            for (int edge = 0; edge < Polygon.Vertices.Length; edge++)
            {
                Vector3 a = Polygon.Vertices[edge], b = Polygon.Vertices[(edge + 1) % Polygon.Vertices.Length];
                Vector3 segment = b - a;
                if (Vector3.Dot(Vector3.Cross(segment, point - a), Polygon.Normal) < 0) inside = false;
                Vector3 candidate = a + segment * Math.Clamp(Vector3.Dot(point - a, segment) / segment.LengthSquared(), 0, 1);
                float distance = Vector3.DistanceSquared(candidate, point);
                if (distance < closestDistance) { closestDistance = distance; closest = candidate; }
            }
            return inside ? point : closest;
        }
    }

    private static float[] ChooseLuxelSizes(BrushLightingScene scene, out long totalLuxels)
    {
        // Keep authored terrain density and the automatic 4-unit brush baseline where possible.
        // Oversized individual faces are locally coarsened by AtlasCount; only then do automatic
        // brush densities coarsen to fit the cap, with authored mesh density scaled as a last resort.
        var spans = new Vector2[scene.Polygons.Count];
        for (int faceIndex = 0; faceIndex < scene.Polygons.Count; faceIndex++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            if (scene.IsSky(faceIndex) || scene.IsWater(faceIndex)) continue;
            var (_, minimum, maximum) = Project(scene.Polygons[faceIndex]);
            spans[faceIndex] = maximum - minimum;
        }

        float brushLower = 1, brushUpper = 1;
        float maximumBrushScale = MaximumScale(scene, spans, authored: false);
        while (AtlasCount(scene, spans, brushUpper, 1, null) > LightmapLimit && brushUpper < maximumBrushScale)
        {
            brushLower = brushUpper;
            brushUpper = MathF.Min(brushUpper * 2, maximumBrushScale);
        }
        float authoredScale = 1;
        if (AtlasCount(scene, spans, brushUpper, authoredScale, null) <= LightmapLimit && brushUpper > 1)
        {
            // Preserve authored mesh density and find the finest automatic brush density that fits.
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float middle = brushLower + (brushUpper - brushLower) * 0.5f;
                if (middle == brushLower || middle == brushUpper) break;
                if (AtlasCount(scene, spans, middle, authoredScale, null) <= LightmapLimit) brushUpper = middle;
                else brushLower = middle;
            }
        }
        else if (AtlasCount(scene, spans, brushUpper, authoredScale, null) > LightmapLimit)
        {
            // Only a map whose authored meshes cannot fit after brushes reach minimum size
            // needs its relative authored densities scaled together.
            float authoredLower = 1, authoredUpper = 1;
            float maximumAuthoredScale = MaximumScale(scene, spans, authored: true);
            while (AtlasCount(scene, spans, brushUpper, authoredUpper, null) > LightmapLimit && authoredUpper < maximumAuthoredScale)
            {
                authoredLower = authoredUpper;
                authoredUpper = MathF.Min(authoredUpper * 2, maximumAuthoredScale);
            }
            if (AtlasCount(scene, spans, brushUpper, authoredUpper, null) > LightmapLimit)
                throw new NotSupportedException("The baked world exceeds the 31-lightmap v22 limit even at minimum lightmap density.");
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float middle = authoredLower + (authoredUpper - authoredLower) * 0.5f;
                if (middle == authoredLower || middle == authoredUpper) break;
                if (AtlasCount(scene, spans, brushUpper, middle, null) <= LightmapLimit) authoredUpper = middle;
                else authoredLower = middle;
            }
            authoredScale = authoredUpper;
        }

        var result = new float[scene.Polygons.Count];
        _ = AtlasCount(scene, spans, brushUpper, authoredScale, result);
        totalLuxels = 0;
        for (int faceIndex = 0; faceIndex < result.Length; faceIndex++)
        {
            if (scene.IsSky(faceIndex) || scene.IsWater(faceIndex)) continue;
            totalLuxels += (long)TileLength(spans[faceIndex].X, result[faceIndex]) *
                TileLength(spans[faceIndex].Y, result[faceIndex]);
        }
        return result;
    }

    private static void ReportProgress(IProgress<string>? progress, string stage, string unit,
        long completed, long total, ref int lastBucket)
    {
        if (progress is null || total == 0) return;
        int bucket = (int)(completed * 20 / total);
        if (bucket <= lastBucket || bucket >= 20) return;
        lastBucket = bucket;
        progress.Report($"{stage}: {completed}/{total} {unit} completed ({completed * 100 / total}%).");
    }

    private static float MaximumScale(BrushLightingScene scene, Vector2[] spans, bool authored)
    {
        float result = 1;
        for (int faceIndex = 0; faceIndex < scene.Polygons.Count; faceIndex++)
        {
            MapRenderSurface surface = scene.Polygons[faceIndex];
            if (scene.IsSky(faceIndex) || scene.IsWater(faceIndex) || surface.LightmapSize.HasValue != authored) continue;
            float baseline = surface.LightmapSize ?? BrushLuxelSize;
            float scale = MathF.Max(spans[faceIndex].X, spans[faceIndex].Y) / baseline;
            result = MathF.Max(result, float.IsFinite(scale) ? scale : float.MaxValue);
        }
        return result < float.MaxValue ? MathF.BitIncrement(result) : result;
    }

    private static int AtlasCount(BrushLightingScene scene, Vector2[] spans, float brushScale, float authoredScale,
        float[]? luxelSizes)
    {
        int reservedRows = scene.HasPrimaryLocalLights ? 1 : 0;
        int usableHeight = GfxLightmapCodec.SecondaryPlaneHeight - reservedRows;
        int count = 1, cursorX = 0, cursorY = reservedRows, rowHeight = 0;
        for (int faceIndex = 0; faceIndex < scene.Polygons.Count; faceIndex++)
        {
            if (scene.IsSky(faceIndex) || scene.IsWater(faceIndex)) continue;
            MapRenderSurface surface = scene.Polygons[faceIndex];
            float requestedSize = (surface.LightmapSize ?? BrushLuxelSize) *
                (surface.LightmapSize.HasValue ? authoredScale : brushScale);
            if (!float.IsFinite(requestedSize)) requestedSize = float.MaxValue;
            float luxelSize = MathF.Max(requestedSize,
                MathF.Max(spans[faceIndex].X / (GfxLightmapCodec.SecondaryWidth - 1 - Border * 2),
                    spans[faceIndex].Y / (usableHeight - 1 - Border * 2)));
            while (TileLength(spans[faceIndex].X, luxelSize) > GfxLightmapCodec.SecondaryWidth ||
                   TileLength(spans[faceIndex].Y, luxelSize) > usableHeight)
                luxelSize = MathF.BitIncrement(luxelSize);
            if (luxelSizes is not null) luxelSizes[faceIndex] = luxelSize;
            int width = TileLength(spans[faceIndex].X, luxelSize);
            int height = TileLength(spans[faceIndex].Y, luxelSize);
            if (cursorX + width > GfxLightmapCodec.SecondaryWidth)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }
            if (cursorY + height > GfxLightmapCodec.SecondaryPlaneHeight)
            {
                count++;
                cursorX = rowHeight = 0;
                cursorY = reservedRows;
            }
            cursorX += width;
            rowHeight = Math.Max(rowHeight, height);
        }
        return count;
    }

    private static int TileLength(float span, float luxelSize) =>
        checked((int)MathF.Ceiling(span / luxelSize) + 1 + Border * 2);

    private static (Vector2[] Coordinates, Vector2 Minimum, Vector2 Maximum) Project(MapRenderSurface polygon)
    {
        Vector3 anchor = polygon.Vertices[0];
        Vector3 uAxis = Vector3.Normalize(polygon.Vertices[1] - anchor);
        Vector3 vAxis = Vector3.Normalize(Vector3.Cross(polygon.Normal, uAxis));
        Vector2[] coordinates = polygon.Vertices.Select(point => new Vector2(
            Vector3.Dot(point - anchor, uAxis), Vector3.Dot(point - anchor, vAxis))).ToArray();
        return (coordinates, coordinates.Aggregate(Vector2.Min), coordinates.Aggregate(Vector2.Max));
    }

    private static byte EncodeIrradiance(float value)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new NotSupportedException("Calculated diffuse lighting exceeds the native lightmap's encoded range.");
        return (byte)MathF.Round(255 * MathF.Sqrt(Math.Clamp(value, 0, 1)));
    }
}
