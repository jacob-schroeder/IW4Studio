using IW4.Game.Zone;

namespace IW4.Runtime.Assets.Lifecycle.State;

internal sealed record GfxWorldRuntimeSnapshot(
    bool IsBspInUse,
    XAssetPoolAddress? PendingTextureInitializationAddress,
    GfxWorldTextureState? TextureState)
    : IXAssetRuntimeStateSnapshot;
