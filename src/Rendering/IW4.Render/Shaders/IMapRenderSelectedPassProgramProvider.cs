using IW4.Game.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

public interface ISelectedPassProgramProvider
{
    SelectedPassProgramSources ResolveSources(
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        int passIndex,
        MaterialPassAsset pass);
}
