using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets.GfxMap;

/// <summary>
/// Compares cached GfxImage identities, synchronizes once on change, then
/// rebuilds primary and secondary lightmap descriptor arrays in that order.
/// </summary>
public static class GfxWorldLightmapTextureRefreshProcessor
{
    public static GfxWorldLightmapTextureRefreshResult R_UpdateFrameLightmapTextures(
        GfxWorldAsset world,
        XAssetPool assetPool,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState,
        GfxWorldLightmapTextureOverrideSelection selection,
        IGfxWorldRenderThreadSynchronizer synchronizer)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(synchronizer);
        (XAssetPoolAddress worldAddress, GfxWorldAsset activeWorld) =
            GfxWorldTextureRuntimeMaterializer.ResolveWorld(
                world,
                assetPool,
                blocks,
                runtimeState);
        GfxWorldTextureState currentState = runtimeState.TextureState ??
            throw new InvalidOperationException(
                "R_UpdateFrameLightmapTextures requires prior R_LoadWorld texture initialization.");
        if (currentState.WorldAddress != worldAddress)
        {
            throw new InvalidOperationException(
                $"Active texture state belongs to {currentState.WorldAddress}, not {worldAddress}.");
        }

        XAssetPoolAddress? desiredPrimaryAddress = GetImageIdentity(
            selection.Primary,
            "primary lightmap override");
        XAssetPoolAddress? desiredSecondaryAddress = GetImageIdentity(
            selection.Secondary,
            "secondary lightmap override");
        if (currentState.PrimaryOverrideImageAddress == desiredPrimaryAddress &&
            currentState.SecondaryOverrideImageAddress == desiredSecondaryAddress)
        {
            // Pointer identity is the complete native cache key. Mutating an
            // image header behind the same pointer does not trigger refresh.
            return GfxWorldLightmapTextureRefreshResult.Unchanged;
        }

        (_, int lightmapCount) =
            GfxWorldTextureRuntimeMaterializer.ValidateLayout(activeWorld, blocks);
        GfxWorldDraw draw = activeWorld.WorldDraw;
        ValidateSources(
            activeWorld,
            draw,
            assetPool,
            GfxWorldTextureKind.PrimaryLightmap,
            lightmapCount,
            selection.Primary);
        ValidateSources(
            activeWorld,
            draw,
            assetPool,
            GfxWorldTextureKind.SecondaryLightmap,
            lightmapCount,
            selection.Secondary);

        // Synchronize once after both identity comparisons and before reading
        // either cache cell or descriptor source.
        synchronizer.R_SyncRenderThread();

        GfxWorldTextureRowState[] primaryRows = BuildRows(
            activeWorld,
            draw,
            assetPool,
            GfxWorldTextureKind.PrimaryLightmap,
            lightmapCount,
            selection.Primary);
        GfxWorldTextureRowState[] secondaryRows = BuildRows(
            activeWorld,
            draw,
            assetPool,
            GfxWorldTextureKind.SecondaryLightmap,
            lightmapCount,
            selection.Secondary);

        var nextState = new GfxWorldTextureState(
            worldAddress,
            currentState.ReflectionProbeTexturesAddress,
            currentState.LightmapPrimaryTexturesAddress,
            currentState.LightmapSecondaryTexturesAddress,
            currentState.ReflectionProbeRows,
            primaryRows,
            secondaryRows,
            desiredPrimaryAddress,
            desiredSecondaryAddress,
            checked(currentState.Revision + 1));

