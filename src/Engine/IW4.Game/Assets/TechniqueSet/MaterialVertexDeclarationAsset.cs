using IW4.Game.Assets;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.TechniqueSet;

public sealed class MaterialVertexDeclarationAsset
{
    public const int SerializedSize = 0x1c;
    public const int RoutingCount = 13;

    public byte StreamCount { get; init; }
    public byte HasOptionalSourceRaw { get; init; }
    public bool HasOptionalSource => HasOptionalSourceRaw != 0;
    public IReadOnlyList<MaterialVertexStreamRouting> Routing { get; init; } = [];
}
