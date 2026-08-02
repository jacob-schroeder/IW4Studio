using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.GameMap;

public sealed class VehicleTrack
{
    public const int SerializedSize = 0x08;

    public XPointer<VehicleTrackSegment[]> SegmentsPointer { get; init; }
    public IReadOnlyList<VehicleTrackSegment> Segments { get; init; } = [];
    public int SegmentCount { get; init; }
}
