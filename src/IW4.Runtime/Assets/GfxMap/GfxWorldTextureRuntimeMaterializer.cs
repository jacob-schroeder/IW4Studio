using IW4.Assets.Zone;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets.GfxMap;

internal static class GfxWorldTextureRuntimeMaterializer
{
    internal static (XAssetPoolAddress Address, GfxWorldAsset World) ResolveWorld(
        GfxWorldAsset world,
        XAssetPool assetPool,
        IXAssetSourceMemory blocks,
        GfxWorldRuntimeState runtimeState)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assetPool);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(runtimeState);

        if (!assetPool.TryGetEntry(world, out XAssetPoolEntry? entry) ||
            entry.AssetType != XAssetType.GfxMap ||
            entry.Asset is not GfxWorldAsset activeWorld)
        {
            throw new InvalidOperationException("GfxWorld texture work requires an active canonical GfxMap slot.");
        }
        if (entry.SourceBlocks is not null && !ReferenceEquals(entry.SourceBlocks, blocks))
        {
            throw new InvalidOperationException(
                $"GfxWorld '{entry.Name}' texture destinations belong to another zone's block streams.");
        }
        if (runtimeState.PendingTextureInitializationAddress is { } pending &&
            pending != entry.Address)
        {
            throw new InvalidOperationException(
                $"Pending GfxWorld texture initialization belongs to {pending}, not {entry.Address}.");
        }
        if (runtimeState.PendingTextureInitializationAddress is null &&
            runtimeState.TextureState is { } textureState &&
            textureState.WorldAddress != entry.Address)
        {
            throw new InvalidOperationException(
                $"Active GfxWorld texture state belongs to {textureState.WorldAddress}, not {entry.Address}.");
        }

        return (entry.Address, activeWorld);
    }

    internal static (XAssetPoolAddress Address, GfxImageAsset Image, GfxTexture Descriptor)
        ResolveImage(
            GfxImageAsset image,
            XAssetPool assetPool,
            string memberName)
    {
        if (!assetPool.TryGetEntry(image, out XAssetPoolEntry? entry) ||
            entry.AssetType != XAssetType.Image ||
            entry.Asset is not GfxImageAsset activeImage)
        {
            throw new InvalidDataException(
                $"{memberName} does not resolve to an active canonical Image slot.");
        }

        return (entry.Address, activeImage, GfxTextureCodec.FromImage(activeImage));
    }

    internal static (int ReflectionProbeCount, int LightmapCount) ValidateLayout(
        GfxWorldAsset world,
        IXAssetSourceMemory blocks)
    {
        GfxWorldDraw draw = world.WorldDraw;
        int reflectionProbeCount;
        try
        {
            reflectionProbeCount = checked((int)draw.ReflectionProbeCount);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' reflection-probe count 0x{draw.ReflectionProbeCount:X8} exceeds the managed PS3 range.",
                exception);
        }
        if (draw.LightmapCount < 0)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has negative lightmap count {draw.LightmapCount}.");
        }

        int lightmapCount = draw.LightmapCount;
        ValidateCount(world, "reflectionProbeImages", draw.ReflectionProbeImages.Count, reflectionProbeCount);
        ValidateCount(world, "reflectionProbeOrigins", draw.ReflectionProbeOrigins.Count, reflectionProbeCount);
        ValidateCount(world, "reflectionProbeTextures", draw.ReflectionProbeTextures.Count, reflectionProbeCount);
        ValidateCount(world, "lightmaps", draw.Lightmaps.Count, lightmapCount);
        ValidateCount(world, "lightmapPrimaryTextures", draw.LightmapPrimaryTextures.Count, lightmapCount);
        ValidateCount(world, "lightmapSecondaryTextures", draw.LightmapSecondaryTextures.Count, lightmapCount);

        ValidateTarget(
            world,
            blocks,
            "reflectionProbeTextures",
            draw.ReflectionProbeTexturesAddress,
            reflectionProbeCount);
        ValidateTarget(
            world,
            blocks,
            "lightmapPrimaryTextures",
            draw.LightmapPrimaryTexturesAddress,
            lightmapCount);
        ValidateTarget(
            world,
            blocks,
            "lightmapSecondaryTextures",
            draw.LightmapSecondaryTexturesAddress,
            lightmapCount);
        ValidateNonOverlappingTargets(world, draw, reflectionProbeCount, lightmapCount);

        return (reflectionProbeCount, lightmapCount);
    }

    internal static GfxWorldTextureRowState CreateRow(
        GfxWorldTextureKind kind,
        int ordinal,
        GfxImageAsset image,
        GfxWorldTextureSourceKind sourceKind,
        XAssetPool assetPool,
        string memberName)
    {
        (XAssetPoolAddress address, _, GfxTexture descriptor) = ResolveImage(
            image,
            assetPool,
            memberName);
        return new GfxWorldTextureRowState(
            kind,
            ordinal,
            descriptor,
            sourceKind,
            address);
    }

    internal static byte[] EncodeRows(
        IReadOnlyList<GfxWorldTextureRowState> rows)
    {
        var bytes = new byte[checked(rows.Count * GfxTexture.SerializedSize)];
        for (int index = 0; index < rows.Count; index++)
        {
            byte[] descriptorBytes = GfxTextureCodec.Encode(rows[index].Descriptor);
            descriptorBytes.CopyTo(bytes, index * GfxTexture.SerializedSize);
        }

        return bytes;
    }

    internal static byte[] ReadRows(
        IXAssetSourceMemory blocks,
        XBlockAddress? address,
        int rowCount) => rowCount == 0
            ? []
            : blocks.ReadBytes(address!.Value, checked(rowCount * GfxTexture.SerializedSize));

    internal static void WriteRows(
        IXAssetSourceMemory blocks,
        XBlockAddress? address,
        ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty)
            blocks.WriteBytes(address!.Value, bytes);
    }

    private static void ValidateCount(
        GfxWorldAsset world,
        string memberName,
        int actual,
        int expected)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {actual} {memberName} row(s), but its native count requires exactly {expected}.");
        }
    }

    private static void ValidateTarget(
        GfxWorldAsset world,
        IXAssetSourceMemory blocks,
        string memberName,
        XBlockAddress? address,
        int rowCount)
    {
        int byteCount = checked(rowCount * GfxTexture.SerializedSize);
        if (rowCount == 0)
            return;
        if (address is not { } target || target.BlockType != XFileBlockType.RUNTIME)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {rowCount} {memberName} row(s), but no valid RUNTIME destination.");
        }

        _ = blocks.ReadBytes(target, byteCount);
    }

    private static void ValidateNonOverlappingTargets(
        GfxWorldAsset world,
        GfxWorldDraw draw,
        int reflectionProbeCount,
        int lightmapCount)
    {
        (string Name, XBlockAddress? Address, int ByteCount)[] ranges =
        [
            ("reflectionProbeTextures", draw.ReflectionProbeTexturesAddress,
                checked(reflectionProbeCount * GfxTexture.SerializedSize)),
            ("lightmapPrimaryTextures", draw.LightmapPrimaryTexturesAddress,
                checked(lightmapCount * GfxTexture.SerializedSize)),
            ("lightmapSecondaryTextures", draw.LightmapSecondaryTexturesAddress,
                checked(lightmapCount * GfxTexture.SerializedSize))
        ];

        for (int firstIndex = 0; firstIndex < ranges.Length; firstIndex++)
        {
            (string firstName, XBlockAddress? firstAddress, int firstByteCount) = ranges[firstIndex];
            if (firstByteCount == 0)
                continue;
            long firstEnd = (long)firstAddress!.Value.Offset + firstByteCount;
            for (int secondIndex = firstIndex + 1; secondIndex < ranges.Length; secondIndex++)
            {
                (string secondName, XBlockAddress? secondAddress, int secondByteCount) = ranges[secondIndex];
                if (secondByteCount == 0 || firstAddress.Value.BlockType != secondAddress!.Value.BlockType)
                    continue;
                long secondEnd = (long)secondAddress.Value.Offset + secondByteCount;
                if (firstAddress.Value.Offset < secondEnd && secondAddress.Value.Offset < firstEnd)
                {
                    throw new InvalidDataException(
                        $"GfxWorld '{world.Name}' runtime texture ranges {firstName} and {secondName} overlap.");
                }
            }
        }
    }
}
