using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle.State;

internal sealed record GfxWorldRuntimeSnapshot(
    bool IsBspInUse,
    XAssetPoolAddress? PendingTextureInitializationAddress,
    GfxWorldTextureState? TextureState)
    : IXAssetRuntimeStateSnapshot;
