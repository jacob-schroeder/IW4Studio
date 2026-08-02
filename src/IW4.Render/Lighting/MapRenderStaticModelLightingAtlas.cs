using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Lighting;

/// <summary>
/// Host representation of the PS3 model-lighting cache. The physical image is
/// a 512x256x4 RGBA volume with 4x4x4 tile addressing. Static-model source
/// tiles remain object-indexed on the CPU and are copied into physical
/// working-set entries as runtime handles are assigned.
/// </summary>
public sealed class MapRenderStaticModelLightingAtlas
{
    public const int Width = 512;
    public const int Height = 256;
    public const int Depth = 4;
    public const int TileWidth = 4;
    public const int TileHeight = 4;
    public const int TileDepth = 4;
    public const int EntriesPerRow = Width / TileWidth;
    public const int RowsPerSlice = Height / TileHeight;
    public const int EntryCapacity = EntriesPerRow * RowsPerSlice;
    // Native xmodel lighting starts at baseIndex 7168, leaving handles
    // 1..7168 (physical entries 0..7167) to the static-model cache.
    public const int DynamicEntryCapacity = 1024;
    public const int StaticEntryCapacity =
        EntryCapacity - DynamicEntryCapacity;
    public const int TilePixelCount =
        TileWidth * TileHeight * TileDepth;
    public const int TileByteCount = TilePixelCount * 4;

    /// <summary>
    /// Pixel row 0x21 sampling transform. Multiplying a normalized game-space
    /// material normal by this row addresses the inner 3x3x3 span around a
    /// row-0x39 tile center.
    /// </summary>
    public static Vector4 SamplerTransform { get; } = new(
        1.5f / Width,
        1.5f / Height,
        1.5f / Depth,
        0f);

    public MapRenderStaticModelLightingAtlas(
        byte[] rgbaBytes,
        byte[] sourceTileRgbaBytes,
        Vector4[] lightProbeAmbientRows,
        int entryCount)
    {
        ArgumentNullException.ThrowIfNull(rgbaBytes);
        ArgumentNullException.ThrowIfNull(sourceTileRgbaBytes);
        ArgumentNullException.ThrowIfNull(lightProbeAmbientRows);
        if (rgbaBytes.Length != Width * Height * Depth * 4)
            throw new ArgumentException("Model-lighting atlas byte size is invalid.", nameof(rgbaBytes));
        if (entryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(entryCount));
        if (sourceTileRgbaBytes.Length !=
            checked(entryCount * TileByteCount))
        {
            throw new ArgumentException(
                "Static-model source tiles must remain index-parallel with model-lighting entries.",
                nameof(sourceTileRgbaBytes));
        }
        if (lightProbeAmbientRows.Length != entryCount)
        {
            throw new ArgumentException(
                "Light-probe ambient rows must remain index-parallel with model-lighting entries.",
                nameof(lightProbeAmbientRows));
        }
        RgbaBytes = rgbaBytes;
        SourceTileRgbaBytes = sourceTileRgbaBytes;
        LightProbeAmbientRows = lightProbeAmbientRows;
        EntryCount = entryCount;
    }

    /// <summary>
    /// Cleared physical-cache template used to initialize each renderer's GPU
    /// texture and renderer-local mutable backing image.
    /// </summary>
    public byte[] RgbaBytes { get; }

    internal byte[] SourceTileRgbaBytes { get; }

    /// <summary>
    /// Per-static-draw direct-code row 0x3A. The native backend derives this
    /// from the same packed representative color that owns drawInst + 0x28;
    /// consequently this array shares the atlas entry/object index space.
    /// </summary>
    public Vector4[] LightProbeAmbientRows { get; }

    public int EntryCount { get; }

    internal ReadOnlySpan<byte> GetSourceTile(int objectIndex)
    {
        if ((uint)objectIndex >= (uint)EntryCount)
            throw new ArgumentOutOfRangeException(nameof(objectIndex));
        return SourceTileRgbaBytes.AsSpan(
            checked(objectIndex * TileByteCount),
            TileByteCount);
    }

