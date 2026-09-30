using System.Numerics;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Codecs.GfxMap;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushLightGridCompiler
{
    internal static GfxLightGrid BakeLightGrid(BrushLightingScene scene, IProgress<string>? progress = null)
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
        long completedEntries = 0;
        int lastBucket = 0;
        for (int x = mins[0]; x <= maxs[0]; x++)
        for (int y = mins[1]; y <= maxs[1]; y++)
        for (int z = mins[2]; z <= maxs[2]; z++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            Vector3 point = GridPoint(x, y, z);
            if (scene.IsInsideSolid(point))
            {
                entries.Add(new GfxLightGridEntry(0, 0, 1));
                ReportEntryProgress();
                continue;
            }
            byte primaryLight = primaryOwners?[entries.Count] ?? scene.PrimaryLightAt(point);
            scene.DiffuseIrradianceDirections(point, irradiance, primaryLight);
            Encode(irradiance, rgb);
            Convert.TryToHexString(rgb, key, out _);
            if (!colorLookup.TryGetValue(key, out ushort colorIndex))
            {
                if (colors.Count > ushort.MaxValue)
                    throw new NotSupportedException("The baked light grid exceeds 65536 unique colors. Reduce map bounds or lighting variation.");
                colorIndex = checked((ushort)colors.Count);
                colors.Add(new GfxLightGridColors(rgb.ToArray()));
                colorIndices.Add(new string(key), colorIndex);
            }
            byte visiblePrimary = scene.PrimaryVisibility(point, Vector3.Zero, primaryLight) > 0
                ? primaryLight
                : scene.SunCount > 1 && primaryLight > 0 && primaryLight <= scene.SunCount
                    ? checked((byte)(255 - scene.SunCount + primaryLight))
                    : (byte)0;
            entries.Add(new GfxLightGridEntry(colorIndex, visiblePrimary, 0));
            ReportEntryProgress();
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
            if (!float.IsFinite(value) || value < 0 || value > 4)
                throw new NotSupportedException("Calculated model lighting exceeds the native light-grid range.");
            // Native light-probe RGB decodes as (2 * byte / 255)^2.
            bytes[sample * 3 + channel] = (byte)Math.Clamp((int)MathF.Round(127.5f * MathF.Sqrt(value)), 0, 255);
        }
    }
}
