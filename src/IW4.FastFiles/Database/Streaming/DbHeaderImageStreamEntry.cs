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
    public const uint MaximumNumberedFileIndex = 20;
    public const uint NamedFileIndex = uint.MaxValue;

    public uint SourceSize => SourceEnd - SourceStart;
    public uint StreamBlockBase => StreamOffset & 0xffff0000;
    public bool IsEmpty => SourceEnd == 0;

    public static bool IsValidPackageFileIndex(uint fileIndex) =>
        fileIndex is >= 1 and <= MaximumNumberedFileIndex or NamedFileIndex;

    public static string GetPackageFileName(uint fileIndex, string fastFilePath)
    {
        if (fileIndex != NamedFileIndex)
            return $"imagefile{fileIndex}.pak";

        string stem = Path.GetFileNameWithoutExtension(fastFilePath);
        if (stem.EndsWith("_load", StringComparison.Ordinal))
            stem = stem[..^5];
        return $"{stem}.pak";
    }
}
