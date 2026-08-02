using IW4.Assets.Assets.GfxMap;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Render.Assets;

public interface IMapRenderWorldTextureBindingSnapshotFactory
{
    MapRenderWorldTextureBindingSnapshot CaptureWorldRuntimeTextureBindings(
        GfxWorldAsset world,
        GfxWorldTextureState textureState);
}
