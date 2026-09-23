using System.Buffers.Binary;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Math;
namespace IW4.Game.Codecs.GfxMap;

public static class GfxLightGridCodec
{
    public const float CoordinateOrigin = -131072;
    public const int HorizontalSpacing = 32;
    public const int VerticalSpacing = 64;

    public static IReadOnlyList<Vec3> SampleDirections { get; } = Array.AsReadOnly(CreateSampleDirections());

    /// <summary>Encodes a complete X-row/Y-column lattice; entries advance Z, then Y, then X.</summary>
    public static GfxLightGrid CreateDenseGrid(IReadOnlyList<ushort> mins, IReadOnlyList<ushort> maxs,
        IReadOnlyList<GfxLightGridEntry> entries, IReadOnlyList<GfxLightGridColors> colors, uint sunPrimaryLightIndex)
    {
        if (mins.Count != 3 || maxs.Count != 3 || Enumerable.Range(0, 3).Any(axis => maxs[axis] < mins[axis]))
            throw new ArgumentException("A light-grid lattice requires three ordered coordinate bounds.");
        int rows = maxs[0] - mins[0] + 1, columns = maxs[1] - mins[1] + 1, depth = maxs[2] - mins[2] + 1;
        if (depth > byte.MaxValue)
            throw new NotSupportedException("A dense light-grid column can contain at most 255 vertical samples.");
        if (entries.Count != checked(rows * columns * depth))
            throw new ArgumentException("The light-grid entry count does not match its lattice dimensions.");
        if (colors.Count < 2 || colors.Any(color => color.RgbBytes.Count != GfxLightGridColors.SerializedSize) ||
            entries.Any(entry => entry.ColorsIndex >= colors.Count))
            throw new ArgumentException("The light-grid colors and entry indices are inconsistent.");
        int rowBytes = (12 + ((columns + 254) / 255) * 3 + 3) & ~3;
        if (checked((rows - 1) * rowBytes / 4) >= ushort.MaxValue)
            throw new NotSupportedException("The light-grid row offsets exceed the native 16-bit word range.");
        var starts = new ushort[rows];
        var data = new byte[checked(rows * rowBytes)];
        for (int row = 0; row < rows; row++)
        {
            int offset = row * rowBytes;
            starts[row] = checked((ushort)(offset / 4));
            Span<byte> header = data.AsSpan(offset, 12);
            BinaryPrimitives.WriteUInt16BigEndian(header, mins[1]);
            BinaryPrimitives.WriteUInt16BigEndian(header[2..], checked((ushort)columns));
            BinaryPrimitives.WriteUInt16BigEndian(header[4..], mins[2]);
            BinaryPrimitives.WriteUInt16BigEndian(header[6..], checked((ushort)depth));
            BinaryPrimitives.WriteUInt32BigEndian(header[8..], checked((uint)(row * columns * depth)));
            int cursor = offset + 12;
            for (int column = 0; column < columns; column += byte.MaxValue)
            {
                data[cursor++] = checked((byte)System.Math.Min(byte.MaxValue, columns - column));
                data[cursor++] = checked((byte)depth);
                data[cursor++] = 0;
            }
        }
        return new GfxLightGrid
        {
            SunPrimaryLightIndex = sunPrimaryLightIndex,
            Mins = mins.ToArray(), Maxs = maxs.ToArray(),
            RowAxis = GfxLightGridHorizontalAxis.X, ColAxis = GfxLightGridHorizontalAxis.Y,
            RowDataStart = starts, RawRowDataSize = checked((uint)data.Length), RawRowData = data,
            EntryCount = checked((uint)entries.Count), Entries = entries.ToArray(),
            ColorCount = checked((uint)colors.Count), Colors = colors.ToArray()
        };
    }

    public static GfxLightGridColors CreateDefault()
    {
        var rgbBytes = new byte[GfxLightGridColors.SerializedSize];
        for (int index = 0; index < SampleDirections.Count; index++)
        {
            Vec3 sample = SampleDirections[index];
            rgbBytes[index * 3] = PackDefaultLightGridColor(sample.X);
            rgbBytes[index * 3 + 1] = PackDefaultLightGridColor(sample.Y);
            rgbBytes[index * 3 + 2] = PackDefaultLightGridColor(sample.Z);
        }
        return new GfxLightGridColors(rgbBytes);
    }

    private static Vec3[] CreateSampleDirections()
    {
        // linker_pc evaluates these expressions with x87 precision around explicit float spills.
        // Double intermediates reproduce those stable bytes on every .NET target.
        const double gridStep = 0.6666666865348816;
        const double rotatedXFromX = 0.4714045226573944;
        const double rotatedYZFromX = -0.2357022613286972;
        const double rotatedYZFromY = 0.40824827551841736;
        const double rotatedFromZ = 0.3333333432674408;
        var directions = new Vec3[GfxLightGridColors.SerializedSize / 3];
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
                    directions[basisIndex] = new Vec3 { X = projectedX, Y = projectedY, Z = projectedZ };
                    basisIndex++;
                }
            }
        }

        if (basisIndex != directions.Length)
        {
            throw new InvalidDataException(
                $"Default light-grid generation produced {basisIndex} samples instead of {directions.Length}.");
        }

        return directions;
    }

    private static byte PackDefaultLightGridColor(float projected) =>
        (byte)(((double)projected * 0.5 + 0.5) * 255.0);
}
