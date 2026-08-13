using IW4.Render.Techniques;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;

namespace IW4.Render.SceneBuilding;

internal sealed record SelectedColorPass(
    MaterialTextureDef Texture,
    GfxImageAsset Image,
    MaterialPassIdentity Pass,
    MaterialSamplerIdentity PrimarySampler,
    RenderState State,
    int UnresolvedCodeSamplerCount,
    MaterialStreamSource TexCoordSource,
    bool TexCoordSourceIsEngineRouted,
    bool AuthoredProgramExecutable);
