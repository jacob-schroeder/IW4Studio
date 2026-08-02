using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldVertexData
{
    public const int SerializedSize = 0x0C;

    public XPointer<byte[]> VerticesPointer { get; init; }
    // Materialized payload target written into VerticesPointer's destination
    // cell by Load_GfxWorldVertexData. This remains an XBlock address until
    // renderer post-load translates it into the +0x04/+0x08 RSX record.
    public XBlockAddress? VerticesAddress { get; init; }
    public IReadOnlyList<byte> PackedVertices { get; init; } = [];
    public int WorldVbHandle { get; init; }
    public int WorldVbOffset { get; init; }
}
