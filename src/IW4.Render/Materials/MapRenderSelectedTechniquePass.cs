using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Materials;

public readonly record struct MapRenderSelectedTechniquePass(
    int PassIndex,
    MaterialPassAsset Pass);