    internal void CopySourceTileToPhysicalAtlas(
        int objectIndex,
        int entryIndex,
        Span<byte> physicalAtlas)
    {
        if ((uint)entryIndex >= StaticEntryCapacity)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        if (physicalAtlas.Length != RgbaBytes.Length)
        {
            throw new ArgumentException(
                "The physical model-lighting cache byte size is invalid.",
                nameof(physicalAtlas));
        }

        ReadOnlySpan<byte> source = GetSourceTile(objectIndex);
        int baseX =
            (entryIndex & (EntriesPerRow - 1)) * TileWidth;
        int baseY =
            (entryIndex / EntriesPerRow) * TileHeight;
        int sourceRowByteCount = TileWidth * 4;
        for (int z = 0; z < TileDepth; z++)
        {
            for (int y = 0; y < TileHeight; y++)
            {
                int sourceOffset =
                    (z * TileHeight + y) * sourceRowByteCount;
                int destinationOffset =
                    (((z * Height + baseY + y) * Width + baseX) *
                        4);
                source.Slice(sourceOffset, sourceRowByteCount)
                    .CopyTo(physicalAtlas.Slice(
                        destinationOffset,
                        sourceRowByteCount));
            }
        }
    }

    public static Vector4 EntryCoordinates(int entryIndex)
    {
        if ((uint)entryIndex >= EntryCapacity)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        return new Vector4(
            (TileWidth * (entryIndex & (EntriesPerRow - 1)) + 2f) / Width,
            (TileHeight * (entryIndex / EntriesPerRow) + 2f) / Height,
            0.5f,
            1f);
    }

    /// <summary>
    /// Mirrors the native row-0x39 + normal*row-0x21 lookup while adapting
    /// the viewer's (X,Z,-Y) normal back to the game's (X,Y,Z) atlas basis.
    /// </summary>
    internal static Vector3 LookupCoordinatesFromRenderNormal(
        Vector4 baseLightingCoords,
        Vector3 renderNormal)
    {
        Vector3 gameNormal = new(
            renderNormal.X,
            -renderNormal.Z,
            renderNormal.Y);
        float lengthSquared = gameNormal.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
        {
            return new Vector3(
                baseLightingCoords.X,
                baseLightingCoords.Y,
                baseLightingCoords.Z);
        }

        gameNormal /= MathF.Sqrt(lengthSquared);
        return new Vector3(
            baseLightingCoords.X + gameNormal.X * SamplerTransform.X,
            baseLightingCoords.Y + gameNormal.Y * SamplerTransform.Y,
            baseLightingCoords.Z + gameNormal.Z * SamplerTransform.Z);
    }

    /// <summary>
    /// Native lp programs expand normalized atlas RGB with (2*x)^2 before
    /// applying it as diffuse irradiance.
    /// </summary>
    internal static Vector3 DecodeIrradiance(Vector3 encodedRgb)
    {
        Vector3 doubled = encodedRgb * 2f;
        return doubled * doubled;
    }
}

/// <summary>
/// Builds object-indexed static-model lighting source tiles during scene build.
/// The physical cache is populated later by the visibility-driven working set.
/// Tile contents, ground-lighting selection, light-grid RLE lookup, fixed-point
/// color blend, and 56-to-64 texel expansion match the PS3 consumer. Collision
/// NeedsTrace rejection is not applied until the editor has a compatible
/// CM_BoxSightTrace equivalent.
/// </summary>
public static class MapRenderStaticModelLightingAtlasBuilder
{
    // (drawInst->flags & 2) selects the serialized GroundLighting fill path.
    private const byte GroundLightingFlag = 0x02;
    private const float GridOrigin = -131072f;
    private const float MinimumCornerWeight = 0.001f;

