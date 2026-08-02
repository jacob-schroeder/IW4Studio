using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxCellTree
{
    public const int SerializedSize = 0x04;

    public XPointer<GfxAabbTree[]> AabbTreesPointer { get; init; }
    public IReadOnlyList<GfxAabbTree> AabbTrees { get; init; } = [];
}
