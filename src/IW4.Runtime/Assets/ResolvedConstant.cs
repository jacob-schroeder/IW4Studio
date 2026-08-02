using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Runtime.Assets;

internal readonly record struct ResolvedConstant(
    ushort Destination,
    MaterialShaderLiteralConstant Value);
