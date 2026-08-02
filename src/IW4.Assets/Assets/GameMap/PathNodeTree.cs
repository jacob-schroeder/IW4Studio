using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.GameMap;

public sealed class PathNodeTree
{
    public const int SerializedSize = 0x10;

    // Tree links can be forward packed references.  The loader therefore
    // allocates an identity before decoding its body and completes these
    // values once the source payload is reached.
    public int Offset { get; set; }
    public int Axis { get; set; }
    public float Distance { get; set; }

    // axis >= 0: the union contains two pathnode_tree_t* cells.
    public XPointer<PathNodeTree> Child0Pointer { get; set; }
    public PathNodeTree? Child0 { get; set; }
    public XPointer<PathNodeTree> Child1Pointer { get; set; }
    public PathNodeTree? Child1 { get; set; }

    // axis < 0: the same union contains a count and ushort node-index pointer.
    public int NodeCount { get; set; }
    public XPointer<ushort[]> NodesPointer { get; set; }
    public IReadOnlyList<ushort> Nodes { get; set; } = [];
}
