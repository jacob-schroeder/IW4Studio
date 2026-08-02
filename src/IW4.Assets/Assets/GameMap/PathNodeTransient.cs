namespace IW4.Assets.Assets.GameMap;

/// <summary>
/// Search-only path-node state stored at pathnode_t +0x6C on PS3.
/// The final dword is the native nodeCost/linkIndex union.
/// </summary>
public sealed class PathNodeTransient
{
    public const int SerializedSize = 0x1C;

    public int SearchFrame { get; init; }                       // pathnode_t +0x6C
    public uint NextOpenRuntimePointer { get; init; }           // +0x70
    public uint PreviousOpenRuntimePointer { get; init; }       // +0x74
    public uint ParentRuntimePointer { get; init; }             // +0x78
    public float Cost { get; init; }                            // +0x7C
    public float Heuristic { get; init; }                       // +0x80
    public uint NodeCostOrLinkIndexBits { get; init; }          // +0x84 union

    public float NodeCost =>
        BitConverter.Int32BitsToSingle(unchecked((int)NodeCostOrLinkIndexBits));

    public int LinkIndex => unchecked((int)NodeCostOrLinkIndexBits);
}
