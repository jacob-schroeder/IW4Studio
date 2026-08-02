using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.GameMap;

public sealed class VehicleTrackSegment
{
    public const int SerializedSize = 0x2C;

    public int Offset { get; init; }
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    public XPointer<VehicleTrackSector[]> SectorsPointer { get; init; }
    public IReadOnlyList<VehicleTrackSector> Sectors { get; init; } = [];
    public int SectorCount { get; init; }

    public XPointer<XPointer<VehicleTrackSegment>[]> NextBranchesPointer { get; init; } // 0x0C
    public IReadOnlyList<XPointer<VehicleTrackSegment>> NextBranchPointers { get; init; } = [];
    public IReadOnlyList<VehicleTrackSegment?> NextBranches { get; init; } = [];
    public int NextBranchCount { get; init; } // 0x10

    public XPointer<XPointer<VehicleTrackSegment>[]> PreviousBranchesPointer { get; init; } // 0x14
    public IReadOnlyList<XPointer<VehicleTrackSegment>> PreviousBranchPointers { get; init; } = [];
    public IReadOnlyList<VehicleTrackSegment?> PreviousBranches { get; init; } = [];
    public int PreviousBranchCount { get; init; } // 0x18

    public IReadOnlyList<float> EndEdgeDirection { get; init; } = []; // 0x1C, vec2
    public float EndEdgeDistance { get; init; }                     // 0x24
    public float TotalLength { get; init; }                         // 0x28
}
