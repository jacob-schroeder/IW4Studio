using IW4.Assets.Assets.GfxMap;
using IW4.Render.Textures;

namespace IW4.Render.Assets;

public interface IMapRenderWorldTextureBindingResolver
{
    MapRenderWorldTextureAssetBinding ResolveWorldRuntimeTexture(
        GfxWorldAsset world,
        MapRenderWorldRuntimeTextureIdentity identity);
}
