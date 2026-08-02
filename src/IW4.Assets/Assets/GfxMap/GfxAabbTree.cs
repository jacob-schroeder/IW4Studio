using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxAabbTree
{
    public const int SerializedSize = 0x28;

    // 0x00: Bounds use midpoint[3] followed by nonnegative halfSize[3].
    public Bounds Bounds { get; init; } = new();

    // 0x18
    public ushort ChildCount { get; init; }

    // 0x1A
    public ushort SurfaceCount { get; init; }

    // 0x1C
    public ushort StartSurfIndex { get; init; }

    // 0x1E: Number of static-model indices addressed by +0x20.
    public ushort SModelIndexCount { get; init; }

    // 0x20: Direct ushort*. A packed pointer may select a contiguous slice of
    // an earlier inline index payload without consuming source bytes.
    public XPointer<ushort[]> SModelIndexesPointer { get; init; }
    public IReadOnlyList<ushort> SModelIndexes { get; init; } = [];

    // 0x24: Relative byte offset from this row to its first child row.
    public int ChildrenOffset { get; init; }
}
