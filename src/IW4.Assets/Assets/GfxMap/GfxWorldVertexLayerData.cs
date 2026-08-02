using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldVertexLayerData
{
    public const int SerializedSize = 0x0C;

    public XPointer<byte[]> DataPointer { get; init; }
    // Materialized PHYSICAL payload target consumed by the PS3 local-memory
    // record helper before Event20 binds the layer stream.
    public XBlockAddress? DataAddress { get; init; }
    public IReadOnlyList<byte> PackedLayerData { get; init; } = [];
    public int LayerVbHandle { get; init; }
    public int LayerVbOffset { get; init; }
}
