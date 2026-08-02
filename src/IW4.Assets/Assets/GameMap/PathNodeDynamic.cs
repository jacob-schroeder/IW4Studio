namespace IW4.Assets.Assets.GameMap;

/// <summary>
/// Mutable path-node state stored at pathnode_t +0x40 on PS3.
/// </summary>
public sealed class PathNodeDynamic
{
    public const int SerializedSize = 0x2C;

    // PS3 stores a 1-based sentient handle in +0x40; zero means unowned.
    public ushort OwnerHandle { get; init; }                    // pathnode_t +0x40
    public ushort Pad42 { get; init; }                          // +0x42
    public int FreeTime { get; init; }                          // +0x44
    public IReadOnlyList<int> ValidTimes { get; init; } = [];   // +0x48, int[3]
    public IReadOnlyList<int> DangerousNodeTimes { get; init; } = []; // +0x54, int[3]
    public int InPlayerLosTime { get; init; }                   // +0x60
    public short LinkCount { get; init; }                       // +0x64
    public short OverlapCount { get; init; }                    // +0x66
    public short TurretEntityNumber { get; init; }              // +0x68
    public byte UserCount { get; init; }                        // +0x6A
    public bool HasBadPlaceLink { get; init; }                  // +0x6B, bool8
}
