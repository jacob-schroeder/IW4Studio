using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

public interface ISelectedPassProgramProvider
{
    SelectedPassProgramSources ResolveSources(
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        int passIndex,
        MaterialPassAsset pass);
}
