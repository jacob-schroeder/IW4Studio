using IW4.Game.Pointers;

namespace IW4.Game.Assets.GameMap;

public sealed class VehicleTrack
{
    public const int SerializedSize = 0x08;

    public XPointer<VehicleTrackSegment[]> SegmentsPointer { get; init; }
    public IReadOnlyList<VehicleTrackSegment> Segments { get; init; } = [];
    public int SegmentCount { get; init; }
}
