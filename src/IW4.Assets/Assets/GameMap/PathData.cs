using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.GameMap;

public sealed class PathData
{
    public const int SerializedSize = 0x28;

    public uint NodeCount { get; init; }
    public XPointer<PathNode[]> NodesPointer { get; init; }
    public IReadOnlyList<PathNode> Nodes { get; init; } = [];

    public XPointer<PathBaseNode[]> BaseNodesPointer { get; init; }
    public IReadOnlyList<PathBaseNode> BaseNodes { get; init; } = [];

    public uint ChainNodeCount { get; init; }
    public XPointer<ushort[]> ChainNodeForNodePointer { get; init; }
    public IReadOnlyList<ushort> ChainNodeForNode { get; init; } = [];

    public XPointer<ushort[]> NodeForChainNodePointer { get; init; }
    public IReadOnlyList<ushort> NodeForChainNode { get; init; } = [];

    public int VisBytes { get; init; }
    public XPointer<byte[]> PathVisPointer { get; init; }
    public IReadOnlyList<byte> PathVis { get; init; } = [];

    public int NodeTreeCount { get; init; }
    public XPointer<PathNodeTree[]> NodeTreePointer { get; init; }
    public IReadOnlyList<PathNodeTree> NodeTree { get; init; } = [];
}
