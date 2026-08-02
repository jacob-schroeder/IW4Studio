using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets.GfxMap;

/// <summary>
/// Initializes world texture descriptors at the renderer activation boundary,
/// deliberately separate from DB_PostLoadXZone.
/// </summary>
public static class GfxWorldTextureRuntimeInitializer
{
    public static GfxWorldTextureState EnsureInitialized(
        GfxWorldAsset world,
        XAssetPool assetPool,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState)
    {
        (XAssetPoolAddress worldAddress, GfxWorldAsset activeWorld) =
            GfxWorldTextureRuntimeMaterializer.ResolveWorld(
                world,
                assetPool,
                blocks,
                runtimeState);
        if (runtimeState.PendingTextureInitializationAddress is null &&
            runtimeState.TextureState is { } existing &&
            existing.WorldAddress == worldAddress)
        {
            return existing;
        }

        return R_LoadWorld(activeWorld, assetPool, blocks, runtimeState);
    }

    public static GfxWorldTextureState R_LoadWorld(
        GfxWorldAsset world,
        XAssetPool assetPool,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState)
    {
        (XAssetPoolAddress worldAddress, GfxWorldAsset activeWorld) =
            GfxWorldTextureRuntimeMaterializer.ResolveWorld(
                world,
                assetPool,
                blocks,
                runtimeState);
        GfxWorldDraw draw = activeWorld.WorldDraw;
        (int reflectionProbeCount, int lightmapCount) =
            GfxWorldTextureRuntimeMaterializer.ValidateLayout(activeWorld, blocks);

        var reflectionRows = new GfxWorldTextureRowState[reflectionProbeCount];
        for (int ordinal = 0; ordinal < reflectionRows.Length; ordinal++)
        {
            GfxImageAsset image = draw.ReflectionProbeImages[ordinal] ??
                throw new InvalidDataException(
                    $"GfxWorld '{activeWorld.Name}' reflectionProbeImages[{ordinal}] is null.");
            reflectionRows[ordinal] = GfxWorldTextureRuntimeMaterializer.CreateRow(
                GfxWorldTextureKind.ReflectionProbe,
                ordinal,
                image,
                GfxWorldTextureSourceKind.AuthoredImage,
                assetPool,
                $"GfxWorld '{activeWorld.Name}' reflectionProbeImages[{ordinal}]");
        }

        var primaryRows = new GfxWorldTextureRowState[lightmapCount];
        var secondaryRows = new GfxWorldTextureRowState[lightmapCount];
        for (int ordinal = 0; ordinal < lightmapCount; ordinal++)
        {
            GfxLightmapArray lightmap = draw.Lightmaps[ordinal];
            GfxImageAsset primary = lightmap.Primary ??
                throw new InvalidDataException(
                    $"GfxWorld '{activeWorld.Name}' lightmaps[{ordinal}].primary is null.");
            GfxImageAsset secondary = lightmap.Secondary ??
                throw new InvalidDataException(
                    $"GfxWorld '{activeWorld.Name}' lightmaps[{ordinal}].secondary is null.");
            primaryRows[ordinal] = GfxWorldTextureRuntimeMaterializer.CreateRow(
                GfxWorldTextureKind.PrimaryLightmap,
                ordinal,
                primary,
                GfxWorldTextureSourceKind.AuthoredImage,
                assetPool,
                $"GfxWorld '{activeWorld.Name}' lightmaps[{ordinal}].primary");
            secondaryRows[ordinal] = GfxWorldTextureRuntimeMaterializer.CreateRow(
                GfxWorldTextureKind.SecondaryLightmap,
                ordinal,
                secondary,
                GfxWorldTextureSourceKind.AuthoredImage,
                assetPool,
                $"GfxWorld '{activeWorld.Name}' lightmaps[{ordinal}].secondary");
        }

        long revision = checked((runtimeState.TextureState?.Revision ?? -1) + 1);
        var nextState = new GfxWorldTextureState(
            worldAddress,
            draw.ReflectionProbeTexturesAddress,
            draw.LightmapPrimaryTexturesAddress,
            draw.LightmapSecondaryTexturesAddress,
            reflectionRows,
            primaryRows,
            secondaryRows,
            primaryOverrideImageAddress: null,
            secondaryOverrideImageAddress: null,
            revision);
        Commit(activeWorld, blocks, runtimeState, nextState);
        return nextState;
    }

    private static void Commit(
        GfxWorldAsset world,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState,
        GfxWorldTextureState nextState)
    {
        GfxWorldDraw draw = world.WorldDraw;
        byte[] oldReflectionBytes = GfxWorldTextureRuntimeMaterializer.ReadRows(
            blocks,
            draw.ReflectionProbeTexturesAddress,
            draw.ReflectionProbeTextures.Count);
        byte[] oldPrimaryBytes = GfxWorldTextureRuntimeMaterializer.ReadRows(
            blocks,
            draw.LightmapPrimaryTexturesAddress,
            draw.LightmapPrimaryTextures.Count);
        byte[] oldSecondaryBytes = GfxWorldTextureRuntimeMaterializer.ReadRows(
            blocks,
            draw.LightmapSecondaryTexturesAddress,
            draw.LightmapSecondaryTextures.Count);
        IReadOnlyList<GfxTexture> oldReflectionRows = draw.ReflectionProbeTextures;
        IReadOnlyList<GfxTexture> oldPrimaryRows = draw.LightmapPrimaryTextures;
        IReadOnlyList<GfxTexture> oldSecondaryRows = draw.LightmapSecondaryTextures;
        IXAssetRuntimeStateSnapshot runtimeSnapshot = runtimeState.CaptureSnapshot();

        byte[] reflectionBytes = GfxWorldTextureRuntimeMaterializer.EncodeRows(
            nextState.ReflectionProbeRows);
        byte[] primaryBytes = GfxWorldTextureRuntimeMaterializer.EncodeRows(
            nextState.LightmapPrimaryRows);
        byte[] secondaryBytes = GfxWorldTextureRuntimeMaterializer.EncodeRows(
            nextState.LightmapSecondaryRows);
        try
        {
            // Native R_LoadWorld order: reflection, cache reset, primary,
            // secondary. Cache identities live in runtime side state here.
            GfxWorldTextureRuntimeMaterializer.WriteRows(
                blocks,
                draw.ReflectionProbeTexturesAddress,
                reflectionBytes);
            GfxWorldTextureRuntimeMaterializer.WriteRows(
                blocks,
                draw.LightmapPrimaryTexturesAddress,
                primaryBytes);
            GfxWorldTextureRuntimeMaterializer.WriteRows(
                blocks,
                draw.LightmapSecondaryTexturesAddress,
                secondaryBytes);
            draw.ApplyRuntimeTextures(
                nextState.ReflectionProbeRows.Select(row => row.Descriptor).ToArray(),
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
                    draw.ReflectionProbeTexturesAddress,
                    oldReflectionBytes);
                GfxWorldTextureRuntimeMaterializer.WriteRows(
                    blocks,
                    draw.LightmapPrimaryTexturesAddress,
                    oldPrimaryBytes);
                GfxWorldTextureRuntimeMaterializer.WriteRows(
                    blocks,
                    draw.LightmapSecondaryTexturesAddress,
                    oldSecondaryBytes);
                draw.ApplyRuntimeTextures(
                    oldReflectionRows,
                    oldPrimaryRows,
                    oldSecondaryRows);
                runtimeState.RestoreSnapshot(runtimeSnapshot);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "GfxWorld texture initialization failed and rollback did not complete.",
                    failure,
                    rollbackFailure);
            }

            throw;
        }
    }
}
