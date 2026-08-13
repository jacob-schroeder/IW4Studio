using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed record MaterialTechniqueSlot(
    MaterialTechniqueType Type,
    XPointer<MaterialTechniqueAsset> Pointer,
    MaterialTechniqueAsset? Technique)
{
    public int Index => (int)Type;
}
