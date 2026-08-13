using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Image;

public sealed record GfxImageStreamData(
    ushort Width,
    ushort Height,
    uint LevelSizeAndOffset)
{
    public const int SerializedSize = 0x08;
    public const int EntryCount = (int)GfxImageStreamPart.Count;

    public int LevelMarker => (int)(LevelSizeAndOffset >> 26);
    public int CumulativeByteCount => (int)(LevelSizeAndOffset & 0x03ffffff);
    public bool HasStreamingData => Width != 0 || Height != 0 || LevelSizeAndOffset != 0;

    public static int[] ValidateProfileAndComputePartByteCounts(
        IReadOnlyList<GfxImageStreamData> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != EntryCount)
        {
            throw new InvalidDataException(
                $"A GfxImage stream profile requires exactly {EntryCount} records.");
        }

        var byteCounts = new int[EntryCount];
        int previousCumulativeByteCount = 0;
        bool reachedEmptyTail = false;
        for (int index = 0; index < entries.Count; index++)
        {
            GfxImageStreamData entry = entries[index] ??
                throw new InvalidDataException(
                    $"GfxImage stream record {index} cannot be null.");
            if (!entry.HasStreamingData)
            {
                reachedEmptyTail = true;
                continue;
            }

            if (reachedEmptyTail)
            {
                throw new InvalidDataException(
                    $"GfxImage stream record {index} is active after the empty tail began.");
            }
            if (entry.Width == 0 || entry.Height == 0 || entry.CumulativeByteCount == 0)
            {
                throw new InvalidDataException(
                    $"Active GfxImage stream record {index} requires nonzero width, height, and cumulative byte count.");
            }
            if (entry.CumulativeByteCount <= previousCumulativeByteCount)
            {
                throw new InvalidDataException(
                    $"GfxImage stream record {index} does not strictly increase the cumulative byte count.");
            }

            byteCounts[index] = entry.CumulativeByteCount - previousCumulativeByteCount;
            previousCumulativeByteCount = entry.CumulativeByteCount;
        }

        return byteCounts;
    }
}