        Commit(activeWorld, blocks, runtimeState, nextState);
        return GfxWorldLightmapTextureRefreshResult.Updated;
    }

    private static void ValidateSources(
        GfxWorldAsset world,
        GfxWorldDraw draw,
        XAssetPool assetPool,
        GfxWorldTextureKind kind,
        int lightmapCount,
        GfxImageAsset? overrideImage)
    {
        if (lightmapCount == 0)
            return;
        if (overrideImage is not null)
        {
            _ = GfxWorldTextureRuntimeMaterializer.ResolveImage(
                overrideImage,
                assetPool,
                $"GfxWorld '{world.Name}' {kind} override");
            return;
        }

        for (int ordinal = 0; ordinal < lightmapCount; ordinal++)
        {
            GfxImageAsset image = GetAuthoredImage(world, draw, kind, ordinal);
            _ = GfxWorldTextureRuntimeMaterializer.ResolveImage(
                image,
                assetPool,
                $"GfxWorld '{world.Name}' lightmaps[{ordinal}].{kind}");
        }
    }

    private static GfxWorldTextureRowState[] BuildRows(
        GfxWorldAsset world,
        GfxWorldDraw draw,
        XAssetPool assetPool,
        GfxWorldTextureKind kind,
        int lightmapCount,
        GfxImageAsset? overrideImage)
    {
        var rows = new GfxWorldTextureRowState[lightmapCount];
        if (overrideImage is not null)
        {
            // r7 remains fixed in both PS3 override loops. One GfxImage
            // descriptor is broadcast into every destination row.
            (XAssetPoolAddress address, _, GfxTexture descriptor) =
                GfxWorldTextureRuntimeMaterializer.ResolveImage(
                    overrideImage,
                    assetPool,
                    $"GfxWorld '{world.Name}' {kind} override");
            for (int ordinal = 0; ordinal < rows.Length; ordinal++)
            {
                rows[ordinal] = new GfxWorldTextureRowState(
                    kind,
                    ordinal,
                    descriptor,
                    GfxWorldTextureSourceKind.OverrideImage,
                    address);
            }

            return rows;
        }

        for (int ordinal = 0; ordinal < rows.Length; ordinal++)
        {
            GfxImageAsset image = GetAuthoredImage(world, draw, kind, ordinal);
            rows[ordinal] = GfxWorldTextureRuntimeMaterializer.CreateRow(
                kind,
                ordinal,
                image,
                GfxWorldTextureSourceKind.AuthoredImage,
                assetPool,
                $"GfxWorld '{world.Name}' lightmaps[{ordinal}].{kind}");
        }

        return rows;
    }

    private static GfxImageAsset GetAuthoredImage(
        GfxWorldAsset world,
        GfxWorldDraw draw,
        GfxWorldTextureKind kind,
        int ordinal)
    {
        GfxLightmapArray lightmap = draw.Lightmaps[ordinal];
        return kind switch
        {
            GfxWorldTextureKind.PrimaryLightmap => lightmap.Primary,
            GfxWorldTextureKind.SecondaryLightmap => lightmap.Secondary,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        } ?? throw new InvalidDataException(
            $"GfxWorld '{world.Name}' lightmaps[{ordinal}].{kind} is null.");
    }

    private static XAssetPoolAddress? GetImageIdentity(
        GfxImageAsset? image,
        string memberName)
    {
        if (image is null)
            return null;
        if (image.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != XAssetType.Image)
        {
            throw new InvalidDataException(
                $"The {memberName} has no canonical Image pointer identity.");
        }

        return address;
    }

    private static void Commit(
        GfxWorldAsset world,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState,
        GfxWorldTextureState nextState)
    {
        GfxWorldDraw draw = world.WorldDraw;
        byte[] oldPrimaryBytes = GfxWorldTextureRuntimeMaterializer.ReadRows(
            blocks,
            draw.LightmapPrimaryTexturesAddress,
            draw.LightmapPrimaryTextures.Count);
        byte[] oldSecondaryBytes = GfxWorldTextureRuntimeMaterializer.ReadRows(
            blocks,
            draw.LightmapSecondaryTexturesAddress,
            draw.LightmapSecondaryTextures.Count);
        IReadOnlyList<GfxTexture> oldPrimaryRows = draw.LightmapPrimaryTextures;
        IReadOnlyList<GfxTexture> oldSecondaryRows = draw.LightmapSecondaryTextures;
        IXAssetRuntimeStateSnapshot runtimeSnapshot = runtimeState.CaptureSnapshot();
        byte[] primaryBytes = GfxWorldTextureRuntimeMaterializer.EncodeRows(
            nextState.LightmapPrimaryRows);
        byte[] secondaryBytes = GfxWorldTextureRuntimeMaterializer.EncodeRows(
            nextState.LightmapSecondaryRows);

        try
        {
            // Native loop order is primary first, then secondary.
            GfxWorldTextureRuntimeMaterializer.WriteRows(
                blocks,
                draw.LightmapPrimaryTexturesAddress,
                primaryBytes);
            GfxWorldTextureRuntimeMaterializer.WriteRows(
                blocks,
                draw.LightmapSecondaryTexturesAddress,
                secondaryBytes);
            draw.ApplyRuntimeTextures(
                draw.ReflectionProbeTextures,
                nextState.LightmapPrimaryRows.Select(row => row.Descriptor).ToArray(),
                nextState.LightmapSecondaryRows.Select(row => row.Descriptor).ToArray());
            runtimeState.PublishTextureState(nextState);
        }
        catch (Exception failure)
        {
            try
            {
                GfxWorldTextureRuntimeMaterializer.WriteRows(
                    blocks,
                    draw.LightmapPrimaryTexturesAddress,
                    oldPrimaryBytes);
                GfxWorldTextureRuntimeMaterializer.WriteRows(
                    blocks,
                    draw.LightmapSecondaryTexturesAddress,
                    oldSecondaryBytes);
                draw.ApplyRuntimeTextures(
                    draw.ReflectionProbeTextures,
                    oldPrimaryRows,
                    oldSecondaryRows);
                runtimeState.RestoreSnapshot(runtimeSnapshot);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "GfxWorld lightmap texture refresh failed and rollback did not complete.",
                    failure,
                    rollbackFailure);
            }

            throw;
        }
    }
}