    // These vector literals select squared RGB and unsquared alpha for direct
    // row 0x3A.
    private static readonly float LightProbeRgbScale =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3C008081));
    private static readonly float LightProbeAlphaScale =
        BitConverter.Int32BitsToSingle(unchecked((int)0x3A808081));

    // The final 56-direction light-grid color uses these eight representative
    // RGB triplets before writing drawInst + 0x28.
    private static readonly byte[] LightProbeRepresentativeSamples =
        [0, 3, 12, 15, 40, 43, 52, 55];

    // Expands 56 RGB directions to one 4x4x4 tile.
    private static readonly sbyte[] ExpandedSourceSamples =
    [
        0, 1, 4, 5, 16, 17, 20, 0,
        2, 3, 6, 7, 18, 19, 3, 21,
        8, 9, 12, 13, 22, -1, 24, 25,
        10, 11, 14, 15, 15, 23, 26, 27,
        28, 29, 32, 40, 40, 41, 44, 45,
        30, 31, 43, 33, 42, 43, 46, 47,
        34, 52, 36, 37, 48, 49, 52, 53,
        55, 35, 38, 39, 50, 51, 54, 55
    ];

    public static MapRenderStaticModelLightingAtlas Build(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        IReadOnlyList<GfxStaticModelDrawInst> draws = world.Dpvs.SModelDrawInsts;
        IReadOnlyList<GfxStaticModelInst> instances = world.Dpvs.SModelInsts;
        if (instances.Count < draws.Count)
        {
            throw new InvalidDataException(
                "GfxWorld static draw and lighting-origin arrays do not share one object-index space.");
        }

        byte[] physicalAtlas = new byte[
            MapRenderStaticModelLightingAtlas.Width *
            MapRenderStaticModelLightingAtlas.Height *
            MapRenderStaticModelLightingAtlas.Depth * 4];
        byte[] sourceTiles = new byte[
            checked(draws.Count *
                MapRenderStaticModelLightingAtlas.TileByteCount)];
        var lightProbeAmbientRows = new Vector4[draws.Count];
        Span<byte> blended = stackalloc byte[GfxLightGridColors.SerializedSize];
        for (int entryIndex = 0; entryIndex < draws.Count; entryIndex++)
        {
            GfxStaticModelDrawInst draw = draws[entryIndex];
            if ((draw.Flags & GroundLightingFlag) != 0)
            {
                WriteGroundTile(
                    sourceTiles,
                    entryIndex,
                    draw.GroundLighting);
                lightProbeAmbientRows[entryIndex] =
                    DecodeLightProbeAmbientRow(draw.GroundLighting);
                continue;
            }

            GfxStaticModelInst instance = instances[entryIndex];
            if (TrySampleLightGrid(
                    world.LightGrid,
                    new Vector3(
                        instance.LightingOrigin.X,
                        instance.LightingOrigin.Y,
                        instance.LightingOrigin.Z),
                    draw.PrimaryLightIndex,
                    blended,
                    out byte primaryWeight))
            {
                WriteExpandedTile(
                    sourceTiles,
                    entryIndex,
                    blended,
                    primaryWeight);
                lightProbeAmbientRows[entryIndex] =
                    DecodeLightProbeAmbientRow(
                        ReduceLightProbeAmbientColor(
                            blended,
                            primaryWeight));
            }
            else
            {
                // Native extrapolation selects default color 1 when present.
                int fallback = world.LightGrid.Colors.Count > 1 ? 1 : 0;
                if ((uint)fallback < (uint)world.LightGrid.Colors.Count &&
                    world.LightGrid.Colors[fallback].RgbBytes.Count ==
                        GfxLightGridColors.SerializedSize)
                {
                    CopyColors(world.LightGrid.Colors[fallback], blended);
                    WriteExpandedTile(
                        sourceTiles,
                        entryIndex,
                        blended,
                        0xff);
                    lightProbeAmbientRows[entryIndex] =
                        DecodeLightProbeAmbientRow(
                            ReduceLightProbeAmbientColor(
                                blended,
                                0xff));
                }
            }
        }

        return new MapRenderStaticModelLightingAtlas(
            physicalAtlas,
            sourceTiles,
            lightProbeAmbientRows,
            draws.Count);
    }

    /// <summary>
    /// Rounds the average of eight representative directions independently for
    /// RGB and preserves the caller-supplied primary-light weight in packed
    /// alpha.
    /// </summary>
    internal static GfxColor ReduceLightProbeAmbientColor(
        ReadOnlySpan<byte> rgb,
        byte alpha)
    {
        if (rgb.Length != GfxLightGridColors.SerializedSize)
        {
            throw new ArgumentException(
                "A light-probe ambient reduction requires exactly 56 RGB triplets.",
                nameof(rgb));
        }

        int red = 0;
        int green = 0;
        int blue = 0;
        foreach (byte sample in LightProbeRepresentativeSamples)
        {
            int offset = sample * 3;
            red += rgb[offset];
            green += rgb[offset + 1];
            blue += rgb[offset + 2];
        }

        uint packed =
            (uint)((red + 4) >> 3) << 24 |
            (uint)((green + 4) >> 3) << 16 |
            (uint)((blue + 4) >> 3) << 8 |
            alpha;
        return new GfxColor(packed);
    }

    /// <summary>
    /// Mirrors the PS3 static backend's drawInst + 0x28 expansion into direct
    /// code row 0x3A: RGB is scaled then squared; alpha remains linear.
    /// </summary>
    internal static Vector4 DecodeLightProbeAmbientRow(GfxColor color)
    {
        uint packed = color.Packed;
        float red = (byte)(packed >> 24) * LightProbeRgbScale;
        float green = (byte)(packed >> 16) * LightProbeRgbScale;
        float blue = (byte)(packed >> 8) * LightProbeRgbScale;
        return new Vector4(
            red * red,
            green * green,
            blue * blue,
            (byte)packed * LightProbeAlphaScale);
    }

    private static bool TrySampleLightGrid(
        GfxLightGrid grid,
        Vector3 sample,
        byte nonSunPrimaryLightIndex,
        Span<byte> output,
        out byte primaryWeight)
    {
        primaryWeight = 0;
        if (grid.Mins.Count < 3 || grid.Maxs.Count < 3 ||
            grid.RowAxis > 1 || grid.ColAxis > 1 ||
            grid.RowAxis == grid.ColAxis || grid.Entries.Count == 0 ||
            grid.Colors.Count == 0)
        {
            return false;
        }

        Span<int> pos = stackalloc int[3];
        pos[0] = ((int)MathF.Floor(sample.X) + 0x20000) >> 5;
        pos[1] = ((int)MathF.Floor(sample.Y) + 0x20000) >> 5;
        pos[2] = ((int)MathF.Floor(sample.Z) + 0x20000) >> 6;
        int rowAxis = checked((int)grid.RowAxis);
        int colAxis = checked((int)grid.ColAxis);
        float rowLerp = (Axis(sample, rowAxis) - GridOrigin) * (1f / 32f) - pos[rowAxis];
        float colLerp = (Axis(sample, colAxis) - GridOrigin) * (1f / 32f) - pos[colAxis];
        float zLerp = (sample.Z - GridOrigin) * (1f / 64f) - pos[2];
        Span<float> weights = stackalloc float[8];
        Span<int> entryIndices = stackalloc int[8];
        entryIndices.Fill(-1);
        SetWeights(weights, rowLerp, colLerp, zLerp);

        ReadQuad(grid, pos, entryIndices, 0);
        pos[rowAxis]++;
        ReadQuad(grid, pos, entryIndices, 4);
        pos[rowAxis]--;

        byte primaryIndex = 0;
        float bestPrimaryWeight = 0f;
        for (int corner = 0; corner < 8; corner++)
        {
            int index = entryIndices[corner];
            if ((uint)index >= (uint)grid.Entries.Count ||
                weights[corner] < MinimumCornerWeight)
            {
                entryIndices[corner] = -1;
                continue;
            }
            GfxLightGridEntry entry = grid.Entries[index];
            byte candidate = entry.PrimaryLightIndex;
            bool replace = primaryIndex == 0 ||
                (candidate != 0 &&
                 (primaryIndex == byte.MaxValue ||
                  (candidate != byte.MaxValue && weights[corner] > bestPrimaryWeight)));
            if (replace)
            {
                primaryIndex = candidate;
                bestPrimaryWeight = weights[corner];
            }
        }

        if (primaryIndex == byte.MaxValue)
            primaryIndex = checked((byte)grid.SunPrimaryLightIndex);
        else if (grid.HasLightRegions != 0 &&
                 primaryIndex != checked((byte)grid.SunPrimaryLightIndex))
            primaryIndex = nonSunPrimaryLightIndex;

        Span<ushort> colors = stackalloc ushort[8];
        Span<float> colorWeights = stackalloc float[8];
        int colorCount = 0;
        float totalWeight = 0f;
        float visibleWeight = 0f;
        float occludedWeight = 0f;
        for (int corner = 0; corner < 8; corner++)
        {
            int index = entryIndices[corner];
            if ((uint)index >= (uint)grid.Entries.Count)
                continue;
            GfxLightGridEntry entry = grid.Entries[index];
            float weight = weights[corner];
            totalWeight += weight;
            if (entry.PrimaryLightIndex == primaryIndex)
                visibleWeight += weight;
            else if (entry.PrimaryLightIndex == 0 ||
                     (entry.PrimaryLightIndex == byte.MaxValue &&
                      primaryIndex != 0))
            {
                // Accumulate rejected or occluded entries. CM visibility
                // suppression remains unavailable to the editor.
                occludedWeight += weight;
            }
            int existing = -1;
            for (int i = 0; i < colorCount; i++)
            {
                if (colors[i] == entry.ColorsIndex)
                {
                    existing = i;
                    break;
                }
            }
            if (existing >= 0)
                colorWeights[existing] += weight;
            else if (colorCount < colors.Length)
            {
                colors[colorCount] = entry.ColorsIndex;
                colorWeights[colorCount] = weight;
                colorCount++;
            }
        }
        if (colorCount == 0 || totalWeight <= 0f)
            return false;

        primaryWeight = QuantizePrimaryLightWeight(
            primaryIndex,
            visibleWeight,
            occludedWeight);

        if (colorCount == 1)
        {
            if ((uint)colors[0] >= (uint)grid.Colors.Count)
                return false;
            CopyColors(grid.Colors[colors[0]], output);
            return true;
        }

        Span<ushort> fixedWeights = stackalloc ushort[8];
        int fixedSum = 0;
        int largest = 0;
        float normalize = 1f / totalWeight;
        for (int i = 0; i < colorCount; i++)
        {
            int fixedWeight = (int)(normalize * 256f * colorWeights[i] + 0.5f);
            fixedWeights[i] = checked((ushort)fixedWeight);
            fixedSum += fixedWeight;
            if (fixedWeights[i] > fixedWeights[largest])
                largest = i;
        }
        fixedWeights[largest] = checked((ushort)(fixedWeights[largest] + 256 - fixedSum));
        Span<int> accumulated = stackalloc int[GfxLightGridColors.SerializedSize];
        for (int i = 0; i < colorCount; i++)
        {
            if ((uint)colors[i] >= (uint)grid.Colors.Count ||
                grid.Colors[colors[i]].RgbBytes.Count != GfxLightGridColors.SerializedSize)
                return false;
            IReadOnlyList<byte> rgb = grid.Colors[colors[i]].RgbBytes;
            int weight = fixedWeights[i];
            for (int component = 0; component < accumulated.Length; component++)
                accumulated[component] += rgb[component] * weight;
        }
        for (int component = 0; component < output.Length; component++)
            output[component] = (byte)((accumulated[component] + 127) >> 8);
        return true;
    }

    internal static byte QuantizePrimaryLightWeight(
        byte selectedPrimaryLightIndex,
        float visibleWeight,
        float occludedWeight)
    {
        if (!float.IsFinite(visibleWeight) || visibleWeight < 0f)
            throw new ArgumentOutOfRangeException(nameof(visibleWeight));
        if (!float.IsFinite(occludedWeight) || occludedWeight < 0f)
            throw new ArgumentOutOfRangeException(nameof(occludedWeight));
        float factor;
        if (selectedPrimaryLightIndex == 0)
            factor = 0f;
        else if (occludedWeight == 1f && visibleWeight != 1f)
            factor = 0.5f;
        else
        {
            float denominator = occludedWeight + visibleWeight;
            factor = denominator > 0f
                ? visibleWeight / denominator
                : 0f;
        }
        return (byte)Math.Clamp(
            (int)(factor * 255f + 0.5f), 0, 255);
    }

    private static void ReadQuad(
        GfxLightGrid grid,
        ReadOnlySpan<int> pos,
        Span<int> entries,
        int outputOffset)
    {
        entries.Slice(outputOffset, 4).Fill(-1);
        int rowAxis = checked((int)grid.RowAxis);
        int colAxis = checked((int)grid.ColAxis);
        int rowIndex = pos[rowAxis] - grid.Mins[rowAxis];
        if ((uint)rowIndex >= (uint)grid.RowDataStart.Count)
            return;
        ushort rowWordOffset = grid.RowDataStart[rowIndex];
        if (rowWordOffset == ushort.MaxValue)
            return;
        int rowOffset = rowWordOffset * 4;
        IReadOnlyList<byte> raw = grid.RawRowData;
        if (rowOffset < 0 || rowOffset + 12 > raw.Count)
            return;

        ushort colStart = ReadUInt16BigEndian(raw, rowOffset);
        ushort colCount = ReadUInt16BigEndian(raw, rowOffset + 2);
        ushort zStart = ReadUInt16BigEndian(raw, rowOffset + 4);
        ushort zCount = ReadUInt16BigEndian(raw, rowOffset + 6);
        uint firstEntryRaw = ReadUInt32BigEndian(raw, rowOffset + 8);
        if (firstEntryRaw > int.MaxValue)
            return;
        int firstEntry = (int)firstEntryRaw;
        int colIndex = pos[colAxis] - colStart;
        int z = pos[2] - zStart;
        if (colIndex < -1 || colIndex + 1 > colCount ||
            z < -1 || z + 1 > zCount)
            return;

        int cursor = rowOffset + 12;
        int fullRunSize = zCount > byte.MaxValue ? 4 : 3;
        if (colIndex == -1)
        {
            if (!TryReadRun(raw, cursor, zCount, out _, out int runDepth, out int baseZ))
                return;
            SetEntry(grid, entries, outputOffset + 2, firstEntry + z - baseZ,
                z - baseZ >= 0 && z - baseZ < runDepth);
            SetEntry(grid, entries, outputOffset + 3, firstEntry + z - baseZ + 1,
                z - baseZ + 1 >= 0 && z - baseZ + 1 < runDepth);
            return;
        }

        while (cursor + 2 <= raw.Count)
        {
            int runColumns = raw[cursor];
            int runDepth = raw[cursor + 1];
            if (runColumns == 0)
                return;
            if (colIndex < runColumns)
                break;
            colIndex -= runColumns;
            firstEntry += runDepth * runColumns;
            cursor += runDepth != 0 ? fullRunSize : 2;
        }
        if (cursor + 2 > raw.Count)
            return;

        int currentColumns = raw[cursor];
        int currentDepth = raw[cursor + 1];
        if (currentDepth != 0)
        {
            if (!TryReadRun(raw, cursor, zCount, out _, out currentDepth, out int baseZ))
                return;
            int localZ = z - baseZ;
            int lookup = firstEntry + colIndex * currentDepth + localZ;
            SetEntry(grid, entries, outputOffset, lookup,
                localZ >= 0 && localZ < currentDepth);
            SetEntry(grid, entries, outputOffset + 1, lookup + 1,
                localZ + 1 >= 0 && localZ + 1 < currentDepth);
            if (colIndex + 1 < currentColumns)
            {
                SetEntry(grid, entries, outputOffset + 2, lookup + currentDepth,
                    localZ >= 0 && localZ < currentDepth);
                SetEntry(grid, entries, outputOffset + 3, lookup + currentDepth + 1,
                    localZ + 1 >= 0 && localZ + 1 < currentDepth);
                return;
            }
        }
        else if (colIndex + 1 < currentColumns)
        {
            return;
        }

        if (pos[colAxis] + 1 == colStart + colCount)
            return;
        int nextCursor = cursor + (currentDepth != 0 ? fullRunSize : 2);
        int nextFirstEntry = firstEntry + currentDepth * currentColumns;
        if (!TryReadRun(raw, nextCursor, zCount, out _, out int nextDepth, out int nextBaseZ) ||
            nextDepth == 0)
            return;
        int nextLocalZ = z - nextBaseZ;
        SetEntry(grid, entries, outputOffset + 2, nextFirstEntry + nextLocalZ,
            nextLocalZ >= 0 && nextLocalZ < nextDepth);
        SetEntry(grid, entries, outputOffset + 3, nextFirstEntry + nextLocalZ + 1,
            nextLocalZ + 1 >= 0 && nextLocalZ + 1 < nextDepth);
    }

    private static bool TryReadRun(
        IReadOnlyList<byte> raw,
        int offset,
        int zCount,
        out int columns,
        out int depth,
        out int baseZ)
    {
        columns = depth = baseZ = 0;
        int size = zCount > byte.MaxValue ? 4 : 3;
        if (offset < 0 || offset + size > raw.Count)
            return false;
        columns = raw[offset];
        depth = raw[offset + 1];
        baseZ = raw[offset + 2];
        if (size == 4)
            baseZ |= raw[offset + 3] << 8;
        return true;
    }

    private static void SetEntry(
        GfxLightGrid grid,
        Span<int> entries,
        int destination,
        int entryIndex,
        bool valid)
    {
        if (valid && (uint)entryIndex < (uint)grid.Entries.Count)
            entries[destination] = entryIndex;
    }

    private static void SetWeights(
        Span<float> weights,
        float row,
        float col,
        float z)
    {
        float inverseRow = 1f - row;
        float inverseCol = 1f - col;
        float inverseZ = 1f - z;
        weights[0] = inverseRow * inverseCol * inverseZ;
        weights[1] = inverseRow * inverseCol * z;
        weights[2] = inverseRow * col * inverseZ;
        weights[3] = inverseRow * col * z;
        weights[4] = row * inverseCol * inverseZ;
        weights[5] = row * inverseCol * z;
        weights[6] = row * col * inverseZ;
        weights[7] = row * col * z;
    }

    private static float Axis(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };

    private static void CopyColors(GfxLightGridColors colors, Span<byte> output)
    {
        if (colors.RgbBytes.Count != output.Length)
            throw new InvalidDataException("GfxLightGridColors payload is not 168 bytes.");
        for (int index = 0; index < output.Length; index++)
            output[index] = colors.RgbBytes[index];
    }

    private static void WriteGroundTile(
        byte[] sourceTiles,
        int entryIndex,
        GfxColor color)
    {
        uint packed = color.Packed;
        byte r = (byte)(packed >> 24);
        byte g = (byte)(packed >> 16);
        byte b = (byte)(packed >> 8);
        byte a = (byte)packed;
        for (int output = 0;
             output < MapRenderStaticModelLightingAtlas.TilePixelCount;
             output++)
        {
            WriteTilePixel(
                sourceTiles,
                entryIndex,
                output,
                r,
                g,
                b,
                a);
        }
    }

    private static void WriteExpandedTile(
        byte[] sourceTiles,
        int entryIndex,
        ReadOnlySpan<byte> rgb,
        byte alpha)
    {
        for (int output = 0; output < ExpandedSourceSamples.Length; output++)
        {
            // Output 18 is overwritten by the final expansion mapping; output
            // 21 is intentionally never written into the cleared tile.
            if (ExpandedSourceSamples[output] < 0)
                continue;
            int source = ExpandedSourceSamples[output] * 3;
            WriteTilePixel(
                sourceTiles,
                entryIndex,
                output,
                rgb[source],
                rgb[source + 1],
                rgb[source + 2],
                alpha);
        }
    }

    private static void WriteTilePixel(
        byte[] sourceTiles,
        int entryIndex,
        int tilePixel,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        int offset = checked(
            entryIndex * MapRenderStaticModelLightingAtlas.TileByteCount +
            tilePixel * 4);
        sourceTiles[offset] = r;
        sourceTiles[offset + 1] = g;
        sourceTiles[offset + 2] = b;
        sourceTiles[offset + 3] = a;
    }

    private static ushort ReadUInt16BigEndian(IReadOnlyList<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian([bytes[offset], bytes[offset + 1]]);

    private static uint ReadUInt32BigEndian(IReadOnlyList<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(
        [
            bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]
        ]);
}
