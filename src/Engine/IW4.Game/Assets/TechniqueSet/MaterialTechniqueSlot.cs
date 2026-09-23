using IW4.Game.Assets;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.TechniqueSet;

public sealed record MaterialTechniqueSlot(
    MaterialTechniqueType Type,
    XPointer<MaterialTechniqueAsset> Pointer,
    MaterialTechniqueAsset? Technique)
{
    public int Index => (int)Type;
}
