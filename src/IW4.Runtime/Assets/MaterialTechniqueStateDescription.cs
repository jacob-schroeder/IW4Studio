namespace IW4.Runtime.Assets;

internal sealed record MaterialTechniqueStateDescription(
    int TechniqueSlot,
    int PassIndex,
    uint Hash,
    IReadOnlyList<ushort> CodePixelConstantIndices,
    IReadOnlyList<ResolvedConstant> PixelConstants);
