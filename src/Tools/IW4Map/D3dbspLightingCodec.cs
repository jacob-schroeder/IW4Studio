using System.Buffers.Binary;
using IW4.Assets.Assets.GfxMap;

namespace IW4Map;

internal static class D3dbspLightingCodec
{
    private const uint Ps3BooleanTrueRaw = 0x01000000;
    private const int LightGridHeaderSize = 20;
    private const int LightGridRowHeaderSize = 12;
    private const int LightRegionHullSize = 76;

    public static GfxLightGrid DecodeLightGrid(
        ReadOnlySpan<byte> headerPayload,
        ReadOnlySpan<byte> rowPayload,
        ReadOnlySpan<byte> entryPayload,
        ReadOnlySpan<byte> colorPayload,
        uint lastSunPrimaryLightIndex,
        bool hasLightRegions)
    {
        if (headerPayload.Length < LightGridHeaderSize)
        {
            throw new InvalidDataException(
                $"Light-grid header has {headerPayload.Length} bytes; at least {LightGridHeaderSize} are required.");
        }

        var mins = new ushort[3];
        var maxs = new ushort[3];
        for (int axis = 0; axis < 3; axis++)
        {
            mins[axis] = BinaryPrimitives.ReadUInt16LittleEndian(
                headerPayload.Slice(axis * sizeof(ushort), sizeof(ushort)));
            maxs[axis] = BinaryPrimitives.ReadUInt16LittleEndian(
                headerPayload.Slice((axis + 3) * sizeof(ushort), sizeof(ushort)));
        }

        var rowAxis = (GfxLightGridHorizontalAxis)BinaryPrimitives.ReadUInt32LittleEndian(
            headerPayload.Slice(12, sizeof(uint)));
        var colAxis = (GfxLightGridHorizontalAxis)BinaryPrimitives.ReadUInt32LittleEndian(
            headerPayload.Slice(16, sizeof(uint)));
        ValidateLightGridDimensions(mins, maxs, rowAxis, colAxis);

        int rowCount = checked(maxs[(int)rowAxis] - mins[(int)rowAxis] + 1);
        int expectedHeaderSize = checked(LightGridHeaderSize + rowCount * sizeof(ushort));
        if (headerPayload.Length != expectedHeaderSize)
        {
            throw new InvalidDataException(
                $"Light-grid header has {headerPayload.Length} bytes instead of the {expectedHeaderSize} required by its {rowCount} rows.");
        }

        var rowDataStart = new ushort[rowCount];
        for (int row = 0; row < rowCount; row++)
        {
            rowDataStart[row] = BinaryPrimitives.ReadUInt16LittleEndian(
                headerPayload.Slice(LightGridHeaderSize + row * sizeof(ushort), sizeof(ushort)));
        }

        GfxLightGridEntry[] entries = DecodeLightGridEntries(entryPayload);
        byte[] rawRowData;
        if (entries.Length == 0)
        {
            mins = [0, 0, 0];
            maxs = [0, 0, 0];
            rowAxis = GfxLightGridHorizontalAxis.X;
            colAxis = GfxLightGridHorizontalAxis.Y;
            rowDataStart = [ushort.MaxValue];
            rawRowData = rowPayload.ToArray();
        }
        else
        {
            rawRowData = DecodeLightGridRows(rowPayload, rowDataStart, entries.Length);
        }

        GfxLightGridColors[] colors = DecodeLightGridColors(colorPayload);
        var lightGrid = new GfxLightGrid
        {
            HasLightRegionsRaw = hasLightRegions ? Ps3BooleanTrueRaw : 0u,
            SunPrimaryLightIndex = lastSunPrimaryLightIndex,
            Mins = mins,
            Maxs = maxs,
            RowAxis = rowAxis,
            ColAxis = colAxis,
            RowDataStart = rowDataStart,
            RawRowDataSize = checked((uint)rawRowData.Length),
            RawRowData = rawRowData,
            EntryCount = checked((uint)entries.Length),
            Entries = entries,
            ColorCount = checked((uint)colors.Length),
            Colors = colors
        };

        ValidateLightGridDimensions(lightGrid);
        GetLightGridRowCount(lightGrid);
        return lightGrid;
    }

