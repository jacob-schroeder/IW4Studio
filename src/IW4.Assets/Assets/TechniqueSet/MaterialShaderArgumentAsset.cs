using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed record MaterialShaderArgumentAsset(
    int Offset,
    MaterialShaderArgumentType Type,
    ushort Dest,
    int ArgumentRaw,
    MaterialShaderLiteralConstant? LiteralConstant,
    XPointerReference ArgumentPointer = default);
