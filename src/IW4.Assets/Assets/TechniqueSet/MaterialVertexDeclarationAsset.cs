using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialVertexDeclarationAsset
{
    public const int SerializedSize = 0x1c;
    public const int RoutingCount = 13;

    // Address of the copied 0x1c declaration in materialized block memory.
    // The PS3 stream setter compares this pointer identity, not route bytes.
    public XBlockAddress? DestinationAddress { get; init; }

    public byte StreamCount { get; init; }
    public byte HasOptionalSource { get; init; }
    public IReadOnlyList<MaterialVertexStreamRouting> Routing { get; init; } = [];
}
