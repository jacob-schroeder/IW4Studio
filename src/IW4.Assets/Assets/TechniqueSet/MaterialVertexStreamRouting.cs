using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public readonly record struct MaterialVertexStreamRouting(
    MaterialStreamSource Source,
    MaterialStreamDestination Dest);
