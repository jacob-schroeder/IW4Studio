using IW4.Game.Assets.TechniqueSet;

namespace IW4.Runtime.Assets;

internal readonly record struct ResolvedConstant(
    ushort Destination,
    MaterialShaderLiteralConstant Value);