    public static IReadOnlyList<GfxLightRegion> DecodeLightRegions(
        ReadOnlySpan<byte> regionPayload,
        ReadOnlySpan<byte> hullPayload,
        ReadOnlySpan<byte> axisPayload,
        int primaryLightCount,
        bool hasLightRegions)
    {
        if (primaryLightCount < 0)
            throw new ArgumentOutOfRangeException(nameof(primaryLightCount));
        if (!hasLightRegions)
        {
            if (!regionPayload.IsEmpty || !hullPayload.IsEmpty || !axisPayload.IsEmpty)
            {
                throw new InvalidDataException(
                    "Light-region payloads cannot be supplied when the light-region lump is absent.");
            }

            var emptyRegions = new GfxLightRegion[primaryLightCount];
            for (int index = 0; index < emptyRegions.Length; index++)
                emptyRegions[index] = new GfxLightRegion();
            return emptyRegions;
        }

        if (regionPayload.Length != primaryLightCount)
        {
            throw new InvalidDataException(
                $"Light-region payload contains {regionPayload.Length} regions but ComWorld contains {primaryLightCount} primary lights.");
        }

        if (hullPayload.Length % LightRegionHullSize != 0)
        {
            throw new InvalidDataException(
                $"Light-region hull payload has {hullPayload.Length} bytes, which is not divisible by {LightRegionHullSize}.");
        }

        if (axisPayload.Length % GfxLightRegionAxis.SerializedSize != 0)
        {
            throw new InvalidDataException(
                $"Light-region axis payload has {axisPayload.Length} bytes, which is not divisible by {GfxLightRegionAxis.SerializedSize}.");
        }

        int hullCount = hullPayload.Length / LightRegionHullSize;
        int declaredHullCount = 0;
        for (int regionIndex = 0; regionIndex < regionPayload.Length; regionIndex++)
            declaredHullCount = checked(declaredHullCount + regionPayload[regionIndex]);

        if (declaredHullCount != hullCount)
        {
            throw new InvalidDataException(
                $"Light regions declare {declaredHullCount} hulls but the hull payload contains {hullCount}.");
        }

        int axisCount = axisPayload.Length / GfxLightRegionAxis.SerializedSize;
        var hullAxisCounts = new uint[hullCount];
        uint declaredAxisCount = 0;
        for (int hullIndex = 0; hullIndex < hullCount; hullIndex++)
        {
            ReadOnlySpan<byte> row = hullPayload.Slice(
                hullIndex * LightRegionHullSize,
                LightRegionHullSize);
            uint hullAxisCount = BinaryPrimitives.ReadUInt32LittleEndian(row[72..]);
            hullAxisCounts[hullIndex] = hullAxisCount;
            declaredAxisCount = checked(declaredAxisCount + hullAxisCount);
        }

        if (declaredAxisCount != (uint)axisCount)
        {
            throw new InvalidDataException(
                $"Light-region hulls declare {declaredAxisCount} axes but the axis payload contains {axisCount}.");
        }

        var regions = new GfxLightRegion[regionPayload.Length];
        int nextHull = 0;
        int nextAxis = 0;
        for (int regionIndex = 0; regionIndex < regions.Length; regionIndex++)
        {
            int regionHullCount = regionPayload[regionIndex];
            var hulls = new GfxLightRegionHull[regionHullCount];
            for (int regionHullIndex = 0; regionHullIndex < hulls.Length; regionHullIndex++)
            {
                ReadOnlySpan<byte> hullRow = hullPayload.Slice(
                    nextHull * LightRegionHullSize,
                    LightRegionHullSize);
                uint hullAxisCount = hullAxisCounts[nextHull];
                var axes = new GfxLightRegionAxis[checked((int)hullAxisCount)];
                for (int hullAxisIndex = 0; hullAxisIndex < axes.Length; hullAxisIndex++)
                {
                    ReadOnlySpan<byte> axisRow = axisPayload.Slice(
                        nextAxis * GfxLightRegionAxis.SerializedSize,
                        GfxLightRegionAxis.SerializedSize);
                    axes[hullAxisIndex] = new GfxLightRegionAxis
                    {
                        Dir = ReadSingles(axisRow, 0, 3),
                        MidPoint = ReadSingle(axisRow, 12),
                        HalfSize = ReadSingle(axisRow, 16)
                    };
                    nextAxis++;
                }

                hulls[regionHullIndex] = new GfxLightRegionHull
                {
                    KdopMidPoint = ReadSingles(hullRow, 0, 9),
                    KdopHalfSize = ReadSingles(hullRow, 36, 9),
                    AxisCount = hullAxisCount,
                    Axes = axes
                };
                nextHull++;
            }

            regions[regionIndex] = new GfxLightRegion
            {
                HullCount = regionHullCount,
                Hulls = hulls
            };
        }

        return regions;
    }

