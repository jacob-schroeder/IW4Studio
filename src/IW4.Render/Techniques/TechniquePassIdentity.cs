using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Techniques;

public sealed record TechniquePassIdentity(
    string TechniqueSetName,
    int TechniqueSlot,
    string TechniqueName,
    string PassClass,
    int PassIndex,
    MaterialCustomSamplerFlags CustomSamplerFlags);
