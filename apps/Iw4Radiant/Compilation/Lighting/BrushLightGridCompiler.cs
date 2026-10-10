using System.Numerics;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Codecs.GfxMap;
using IW4.Render.Geometry;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushLightGridCompiler
{
    internal static GfxLightGrid BakeLightGrid(BrushLightingScene scene, GpuRayTraversal? traversal,
        IProgress<string>? progress = null)
    {
        var mins = new ushort[3];
        var maxs = new ushort[3];
        for (int axis = 0; axis < 3; axis++)
        {
            int spacing = axis == 2 ? GfxLightGridCodec.VerticalSpacing : GfxLightGridCodec.HorizontalSpacing;
            float minimum = (scene.Minimum[axis] - GfxLightGridCodec.CoordinateOrigin) / spacing;
            float maximum = (scene.Maximum[axis] - GfxLightGridCodec.CoordinateOrigin) / spacing;
            if (minimum < 1 || maximum > ushort.MaxValue - 1)
                throw new NotSupportedException("The brush bounds exceed the native light-grid coordinate range.");
            mins[axis] = checked((ushort)(MathF.Floor(minimum) - 1));
            maxs[axis] = checked((ushort)(MathF.Ceiling(maximum) + 1));
        }
        // Entries and row entry offsets are 32-bit. Only the color references
        // are ushort; staged maps must retain a full grid beyond 65535 samples.
        long entryCount = (long)(maxs[0] - mins[0] + 1) * (maxs[1] - mins[1] + 1) * (maxs[2] - mins[2] + 1);
        bool useFallback = entryCount > ushort.MaxValue && scene.SunCount == 1 && !scene.HasPrimaryLocalLights;
        if (!useFallback) entryCount = GfxLightGridCodec.DenseEntryCount(mins, maxs);
        progress?.Report(useFallback
            ? "Light grid: preparing oversized-map fallback."
            : $"Light grid: 0/{entryCount} entries completed.");
        Vector3 sceneCenter = (scene.Minimum + scene.Maximum) * 0.5f;
        // Reuse scratch for every sample; only unique encoded colors need heap storage.
        Span<Vector3> irradiance = stackalloc Vector3[GfxLightGridCodec.SampleDirections.Count];
        Span<byte> rgb = stackalloc byte[GfxLightGridColors.SerializedSize];
        byte centerPrimary = scene.SunCount > 1 ? scene.PrimaryLightAt(sceneCenter) : (byte)1;
        scene.DiffuseIrradianceDirections(sceneCenter, irradiance, centerPrimary);
        Encode(irradiance, rgb);
        var centerColor = new GfxLightGridColors(rgb.ToArray());
        if (useFallback)
        {
            progress?.Report("Light grid complete: oversized map uses the scene-center fallback; no grid entries baked.");
            return CreateFallbackLightGrid(centerColor);
        }
        byte[]? primaryOwners = scene.HasPrimaryLocalLights
            ? SelectPrimaryOwners(scene, mins, maxs, checked((int)entryCount))
            : null;
        var entries = new List<GfxLightGridEntry>(checked((int)entryCount));
        var black = new byte[GfxLightGridColors.SerializedSize];
        var colors = new List<GfxLightGridColors>
        {
            new(black),
            centerColor
        };
        var colorIndices = new Dictionary<string, ushort>(StringComparer.Ordinal);
        colorIndices[Convert.ToHexString(black)] = 0;
        colorIndices[Convert.ToHexString(rgb)] = 1;
        var colorLookup = colorIndices.GetAlternateLookup<ReadOnlySpan<char>>();
        Span<char> key = stackalloc char[GfxLightGridColors.SerializedSize * 2];
        Span<byte> keyedRgb = stackalloc byte[GfxLightGridColors.SerializedSize];
        int discardedBits = 0;
        long completedEntries = 0;
        int lastBucket = 0;
        const int batchCapacity = 512;
        int depth = maxs[2] - mins[2] + 1;
        int columns = maxs[1] - mins[1] + 1;
        int sampleDirections = GfxLightGridCodec.SampleDirections.Count;
        var points = new Vector3[batchCapacity];
        var activePoints = new Vector3[batchCapacity];
        var activePrimaryIndices = new byte[batchCapacity];
        var activeIndex = new int[batchCapacity];
        var solid = new bool[batchCapacity];
        var activeIrradiance = new Vector3[batchCapacity * sampleDirections];
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = scene.CancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount > 2
                ? Environment.ProcessorCount - 1 : Environment.ProcessorCount
        };
        for (long first = 0; first < entryCount; first += batchCapacity)
        {
            int batchCount = (int)Math.Min(batchCapacity, entryCount - first);
            int activeCount = 0;
            for (int sample = 0; sample < batchCount; sample++)
            {
                int index = checked((int)(first + sample));
                int x = index / (columns * depth);
                int y = index / depth % columns;
                int z = index % depth;
                Vector3 point = GridPoint(mins[0] + x, mins[1] + y, mins[2] + z);
                points[sample] = point;
                solid[sample] = scene.IsInsideSolid(point);
                if (solid[sample]) continue;
                activeIndex[sample] = activeCount;
                activePoints[activeCount] = point;
                activePrimaryIndices[activeCount] = primaryOwners?[index] ?? scene.PrimaryLightAt(point);
                activeCount++;
            }
            if (activeCount > 0)
                scene.BakeLightGridSamples(activePoints, activePrimaryIndices, activeIrradiance, activeCount,
                    traversal, parallelOptions);
            for (int sample = 0; sample < batchCount; sample++)
            {
                scene.CancellationToken.ThrowIfCancellationRequested();
                if (solid[sample])
                {
                    entries.Add(new GfxLightGridEntry(0, 0, 1));
                    ReportEntryProgress();
                    continue;
                }
                Vector3 point = points[sample];
                int active = activeIndex[sample];
                byte primaryLight = activePrimaryIndices[active];
                Encode(activeIrradiance.AsSpan(active * sampleDirections, sampleDirections), rgb);
                WriteKey(rgb, discardedBits, keyedRgb, key);
                if (!colorLookup.TryGetValue(key, out ushort colorIndex))
                {
                    if (colors.Count > ushort.MaxValue)
                    {
                        (colors, colorIndices, discardedBits) = ReducePalette(colors, entries, discardedBits);
                        colorLookup = colorIndices.GetAlternateLookup<ReadOnlySpan<char>>();
                        WriteKey(rgb, discardedBits, keyedRgb, key);
                        progress?.Report($"Light grid palette: {colors.Count} colors; {discardedBits} low bits discarded per RGB component.");
                    }
                    if (!colorLookup.TryGetValue(key, out colorIndex))
                    {
                        colorIndex = checked((ushort)colors.Count);
                        colors.Add(new GfxLightGridColors(rgb.ToArray()));
                        colorIndices.Add(new string(key), colorIndex);
                    }
                }
                byte visiblePrimary = scene.PrimaryVisibility(point, Vector3.Zero, primaryLight) > 0
                    ? primaryLight
                    : scene.SunCount > 1 && primaryLight > 0 && primaryLight <= scene.SunCount
                        ? checked((byte)(255 - scene.SunCount + primaryLight))
                        : (byte)0;
                entries.Add(new GfxLightGridEntry(colorIndex, visiblePrimary, 0));
                ReportEntryProgress();
            }
        }
        // Canonical BSP export omits the final linker-generated row. Keep that
        // row separate from the authored Colors[1] used by native fallback sampling.
        colors.Add(GfxLightGridCodec.CreateDefault());
        GfxLightGrid grid = GfxLightGridCodec.CreateDenseGrid(mins, maxs, entries, colors, scene.SunCount);
        progress?.Report($"Light grid complete: {completedEntries}/{entryCount} entries.");
        return grid;

        void ReportEntryProgress()
        {
            completedEntries++;
            if (progress is null) return;
            int bucket = (int)(completedEntries * 20 / entryCount);
            if (bucket <= lastBucket || bucket >= 20) return;
            lastBucket = bucket;
            progress.Report($"Light grid: {completedEntries}/{entryCount} entries completed ({completedEntries * 100 / entryCount}%).");
        }
    }

    private static void WriteKey(ReadOnlySpan<byte> rgb, int discardedBits, Span<byte> keyedRgb, Span<char> key)
    {
        for (int index = 0; index < rgb.Length; index++)
            keyedRgb[index] = (byte)(rgb[index] >> discardedBits);
        Convert.TryToHexString(keyedRgb, key, out _);
    }

    private static (List<GfxLightGridColors> Colors, Dictionary<string, ushort> Indices, int DiscardedBits) ReducePalette(
        List<GfxLightGridColors> colors, List<GfxLightGridEntry> entries, int previousBits)
    {
        const int targetCount = (ushort.MaxValue + 1) * 3 / 4;
        for (int bits = previousBits + 1; bits <= 8; bits++)
        {
            var reduced = new List<GfxLightGridColors> { colors[0], colors[1] };
            var indices = new Dictionary<string, ushort>(StringComparer.Ordinal)
            {
                [ColorKey(colors[0].RgbBytes, bits)] = 0,
                [ColorKey(colors[1].RgbBytes, bits)] = 1
            };
            var remap = new ushort[colors.Count];
            remap[1] = 1;
            for (int oldIndex = 2; oldIndex < colors.Count; oldIndex++)
            {
                string key = ColorKey(colors[oldIndex].RgbBytes, bits);
                if (!indices.TryGetValue(key, out ushort newIndex))
                {
                    newIndex = checked((ushort)reduced.Count);
                    reduced.Add(colors[oldIndex]);
                    indices.Add(key, newIndex);
                    if (reduced.Count > targetCount) break;
                }
                remap[oldIndex] = newIndex;
            }
            if (reduced.Count > targetCount) continue;
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                GfxLightGridEntry entry = entries[entryIndex];
                entries[entryIndex] = entry with { ColorsIndex = remap[entry.ColorsIndex] };
            }
            return (reduced, indices, bits);
        }
        throw new InvalidDataException("The light-grid palette could not be reduced to its native index range.");
    }

    private static string ColorKey(IReadOnlyList<byte> rgb, int discardedBits)
    {
        Span<byte> keyedRgb = stackalloc byte[GfxLightGridColors.SerializedSize];
        for (int index = 0; index < keyedRgb.Length; index++)
            keyedRgb[index] = (byte)(rgb[index] >> discardedBits);
        return Convert.ToHexString(keyedRgb);
    }

    private static byte[] SelectPrimaryOwners(BrushLightingScene scene, ushort[] mins, ushort[] maxs,
        int entryCount)
    {
        int rows = maxs[0] - mins[0] + 1;
        int columns = maxs[1] - mins[1] + 1;
        int depth = maxs[2] - mins[2] + 1;
        var owners = new byte[entryCount];
        for (int x = 0; x < rows; x++)
        for (int y = 0; y < columns; y++)
        for (int z = 0; z < depth; z++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            Vector3 point = GridPoint(mins[0] + x, mins[1] + y, mins[2] + z);
            owners[(x * columns + y) * depth + z] = scene.IsInsideSolid(point)
                ? (byte)0 : scene.PrimaryLightAt(point);
        }

        // PS3 blends grid RGB, but ordinary Sun and local primary indices compete
        // for one runtime light. Separate them by a layer of fully baked None rows.
        // All eight corners of an interpolation cell are within one step per axis.
        // Baking these rows with owner 0 below includes Sun; changing only the
        // serialized index would lose its direct contribution.
        for (int x = 0; x < rows; x++)
        for (int y = 0; y < columns; y++)
        for (int z = 0; z < depth; z++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            int index = (x * columns + y) * depth + z;
            if (owners[index] > 0 && owners[index] <= scene.SunCount && HasLocalNeighbor(x, y, z))
                owners[index] = 0;
        }
        return owners;

        bool HasLocalNeighbor(int x, int y, int z)
        {
            for (int nx = Math.Max(0, x - 1); nx <= Math.Min(rows - 1, x + 1); nx++)
            for (int ny = Math.Max(0, y - 1); ny <= Math.Min(columns - 1, y + 1); ny++)
            for (int nz = Math.Max(0, z - 1); nz <= Math.Min(depth - 1, z + 1); nz++)
                if (owners[(nx * columns + ny) * depth + nz] > scene.SunCount)
                    return true;
            return false;
        }
    }

    private static Vector3 GridPoint(int x, int y, int z) => new(
        GfxLightGridCodec.CoordinateOrigin + x * GfxLightGridCodec.HorizontalSpacing,
        GfxLightGridCodec.CoordinateOrigin + y * GfxLightGridCodec.HorizontalSpacing,
        GfxLightGridCodec.CoordinateOrigin + z * GfxLightGridCodec.VerticalSpacing);

    private static GfxLightGrid CreateFallbackLightGrid(GfxLightGridColors centerColor)
    {
        // Oversized maps use the canonical zero-entry/no-bake grid. Native model/static lighting
        // then falls back to the scene-center/default colors while surface lightmaps stay baked.
        return new GfxLightGrid
        {
            SunPrimaryLightIndex = 1,
            Mins = [0, 0, 0],
            Maxs = [0, 0, 0],
            RowAxis = GfxLightGridHorizontalAxis.X,
            ColAxis = GfxLightGridHorizontalAxis.Y,
            RowDataStart = [ushort.MaxValue],
            RawRowDataSize = 0,
            RawRowData = [],
            EntryCount = 0,
            Entries = [],
            ColorCount = 3,
            Colors =
            [
                new GfxLightGridColors(new byte[GfxLightGridColors.SerializedSize]),
                centerColor,
                GfxLightGridCodec.CreateDefault()
            ]
        };
    }

    private static void Encode(ReadOnlySpan<Vector3> irradiance, Span<byte> bytes)
    {
        for (int sample = 0; sample < irradiance.Length; sample++)
        for (int channel = 0; channel < 3; channel++)
        {
            float value = irradiance[sample][channel];
            if (!float.IsFinite(value) || value < 0)
                throw new NotSupportedException("Calculated model lighting exceeds the native light-grid range.");
            // Native light-probe RGB decodes as (2 * byte / 255)^2.
            // Saturate overbright sums, just as the surface lightmap encoder does.
            bytes[sample * 3 + channel] = (byte)MathF.Round(127.5f * MathF.Sqrt(MathF.Min(value, 4)));
        }
    }
}