    public static byte[] EncodeLightGridHeader(GfxLightGrid lightGrid)
    {
        ArgumentNullException.ThrowIfNull(lightGrid);
        ValidateLightGridDimensions(lightGrid);

        int rowCount = GetLightGridRowCount(lightGrid);

        var data = new byte[checked(LightGridHeaderSize + rowCount * sizeof(ushort))];
        for (int axis = 0; axis < 3; axis++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(axis * sizeof(ushort)), lightGrid.Mins[axis]);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((axis + 3) * sizeof(ushort)), lightGrid.Maxs[axis]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)lightGrid.RowAxis);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)lightGrid.ColAxis);
        for (int row = 0; row < rowCount; row++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(LightGridHeaderSize + row * sizeof(ushort)),
                lightGrid.RowDataStart[row]);
        }

        return data;
    }

    public static byte[] EncodeLightGridRows(GfxLightGrid lightGrid)
    {
        ArgumentNullException.ThrowIfNull(lightGrid);
        ValidateDeclaredCount(lightGrid.RawRowDataSize, lightGrid.RawRowData.Count, "rawRowDataSize");
        ValidateDeclaredCount(lightGrid.EntryCount, lightGrid.Entries.Count, "entryCount");
        ValidateLightGridDimensions(lightGrid);
        GetLightGridRowCount(lightGrid);

        var data = new byte[lightGrid.RawRowData.Count];
        for (int index = 0; index < data.Length; index++)
            data[index] = lightGrid.RawRowData[index];

        var convertedOffsets = new HashSet<int>();
        for (int rowIndex = 0; rowIndex < lightGrid.RowDataStart.Count; rowIndex++)
        {
            ushort rowDataStart = lightGrid.RowDataStart[rowIndex];
            if (rowDataStart == ushort.MaxValue)
                continue;

            int offset = checked(rowDataStart * 4);
            if (offset > data.Length - LightGridRowHeaderSize)
            {
                throw new InvalidDataException(
                    $"Light-grid row {rowIndex} header at 0x{offset:X} exceeds the {data.Length}-byte row-data payload.");
            }

            if (!convertedOffsets.Add(offset))
                continue;

            Span<byte> header = data.AsSpan(offset, LightGridRowHeaderSize);
            for (int fieldOffset = 0; fieldOffset < 8; fieldOffset += sizeof(ushort))
            {
                ushort value = BinaryPrimitives.ReadUInt16BigEndian(header[fieldOffset..]);
                BinaryPrimitives.WriteUInt16LittleEndian(header[fieldOffset..], value);
            }

            uint firstEntry = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (firstEntry >= lightGrid.EntryCount)
            {
                throw new InvalidDataException(
                    $"Light-grid row {rowIndex} first entry {firstEntry} is outside the {lightGrid.EntryCount}-entry table.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], firstEntry);
        }

        return data;
    }

    public static byte[] EncodeLightGridEntries(GfxLightGrid lightGrid)
    {
        ArgumentNullException.ThrowIfNull(lightGrid);
        ValidateDeclaredCount(lightGrid.EntryCount, lightGrid.Entries.Count, "entryCount");

        var data = new byte[checked(lightGrid.Entries.Count * GfxLightGridEntry.SerializedSize)];
        for (int index = 0; index < lightGrid.Entries.Count; index++)
        {
            GfxLightGridEntry entry = lightGrid.Entries[index] ??
                throw new InvalidDataException($"Light-grid entry {index} is null.");
            Span<byte> row = data.AsSpan(
                index * GfxLightGridEntry.SerializedSize,
                GfxLightGridEntry.SerializedSize);
            BinaryPrimitives.WriteUInt16LittleEndian(row, entry.ColorsIndex);
            row[2] = entry.PrimaryLightIndex;
            row[3] = entry.NeedsTrace;
        }

        return data;
    }

    public static byte[] EncodeLightGridColors(
        GfxLightGrid lightGrid,
        bool omitLinkerGeneratedDefault)
    {
        ArgumentNullException.ThrowIfNull(lightGrid);
        ValidateDeclaredCount(lightGrid.ColorCount, lightGrid.Colors.Count, "colorCount");
        if (omitLinkerGeneratedDefault && lightGrid.Colors.Count == 0)
        {
            throw new InvalidDataException(
                "The linker-generated default light-grid color cannot be omitted from an empty color array.");
        }

        int generatedFallbackCount = omitLinkerGeneratedDefault
            ? GetGeneratedFallbackColorCount(lightGrid)
            : 0;
        int colorCount = lightGrid.Colors.Count - generatedFallbackCount;
        var data = new byte[checked(colorCount * GfxLightGridColors.SerializedSize)];
        for (int index = 0; index < colorCount; index++)
        {
            GfxLightGridColors colors = lightGrid.Colors[index] ??
                throw new InvalidDataException($"Light-grid color row {index} is null.");
            if (colors.RgbBytes.Count != GfxLightGridColors.SerializedSize)
            {
                throw new InvalidDataException(
                    $"Light-grid color row {index} has {colors.RgbBytes.Count} bytes instead of {GfxLightGridColors.SerializedSize}.");
            }

            int rowOffset = index * GfxLightGridColors.SerializedSize;
            for (int byteIndex = 0; byteIndex < GfxLightGridColors.SerializedSize; byteIndex++)
                data[rowOffset + byteIndex] = colors.RgbBytes[byteIndex];
        }

        return data;
    }

    private static int GetGeneratedFallbackColorCount(GfxLightGrid lightGrid)
    {
        if (lightGrid.EntryCount == 0 &&
            lightGrid.Colors.Count == 2 &&
            lightGrid.Colors[0] is { } first &&
            lightGrid.Colors[1] is { } second &&
            first.RgbBytes.SequenceEqual(second.RgbBytes))
        {
            // No-bake worlds carry a duplicated native fallback pair even
            // though neither row originated in the compiled BSP.
            return 2;
        }

        return 1;
    }

    public static byte[] EncodeLightRegions(
        IReadOnlyList<GfxLightRegion> regions,
        bool hasLightRegions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!hasLightRegions)
        {
            ValidateAbsentLightRegions(regions);
            return [];
        }

        var data = new byte[regions.Count];
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            GfxLightRegion region = RequireRegion(regions, regionIndex);
            ValidateHullCount(region, regionIndex);
            if ((uint)region.HullCount > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"Light region {regionIndex} hull count {region.HullCount} does not fit the v22 byte field.");
            }

            data[regionIndex] = (byte)region.HullCount;
        }

        return data;
    }

    public static byte[] EncodeLightRegionHulls(
        IReadOnlyList<GfxLightRegion> regions,
        bool hasLightRegions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!hasLightRegions)
        {
            ValidateAbsentLightRegions(regions);
            return [];
        }

        int hullCount = CountHulls(regions);
        var data = new byte[checked(hullCount * LightRegionHullSize)];
        int outputIndex = 0;
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            GfxLightRegion region = RequireRegion(regions, regionIndex);
            ValidateHullCount(region, regionIndex);
            for (int hullIndex = 0; hullIndex < region.Hulls.Count; hullIndex++)
            {
                GfxLightRegionHull hull = region.Hulls[hullIndex] ??
                    throw new InvalidDataException(
                        $"Light region {regionIndex} hull {hullIndex} is null.");
                ValidateHull(hull, regionIndex, hullIndex);

                Span<byte> row = data.AsSpan(outputIndex * LightRegionHullSize, LightRegionHullSize);
                WriteSingles(row, 0, hull.KdopMidPoint);
                WriteSingles(row, 36, hull.KdopHalfSize);
                BinaryPrimitives.WriteUInt32LittleEndian(row[72..], hull.AxisCount);
                outputIndex++;
            }
        }

        return data;
    }

    public static byte[] EncodeLightRegionAxes(
        IReadOnlyList<GfxLightRegion> regions,
        bool hasLightRegions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!hasLightRegions)
        {
            ValidateAbsentLightRegions(regions);
            return [];
        }

        int axisCount = CountAxes(regions);
        var data = new byte[checked(axisCount * GfxLightRegionAxis.SerializedSize)];
        int outputIndex = 0;
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            GfxLightRegion region = RequireRegion(regions, regionIndex);
            ValidateHullCount(region, regionIndex);
            for (int hullIndex = 0; hullIndex < region.Hulls.Count; hullIndex++)
            {
                GfxLightRegionHull hull = region.Hulls[hullIndex] ??
                    throw new InvalidDataException(
                        $"Light region {regionIndex} hull {hullIndex} is null.");
                ValidateHull(hull, regionIndex, hullIndex);
                for (int axisIndex = 0; axisIndex < hull.Axes.Count; axisIndex++)
                {
                    GfxLightRegionAxis axis = hull.Axes[axisIndex] ??
                        throw new InvalidDataException(
                            $"Light region {regionIndex} hull {hullIndex} axis {axisIndex} is null.");
                    if (axis.Dir.Count != 3)
                    {
                        throw new InvalidDataException(
                            $"Light region {regionIndex} hull {hullIndex} axis {axisIndex} has {axis.Dir.Count} direction components instead of 3.");
                    }

                    Span<byte> row = data.AsSpan(
                        outputIndex * GfxLightRegionAxis.SerializedSize,
                        GfxLightRegionAxis.SerializedSize);
                    WriteSingles(row, 0, axis.Dir);
                    WriteSingle(row, 12, axis.MidPoint);
                    WriteSingle(row, 16, axis.HalfSize);
                    outputIndex++;
                }
            }
        }

        return data;
    }

    private static GfxLightGridEntry[] DecodeLightGridEntries(ReadOnlySpan<byte> payload)
    {
        if (payload.Length % GfxLightGridEntry.SerializedSize != 0)
        {
            throw new InvalidDataException(
                $"Light-grid entry payload has {payload.Length} bytes, which is not divisible by {GfxLightGridEntry.SerializedSize}.");
        }

        var entries = new GfxLightGridEntry[payload.Length / GfxLightGridEntry.SerializedSize];
        for (int index = 0; index < entries.Length; index++)
        {
            ReadOnlySpan<byte> row = payload.Slice(
                index * GfxLightGridEntry.SerializedSize,
                GfxLightGridEntry.SerializedSize);
            entries[index] = new GfxLightGridEntry(
                BinaryPrimitives.ReadUInt16LittleEndian(row),
                row[2],
                row[3]);
        }

        return entries;
    }

    private static byte[] DecodeLightGridRows(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<ushort> rowDataStart,
        int entryCount)
    {
        byte[] data = payload.ToArray();
        var convertedOffsets = new HashSet<int>();
        for (int rowIndex = 0; rowIndex < rowDataStart.Count; rowIndex++)
        {
            ushort rowStart = rowDataStart[rowIndex];
            if (rowStart == ushort.MaxValue)
                continue;

            int offset = checked(rowStart * 4);
            if (offset > data.Length - LightGridRowHeaderSize)
            {
                throw new InvalidDataException(
                    $"Light-grid row {rowIndex} header at 0x{offset:X} exceeds the {data.Length}-byte row-data payload.");
            }

            if (!convertedOffsets.Add(offset))
                continue;

            Span<byte> header = data.AsSpan(offset, LightGridRowHeaderSize);
            for (int fieldOffset = 0; fieldOffset < 8; fieldOffset += sizeof(ushort))
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(header[fieldOffset..]);
                BinaryPrimitives.WriteUInt16BigEndian(header[fieldOffset..], value);
            }

            uint firstEntry = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            if (firstEntry >= (uint)entryCount)
            {
                throw new InvalidDataException(
                    $"Light-grid row {rowIndex} first entry {firstEntry} is outside the {entryCount}-entry table.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(header[8..], firstEntry);
        }

        return data;
    }

    private static GfxLightGridColors[] DecodeLightGridColors(ReadOnlySpan<byte> payload)
    {
        if (payload.Length % GfxLightGridColors.SerializedSize != 0)
        {
            throw new InvalidDataException(
                $"Light-grid color payload has {payload.Length} bytes, which is not divisible by {GfxLightGridColors.SerializedSize}.");
        }

        int sourceColorCount = payload.Length / GfxLightGridColors.SerializedSize;
        // The native PS3 world-stream helper unconditionally copies Colors[1]
        // into writable runtime storage. A no-bake BSP therefore needs two
        // identical fallback rows; sourced grids retain their authored rows
        // plus the linker-generated fallback.
        int fallbackColorCount = sourceColorCount == 0 ? 2 : 1;
        var colors = new GfxLightGridColors[checked(sourceColorCount + fallbackColorCount)];
        for (int index = 0; index < sourceColorCount; index++)
        {
            colors[index] = new GfxLightGridColors(
                payload.Slice(
                    index * GfxLightGridColors.SerializedSize,
                    GfxLightGridColors.SerializedSize).ToArray());
        }

        for (int index = sourceColorCount; index < colors.Length; index++)
            colors[index] = CreateDefaultLightGridColors();
        return colors;
    }

    private static GfxLightGridColors CreateDefaultLightGridColors()
    {
        // linker_pc evaluates these expressions with x87 precision around explicit float spills.
        // Double intermediates reproduce those stable bytes on every .NET target.
        const double gridStep = 0.6666666865348816;
        const double rotatedXFromX = 0.4714045226573944;
        const double rotatedYZFromX = -0.2357022613286972;
        const double rotatedYZFromY = 0.40824827551841736;
        const double rotatedFromZ = 0.3333333432674408;
        var rgbBytes = new byte[GfxLightGridColors.SerializedSize];
        int basisIndex = 0;
        for (int z = 0; z < 4; z++)
        {
            float deltaZ = (float)(z * gridStep - 1.0);
            for (int y = 0; y < 4; y++)
            {
                float deltaY = (float)(y * gridStep - 1.0);
                for (int x = 0; x < 4; x++)
                {
                    if (x > 0 && x < 3 && y > 0 && y < 3 && z > 0 && z < 3)
                        continue;

                    float deltaX = (float)(x * gridStep - 1.0);
                    float rotatedX = (float)(
                        (double)deltaX * rotatedXFromX +
                        (double)deltaZ * rotatedFromZ);
                    float rotatedY = (float)(
                        (double)deltaX * rotatedYZFromX +
                        (double)deltaY * rotatedYZFromY +
                        (double)deltaZ * rotatedFromZ);
                    float rotatedZ = (float)(
                        (double)deltaX * rotatedYZFromX +
                        (double)deltaY * -rotatedYZFromY +
                        (double)deltaZ * rotatedFromZ);
                    float length = MathF.Max(
                        MathF.Abs(rotatedX),
                        MathF.Max(MathF.Abs(rotatedY), MathF.Abs(rotatedZ)));
                    if (length <= 0.0f)
                        throw new InvalidDataException("Default light-grid basis has a zero-length projection.");

                    float scale = (float)(1.0 / length);
                    float projectedX = (float)((double)rotatedX * scale);
                    float projectedY = (float)((double)rotatedY * scale);
                    float projectedZ = (float)((double)rotatedZ * scale);
                    int outputOffset = basisIndex * 3;
                    rgbBytes[outputOffset] = PackDefaultLightGridColor(projectedX);
                    rgbBytes[outputOffset + 1] = PackDefaultLightGridColor(projectedY);
                    rgbBytes[outputOffset + 2] = PackDefaultLightGridColor(projectedZ);
                    basisIndex++;
                }
            }
        }

        if (basisIndex * 3 != rgbBytes.Length)
        {
            throw new InvalidDataException(
                $"Default light-grid generation produced {basisIndex} samples instead of {rgbBytes.Length / 3}.");
        }

        return new GfxLightGridColors(rgbBytes);
    }

    private static byte PackDefaultLightGridColor(float projected) =>
        (byte)(((double)projected * 0.5 + 0.5) * 255.0);

    private static void ValidateLightGridDimensions(GfxLightGrid lightGrid) =>
        ValidateLightGridDimensions(
            lightGrid.Mins,
            lightGrid.Maxs,
            lightGrid.RowAxis,
            lightGrid.ColAxis);

    private static void ValidateLightGridDimensions(
        IReadOnlyList<ushort> mins,
        IReadOnlyList<ushort> maxs,
        GfxLightGridHorizontalAxis rowAxis,
        GfxLightGridHorizontalAxis colAxis)
    {
        if (mins.Count != 3 || maxs.Count != 3)
            throw new InvalidDataException("Light-grid mins and maxs must each contain exactly 3 values.");

        if ((rowAxis, colAxis) is not
            (GfxLightGridHorizontalAxis.X, GfxLightGridHorizontalAxis.Y) and not
            (GfxLightGridHorizontalAxis.Y, GfxLightGridHorizontalAxis.X))
        {
            throw new InvalidDataException(
                $"Light grid has invalid horizontal axes {rowAxis}/{colAxis}.");
        }

        for (int axis = 0; axis < 3; axis++)
        {
            if (maxs[axis] < mins[axis])
            {
                throw new InvalidDataException(
                    $"Light-grid maximum {maxs[axis]} is below minimum {mins[axis]} on axis {axis}.");
            }
        }
    }

    private static int GetLightGridRowCount(GfxLightGrid lightGrid)
    {
        int rowCount = checked(
            lightGrid.Maxs[(int)lightGrid.RowAxis] -
            lightGrid.Mins[(int)lightGrid.RowAxis] + 1);
        if (lightGrid.RowDataStart.Count != rowCount)
        {
            throw new InvalidDataException(
                $"Light-grid rowDataStart count {lightGrid.RowDataStart.Count} does not match the expected {rowCount} rows.");
        }

        return rowCount;
    }

    private static int CountHulls(IReadOnlyList<GfxLightRegion> regions)
    {
        int hullCount = 0;
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            GfxLightRegion region = RequireRegion(regions, regionIndex);
            ValidateHullCount(region, regionIndex);
            hullCount = checked(hullCount + region.Hulls.Count);
        }

        return hullCount;
    }

    private static int CountAxes(IReadOnlyList<GfxLightRegion> regions)
    {
        int axisCount = 0;
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            GfxLightRegion region = RequireRegion(regions, regionIndex);
            ValidateHullCount(region, regionIndex);
            for (int hullIndex = 0; hullIndex < region.Hulls.Count; hullIndex++)
            {
                GfxLightRegionHull hull = region.Hulls[hullIndex] ??
                    throw new InvalidDataException(
                        $"Light region {regionIndex} hull {hullIndex} is null.");
                ValidateHull(hull, regionIndex, hullIndex);
                axisCount = checked(axisCount + hull.Axes.Count);
            }
        }

        return axisCount;
    }

    private static void ValidateAbsentLightRegions(IReadOnlyList<GfxLightRegion> regions)
    {
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            GfxLightRegion region = RequireRegion(regions, regionIndex);
            ValidateHullCount(region, regionIndex);
            if (region.HullCount != 0)
            {
                throw new InvalidDataException(
                    $"Light region {regionIndex} contains hulls while light regions are marked absent.");
            }
        }
    }

    private static GfxLightRegion RequireRegion(
        IReadOnlyList<GfxLightRegion> regions,
        int regionIndex) =>
        regions[regionIndex] ??
        throw new InvalidDataException($"Light region {regionIndex} is null.");

    private static void ValidateHullCount(GfxLightRegion region, int regionIndex)
    {
        if (region.HullCount != region.Hulls.Count)
        {
            throw new InvalidDataException(
                $"Light region {regionIndex} declares {region.HullCount} hulls but contains {region.Hulls.Count}.");
        }
    }

    private static void ValidateHull(
        GfxLightRegionHull hull,
        int regionIndex,
        int hullIndex)
    {
        if (hull.KdopMidPoint.Count != 9 || hull.KdopHalfSize.Count != 9)
        {
            throw new InvalidDataException(
                $"Light region {regionIndex} hull {hullIndex} must contain 9 K-DOP midpoints and 9 half-sizes.");
        }

        ValidateDeclaredCount(
            hull.AxisCount,
            hull.Axes.Count,
            $"light region {regionIndex} hull {hullIndex} axisCount");
    }

    private static void ValidateDeclaredCount(uint declared, int actual, string name)
    {
        if (declared != (uint)actual)
            throw new InvalidDataException($"{name} declares {declared} items but contains {actual}.");
    }

    private static void WriteSingles(
        Span<byte> destination,
        int offset,
        IReadOnlyList<float> values)
    {
        for (int index = 0; index < values.Count; index++)
            WriteSingle(destination, offset + index * sizeof(float), values[index]);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination[offset..],
            BitConverter.SingleToInt32Bits(value));

    private static float[] ReadSingles(
        ReadOnlySpan<byte> source,
        int offset,
        int count)
    {
        var values = new float[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = ReadSingle(source, offset + index * sizeof(float));

        return values;
    }

    private static float ReadSingle(ReadOnlySpan<byte> source, int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(source[offset..]));
}
