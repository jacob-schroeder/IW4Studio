using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxPortal
{
    public const int SerializedSize = 0x3C;

    public bool IsQueued { get; init; }
    public bool IsAncestor { get; init; }
    public byte RecursionDepth { get; init; }
    public byte HullPointCount { get; init; }
    public int HullPointsRuntimePointer { get; init; }
    // 0x08: runtime GfxPortal*; copied as raw state, not an XFile child pointer.
    public int QueuedParentRuntimePointer { get; init; }
    public GfxPortalPlane Plane { get; init; } = new(0, 0, 0, 0);
    public XPointer<GfxPortalVertex[]> VerticesPointer { get; init; }
    public IReadOnlyList<GfxPortalVertex> Vertices { get; init; } = [];
    public ushort CellIndex { get; init; } // 0x20
    public byte VertexCount { get; init; }
    public byte Pad23 { get; init; }
    public IReadOnlyList<float> HullAxis { get; init; } = [];
}
