using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class SrfTriangles
{
    public const int SerializedSize = 0x14;

    public int VertexLayerData { get; init; }
    public int BaseVertex { get; init; }
    public uint MinVertexIndex { get; init; }
    public ushort VertexCount { get; init; }
    public ushort TriCount { get; init; }
    public int BaseIndex { get; init; }
}
