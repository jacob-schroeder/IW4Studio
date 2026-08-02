using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldDpvsPlanes
{
    public const int SerializedSize = 0x10;

    public int CellCount { get; init; }
    public XPointer<DpvsPlane[]> PlanesPointer { get; init; }
    public IReadOnlyList<DpvsPlane> Planes { get; init; } = [];
    public XPointer<ushort[]> NodesPointer { get; init; }
    public IReadOnlyList<ushort> Nodes { get; init; } = [];
    public XPointer<uint[]> SceneEntCellBitsPointer { get; init; }
    public IReadOnlyList<uint> SceneEntCellBits { get; init; } = [];
}
