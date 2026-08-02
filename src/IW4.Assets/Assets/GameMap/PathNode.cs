namespace IW4.Assets.Assets.GameMap;

public sealed class PathNode
{
    public const int SerializedSize = 0x88;

    public int Offset { get; init; }
    public PathNodeConstant Constant { get; init; } = new();

    // 0x40..0x6B: mutable pathnode_dynamic_t state.
    public PathNodeDynamic Dynamic { get; init; } = new();

    // 0x6C..0x87: path-search pathnode_transient_t state.
    public PathNodeTransient Transient { get; init; } = new();
}
