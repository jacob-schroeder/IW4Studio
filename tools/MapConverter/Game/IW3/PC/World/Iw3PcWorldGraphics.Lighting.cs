using System.Buffers.Binary;

namespace MapConverter.Game.IW3.PC.World;

internal static partial class Iw3PcWorldGraphics
{
    private sealed record Lighting(byte[] Probes, int ProbeCount, byte[] GridHeader, byte[] GridRows,
        byte[] GridEntries, byte[] GridColors, bool HasRegions, byte[] Regions,
        byte[] RegionHulls, byte[] RegionAxes);

    private sealed partial class Graphics
    {
        internal Lighting EncodeLighting()
        {
            int probeCount = Count(gfx + 228, "reflection probe");
            int probes = Required(gfx + 232, "reflection probe table");
            if (probeCount is < 1 or > byte.MaxValue)
                throw new InvalidDataException("IW3 PC reflection-probe count exceeds v22 bounds.");
            byte[] probeBytes = EncodeProbes(probes, probeCount);
            int grid = gfx + 272;
            int rowAxis = Count(grid + 20, "light-grid row axis");
            int colAxis = Count(grid + 24, "light-grid column axis");
            if (rowAxis > 2 || colAxis > 2 || rowAxis == colAxis)
                throw new InvalidDataException("IW3 PC light-grid axes are invalid.");
            int rowCount = U16(grid + 14 + rowAxis * 2) - U16(grid + 8 + rowAxis * 2) + 1;
            if (rowCount <= 0)
                throw new InvalidDataException("IW3 PC light-grid row bounds are invalid.");
            int rowStarts = Required(grid + 28, "light-grid row starts");
            int rowBytes = Count(grid + 32, "light-grid row bytes");
            int rawRows = Required(grid + 36, "light-grid rows");
            int entryCount = Count(grid + 40, "light-grid entry");
            int entries = Required(grid + 44, "light-grid entries");
            int colorCount = Count(grid + 48, "light-grid color");
            int colors = Required(grid + 52, "light-grid colors");
            if (rowBytes == 0 || entryCount == 0 || colorCount == 0)
                throw new InvalidDataException("IW3 PC GfxWorld has no authored light-grid payload.");
            var header = new byte[checked(20 + rowCount * 2)];
            for (int axis = 0; axis < 3; axis++)
            {
                W16(header, axis * 2, U16(grid + 8 + axis * 2));
                W16(header, (axis + 3) * 2, U16(grid + 14 + axis * 2));
            }
            W32(header, 12, rowAxis);
            W32(header, 16, colAxis);
            zone.Bytes(rowStarts, checked(rowCount * 2)).CopyTo(header.AsSpan(20));
            byte[] gridRows = zone.Bytes(rawRows, rowBytes).ToArray();
            byte[] gridEntries = zone.Bytes(entries, checked(entryCount * 4)).ToArray();
            byte[] gridColors = zone.Bytes(colors, checked(colorCount * 168)).ToArray();
            bool hasRegions = B(grid) != 0;
            (byte[] regions, byte[] hulls, byte[] axes) = hasRegions ? EncodeRegions() : ([], [], []);
            return new Lighting(probeBytes, probeCount, header, gridRows, gridEntries, gridColors,
                hasRegions, regions, hulls, axes);
        }

        private byte[] EncodeProbes(int probes, int count)
        {
            const int edge = 64, faces = 6, topMipBytes = edge * edge * 4;
            const int facePixels = 21844, diskPixels = 131064, diskRow = 131140;
            var result = new byte[checked((count - 1) * diskRow)];
            for (int index = 1; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int probe = probes + index * 16;
                int image = Required(probe + 12, "reflection-probe image");
                int load = Required(image + 4, "reflection-probe cubemap");
                if (B(image) != 5 || U16(image + 24) != edge || U16(image + 26) != edge ||
                    U16(image + 28) != 1 || I32(load + 12) != diskPixels)
                    throw new InvalidDataException($"IW3 PC reflection probe {index} is not a resident 64x64 RGBA cubemap.");
                ReadOnlySpan<byte> pixels = zone.Bytes(load + 16, diskPixels);
                int row = (index - 1) * diskRow;
                for (int axis = 0; axis < 3; axis++) WF(result, row + axis * 4, F32(probe + axis * 4));
                Span<byte> output = result.AsSpan(row + 76, diskPixels);
                int sourceMip = 0;
                for (int mip = 0; mip < 7; mip++)
                {
                    int side = Math.Max(1, edge >> mip), mipBytes = side * side * 4;
                    for (int face = 0; face < faces; face++)
                    {
                        int source = sourceMip + face * mipBytes;
                        int tail = 0;
                        for (int previous = 1; previous < mip; previous++)
                        {
                            int previousSide = Math.Max(1, edge >> previous);
                            tail += previousSide * previousSide * 4;
                        }
                        int destination = mip == 0 ? face * topMipBytes :
                            faces * topMipBytes + face * (facePixels - topMipBytes) + tail;
                        for (int pixel = 0; pixel < mipBytes; pixel += 4)
                        {
                            double alpha = pixels[source + pixel + 3] / 255.0;
                            for (int channel = 0; channel < 3; channel++)
                            {
                                double linear = Math.Clamp(4.0 * (pixels[source + pixel + channel] / 255.0) * alpha, 0, 1);
                                output[destination + pixel + channel] = (byte)Math.Round(255.0 * Math.Sqrt(linear),
                                    MidpointRounding.AwayFromZero);
                            }
                            output[destination + pixel + 3] = pixels[source + pixel + 3];
                        }
                    }
                    sourceMip += faces * mipBytes;
                }
                if (sourceMip != diskPixels)
                    throw new InvalidDataException("IW3 PC reflection probe mip extents do not cover its payload.");
            }
            return result;
        }

        private (byte[] Regions, byte[] Hulls, byte[] Axes) EncodeRegions()
        {
            int count = Count(gfx + 220, "light region");
            int table = Required(gfx + 576, "light region table");
            var regions = new byte[count];
            using var hulls = new MemoryStream();
            using var axes = new MemoryStream();
            for (int region = 0; region < count; region++)
            {
                token.ThrowIfCancellationRequested();
                int sourceRegion = table + region * 8;
                int hullCount = Count(sourceRegion, "light-region hull");
                if (hullCount > byte.MaxValue || hullCount != 0 && P(sourceRegion + 4) == 0)
                    throw new InvalidDataException($"IW3 PC light region {region} has an invalid hull table.");
                regions[region] = (byte)hullCount;
                int sourceHulls = P(sourceRegion + 4);
                for (int index = 0; index < hullCount; index++)
                {
                    int sourceHull = sourceHulls + index * 80;
                    int axisCount = Count(sourceHull + 72, "light-region hull axis");
                    if (axisCount > 17 || axisCount != 0 && P(sourceHull + 76) == 0)
                        throw new InvalidDataException($"IW3 PC light region {region} has invalid axes.");
                    byte[] hull = zone.Bytes(sourceHull, 76).ToArray();
                    for (int component = 0; component < 18; component++)
                        Finite(F32(sourceHull + component * 4), "light-region hull bound");
                    hulls.Write(hull);
                    int sourceAxes = P(sourceHull + 76);
                    for (int axis = 0; axis < axisCount; axis++)
                    {
                        int source = sourceAxes + axis * 20;
                        for (int component = 0; component < 5; component++)
                            Finite(F32(source + component * 4), "light-region axis");
                        axes.Write(zone.Bytes(source, 20));
                    }
                }
            }
            return (regions, hulls.ToArray(), axes.ToArray());
        }
    }
}
