using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.GameMap;

public sealed class VehicleTrackSector
{
    public const int SerializedSize = 0x3C;

    public int Offset { get; init; }
    public IReadOnlyList<float> StartEdgeDirection { get; init; } = []; // 0x00, vec2
    public float StartEdgeDistance { get; init; }                       // 0x08
    public IReadOnlyList<float> LeftEdgeDirection { get; init; } = [];  // 0x0C, vec2
    public float LeftEdgeDistance { get; init; }                        // 0x14
    public IReadOnlyList<float> RightEdgeDirection { get; init; } = []; // 0x18, vec2
    public float RightEdgeDistance { get; init; }                       // 0x20
    public float SectorLength { get; init; }                            // 0x24
    public float SectorWidth { get; init; }                             // 0x28
    public float TotalPriorLength { get; init; }                        // 0x2C
    public float TotalFollowingLength { get; init; }                    // 0x30
    public XPointer<VehicleTrackObstacle[]> ObstaclesPointer { get; init; } // 0x34
    public IReadOnlyList<VehicleTrackObstacle> Obstacles { get; init; } = [];
    public int ObstacleCount { get; init; }                             // 0x38
}
