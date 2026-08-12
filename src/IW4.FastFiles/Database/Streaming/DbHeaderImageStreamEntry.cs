namespace IW4.FastFiles.Database.Streaming;

public readonly record struct DbHeaderImageStreamEntry(
    uint FileIndex,
    uint SourceStart,
    uint SourceEnd,
    uint BlockOffset,
    uint StreamOffset,
    int SerializedOffset)
{
    public const int SerializedSize = 0x14;

    public uint SourceSize => SourceEnd - SourceStart;
    public uint StreamBlockBase => StreamOffset & 0xffff0000;
    public bool IsEmpty => SourceEnd == 0;
}
