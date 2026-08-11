using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialVertexDeclarationAsset
{
    public const int SerializedSize = 0x1c;
    public const int RoutingCount = 13;

    public byte StreamCount { get; init; }
    public byte HasOptionalSource { get; init; }
    public IReadOnlyList<MaterialVertexStreamRouting> Routing { get; init; } = [];
}
