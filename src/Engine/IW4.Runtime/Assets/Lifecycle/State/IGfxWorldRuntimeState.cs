using IW4.Game.Zone;

namespace IW4.Runtime.Assets.Lifecycle.State;

public interface IGfxWorldRuntimeState : IXAssetRuntimeStateService
{
    bool IsBspInUse { get; }

    XAssetPoolAddress? PendingTextureInitializationAddress { get; }

    GfxWorldTextureState? TextureState { get; }

    void SetBspInUse(bool isInUse);

    void MarkTextureInitializationPending(XAssetPoolAddress worldAddress);

    void PublishTextureState(GfxWorldTextureState textureState);

    void ClearTextureState(XAssetPoolAddress worldAddress);
}
