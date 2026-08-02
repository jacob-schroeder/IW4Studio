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
    public const int EntryCount = 4;

    public int LevelMarker => (int)(LevelSizeAndOffset >> 26);
    public int CumulativeByteCount => (int)(LevelSizeAndOffset & 0x03ffffff);
    public bool HasStreamingData => Width != 0 || Height != 0 || LevelSizeAndOffset != 0;
}
