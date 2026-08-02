using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle.State;

public sealed class GfxWorldRuntimeState : IGfxWorldRuntimeState
{
    public GfxWorldRuntimeState(bool isBspInUse = false)
    {
        IsBspInUse = isBspInUse;
    }

    public bool IsBspInUse { get; private set; }

    public XAssetPoolAddress? PendingTextureInitializationAddress { get; private set; }

    public GfxWorldTextureState? TextureState { get; private set; }

    public void SetBspInUse(bool isInUse) => IsBspInUse = isInUse;

    public void MarkTextureInitializationPending(XAssetPoolAddress worldAddress)
    {
        ValidateWorldAddress(worldAddress, nameof(worldAddress));
        PendingTextureInitializationAddress = worldAddress;
        if (TextureState?.WorldAddress != worldAddress)
            TextureState = null;
    }

    public void PublishTextureState(GfxWorldTextureState textureState)
    {
        ArgumentNullException.ThrowIfNull(textureState);
        if (PendingTextureInitializationAddress is { } pending &&
            pending != textureState.WorldAddress)
        {
            throw new InvalidOperationException(
                $"Pending GfxWorld texture initialization belongs to {pending}, not {textureState.WorldAddress}.");
        }

        TextureState = textureState;
        PendingTextureInitializationAddress = null;
    }

    public void ClearTextureState(XAssetPoolAddress worldAddress)
    {
        ValidateWorldAddress(worldAddress, nameof(worldAddress));
        if (PendingTextureInitializationAddress == worldAddress)
            PendingTextureInitializationAddress = null;
        if (TextureState?.WorldAddress == worldAddress)
            TextureState = null;
    }

    public IXAssetRuntimeStateSnapshot CaptureSnapshot() =>
        new GfxWorldRuntimeSnapshot(
            IsBspInUse,
            PendingTextureInitializationAddress,
            TextureState);

    public void RestoreSnapshot(IXAssetRuntimeStateSnapshot snapshot)
    {
        if (snapshot is not GfxWorldRuntimeSnapshot typed)
            throw new ArgumentException("Snapshot does not belong to GfxWorld runtime state.", nameof(snapshot));

        IsBspInUse = typed.IsBspInUse;
        PendingTextureInitializationAddress = typed.PendingTextureInitializationAddress;
        TextureState = typed.TextureState;
    }

    private static void ValidateWorldAddress(
        XAssetPoolAddress worldAddress,
        string parameterName)
    {
        if (worldAddress.AssetType != XAssetType.GfxMap)
            throw new ArgumentException("GfxWorld runtime state requires a GfxMap slot.", parameterName);
    }
}
