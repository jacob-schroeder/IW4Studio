using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;

namespace IW4.Render.Shaders;

public interface IMapRenderSelectedPassProgramProvider
{
    MapRenderSelectedPassProgramSources ResolveSources(
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        MapRenderSelectedTechniquePass selectedPass);
}
