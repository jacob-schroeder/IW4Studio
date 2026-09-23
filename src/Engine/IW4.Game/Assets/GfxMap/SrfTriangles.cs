using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class SrfTriangles
{
    public const int SerializedSize = 0x14;
    // PS3 Event32 (0x4AA8 / 0x64C0): larger spans use original indices and
    // leave clipping to RSX. The SPU clipper only knows resting vertex positions.
    public const int SoftwareTriangleCullVertexLimit = 3831;

    public int VertexLayerData { get; init; }
    public int BaseVertex { get; init; }
    public uint MinVertexIndex { get; init; }
    public ushort VertexCount { get; init; }
    public ushort TriCount { get; init; }
    public int BaseIndex { get; init; }
}
