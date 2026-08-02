using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed record MaterialTechniqueSlot(
    int Index,
    XPointer<MaterialTechniqueAsset> Pointer,
    MaterialTechniqueAsset? Technique);
