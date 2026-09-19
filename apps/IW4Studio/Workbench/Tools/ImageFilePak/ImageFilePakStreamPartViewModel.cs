using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database.Streaming;

namespace IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;

/// <summary>
/// Read-only scalar projection of one authored GfxImage stream part and its
/// DB-header package entry.
/// </summary>
public sealed class ImageFilePakStreamPartViewModel
{
    internal ImageFilePakStreamPartViewModel(
        int partIndex,
        GfxImageStreamData streamData,
        DbHeaderImageStreamEntry streamEntry,
        int byteCount,
        string owningFastFileName)
    {
        PartText = $"Part {partIndex}";
        DimensionsText = $"{streamData.Width:N0} × {streamData.Height:N0}";
        LevelText = $"Level {streamData.LevelMarker}";
        ByteCountText = $"{byteCount:N0} bytes";
        PackageName = streamEntry.FileIndex == 0
            ? owningFastFileName
            : $"imagefile{streamEntry.FileIndex}.pak";
        SourceRangeText =
            $"0x{streamEntry.SourceStart:X8}–0x{streamEntry.SourceEnd:X8}";
        BlockOffsetText = $"0x{streamEntry.BlockOffset:X8}";
        StreamOffsetText = $"0x{streamEntry.StreamOffset:X8}";
        IsAvailable = !streamEntry.IsEmpty;
    }

    public string PartText { get; }

    public string DimensionsText { get; }

    public string LevelText { get; }

    public string ByteCountText { get; }

    public string PackageName { get; }

    public string SourceRangeText { get; }

    public string BlockOffsetText { get; }

    public string StreamOffsetText { get; }

    public bool IsAvailable { get; }
}
