using System.Buffers.Binary;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets.GfxMap;

/// <summary>
/// Sorts materialized 0x1C-byte world surfaces with their paired 0x20-byte
/// surface-bounds rows, remap sortedSurfIndex, then rebuild surfaceMaterials and
/// surfaceCastsSunShadow in runtime-slot order.
/// </summary>
public static class GfxWorldSurfacePostLoadProcessor
{
    private const int MaterialPointerOffset = 0x14;
    private const int MaterialSortedIndexShift = 30;
    private const int MaterialSortedIndexMask = 0x1fff;
    private const ulong SurfaceMaterialDrawSurfMask = 0xffc03fffffffffffUL;
    private const ulong CastsSunShadowMaterialMask = 0x000000003e000000UL;

    public static void Process(
        GfxWorldAsset world,
        XAssetPool assetPool,
        IXAssetSourceMemory blocks)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assetPool);
        ArgumentNullException.ThrowIfNull(blocks);

        GfxSurface[] sourceSurfaces = world.Dpvs.Surfaces.ToArray();
        int surfaceCount = sourceSurfaces.Length;
        if (surfaceCount > ushort.MaxValue + 1)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {surfaceCount} surfaces, but its PS3 sorted-surface references are UInt16.");
        }

        if (surfaceCount != 0 && world.Models.Count == 0)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {surfaceCount} surfaces but no brush model zero to supply the PS3 sort prefix.");
        }
        int sortSurfaceCount = surfaceCount == 0
            ? 0
            : world.Models[0].SurfaceCount;
        if ((uint)sortSurfaceCount > (uint)surfaceCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' brush model zero requests a {sortSurfaceCount}-surface sort prefix, " +
                $"but only {surfaceCount} materialized surfaces exist.");
        }
        ValidateHeaderAndCollectionCounts(
            world,
            surfaceCount,
            sortSurfaceCount);
        int[] authoredSurfaceIndexByCurrentSlot = GetAuthoredSurfaceIndexes(
            world,
            surfaceCount);

        MaterialAsset[] sourceMaterials = new MaterialAsset[surfaceCount];
        for (int sourceIndex = 0; sourceIndex < surfaceCount; sourceIndex++)
        {
            sourceMaterials[sourceIndex] = ResolveSurfaceMaterial(
                world,
                sourceSurfaces[sourceIndex],
                sourceIndex,
                assetPool);
        }

        // Comparator-equal runs remain indivisible, producing a stable
        // lexicographic order.
        int[] sourceIndexByRuntimeSlot = Enumerable.Range(0, sortSurfaceCount)
            .OrderBy(index => sourceMaterials[index].Info.SortKey)
            .ThenBy(index => sourceSurfaces[index].PrimaryLightIndex)
            .ThenBy(index => GetMaterialSortedIndex(sourceMaterials[index]))
            .ThenBy(index => index)
            .Concat(Enumerable.Range(
                sortSurfaceCount,
                surfaceCount - sortSurfaceCount))
            .ToArray();
        int[] runtimeSlotBySourceIndex = new int[surfaceCount];
        for (int runtimeSlot = 0; runtimeSlot < surfaceCount; runtimeSlot++)
            runtimeSlotBySourceIndex[sourceIndexByRuntimeSlot[runtimeSlot]] = runtimeSlot;

        XBlockAddress? surfacesAddress = RequireAddress(
            world.Dpvs.SurfacesAddress,
            surfaceCount,
            world,
            "surfaces");
        XBlockAddress? sortedSurfIndexAddress = RequireAddress(
            world.Dpvs.SortedSurfIndexAddress,
            world.Dpvs.SortedSurfIndex.Count,
            world,
            "sortedSurfIndex");
        XBlockAddress? surfaceBoundsAddress = RequireAddress(
            world.Dpvs.SurfaceBoundsAddress,
            world.Dpvs.SurfaceBounds.Count,
            world,
            "surfaceBounds");
        XBlockAddress? surfaceMaterialsAddress = RequireAddress(
            world.Dpvs.SurfaceMaterialsAddress,
            surfaceCount,
            world,
            "surfaceMaterials");
        XBlockAddress? surfaceCastsSunShadowAddress = RequireAddress(
            world.Dpvs.SurfaceCastsSunShadowAddress,
            world.Dpvs.SurfaceCastsSunShadow.Count,
            world,
            "surfaceCastsSunShadow");

        ValidateNonOverlappingPhysicalRanges(
            world,
            [
                ("surfaces", surfacesAddress, checked(surfaceCount * GfxSurface.SerializedSize)),
                ("sortedSurfIndex", sortedSurfIndexAddress, checked(world.Dpvs.SortedSurfIndex.Count * sizeof(ushort))),
                ("surfaceBounds", surfaceBoundsAddress, checked(surfaceCount * GfxSurfaceBounds.SerializedSize)),
                ("surfaceMaterials", surfaceMaterialsAddress, checked(surfaceCount * GfxMapDrawSurf.SerializedSize)),
                ("surfaceCastsSunShadow", surfaceCastsSunShadowAddress, checked(world.Dpvs.SurfaceCastsSunShadow.Count * sizeof(uint)))
            ]);

        byte[] sourceSurfaceBytes = ReadSnapshot(
            blocks,
            surfacesAddress,
            checked(surfaceCount * GfxSurface.SerializedSize));
        byte[] sourceSortedSurfIndexBytes = ReadSnapshot(
            blocks,
            sortedSurfIndexAddress,
            checked(world.Dpvs.SortedSurfIndex.Count * sizeof(ushort)));
        byte[] sourceSurfaceBoundsBytes = ReadSnapshot(
            blocks,
            surfaceBoundsAddress,
            checked(surfaceCount * GfxSurfaceBounds.SerializedSize));
        byte[] sourceSurfaceMaterialBytes = ReadSnapshot(
            blocks,
            surfaceMaterialsAddress,
            checked(surfaceCount * GfxMapDrawSurf.SerializedSize));
        byte[] sourceSurfaceCastsSunShadowBytes = ReadSnapshot(
            blocks,
            surfaceCastsSunShadowAddress,
            checked(world.Dpvs.SurfaceCastsSunShadow.Count * sizeof(uint)));

        var sortedSurfaces = new GfxSurface[surfaceCount];
        var sortedSurfaceBounds = new GfxSurfaceBounds[surfaceCount];
        var sortedMaterials = new MaterialAsset[surfaceCount];
        var authoredSurfaceIndexByRuntimeSlot = new int[surfaceCount];
        var sortedSurfaceBytes = new byte[sourceSurfaceBytes.Length];
        var sortedSurfaceBoundsBytes = new byte[sourceSurfaceBoundsBytes.Length];
        for (int runtimeSlot = 0; runtimeSlot < surfaceCount; runtimeSlot++)
        {
            int sourceIndex = sourceIndexByRuntimeSlot[runtimeSlot];
            GfxSurface source = sourceSurfaces[sourceIndex];
            XBlockAddress pointerCellAddress = surfacesAddress!.Value.Add(
                checked(runtimeSlot * GfxSurface.SerializedSize + MaterialPointerOffset));
            sortedSurfaces[runtimeSlot] = RebaseSurface(
                source,
                sourceMaterials[sourceIndex],
                pointerCellAddress);
            sortedSurfaceBounds[runtimeSlot] = world.Dpvs.SurfaceBounds[sourceIndex];
            sortedMaterials[runtimeSlot] = sourceMaterials[sourceIndex];
            authoredSurfaceIndexByRuntimeSlot[runtimeSlot] =
                authoredSurfaceIndexByCurrentSlot[sourceIndex];
            sourceSurfaceBytes.AsSpan(
                    sourceIndex * GfxSurface.SerializedSize,
                    GfxSurface.SerializedSize)
                .CopyTo(sortedSurfaceBytes.AsSpan(
                    runtimeSlot * GfxSurface.SerializedSize,
                    GfxSurface.SerializedSize));
            sourceSurfaceBoundsBytes.AsSpan(
                    sourceIndex * GfxSurfaceBounds.SerializedSize,
                    GfxSurfaceBounds.SerializedSize)
                .CopyTo(sortedSurfaceBoundsBytes.AsSpan(
                    runtimeSlot * GfxSurfaceBounds.SerializedSize,
                    GfxSurfaceBounds.SerializedSize));
        }

        ushort[] remappedSortedSurfIndex = RemapSortedSurfaceIndexes(
            world,
            runtimeSlotBySourceIndex,
            sourceSortedSurfIndexBytes);
        byte[] remappedSortedSurfIndexBytes = EncodeUInt16(remappedSortedSurfIndex);

        var surfaceMaterials = new GfxMapDrawSurf[surfaceCount];
        var surfaceMaterialBytes = new byte[checked(surfaceCount * GfxMapDrawSurf.SerializedSize)];
        for (int runtimeSlot = 0; runtimeSlot < surfaceCount; runtimeSlot++)
        {
            ulong packed =
                (sortedMaterials[runtimeSlot].Info.DrawSurf.Packed & SurfaceMaterialDrawSurfMask) |
                ((ulong)sortedSurfaces[runtimeSlot].PrimaryLightIndex << 46);
            surfaceMaterials[runtimeSlot] = new GfxMapDrawSurf(packed);
            BinaryPrimitives.WriteUInt64BigEndian(
                surfaceMaterialBytes.AsSpan(
                    runtimeSlot * GfxMapDrawSurf.SerializedSize,
                    GfxMapDrawSurf.SerializedSize),
                packed);
        }

        uint[] surfaceCastsSunShadow = RebuildSurfaceCastsSunShadow(
            world,
            sortedSurfaces,
            sortedMaterials,
            sortSurfaceCount);
        byte[] surfaceCastsSunShadowBytes = EncodeUInt32(surfaceCastsSunShadow);

        try
        {
            Write(blocks, surfacesAddress, sortedSurfaceBytes);
            Write(blocks, sortedSurfIndexAddress, remappedSortedSurfIndexBytes);
            Write(blocks, surfaceBoundsAddress, sortedSurfaceBoundsBytes);
            Write(blocks, surfaceMaterialsAddress, surfaceMaterialBytes);
            Write(blocks, surfaceCastsSunShadowAddress, surfaceCastsSunShadowBytes);
        }
        catch
        {
            Write(blocks, surfacesAddress, sourceSurfaceBytes);
            Write(blocks, sortedSurfIndexAddress, sourceSortedSurfIndexBytes);
            Write(blocks, surfaceBoundsAddress, sourceSurfaceBoundsBytes);
            Write(blocks, surfaceMaterialsAddress, sourceSurfaceMaterialBytes);
            Write(blocks, surfaceCastsSunShadowAddress, sourceSurfaceCastsSunShadowBytes);
            throw;
        }

        world.Dpvs.Surfaces = Array.AsReadOnly(sortedSurfaces);
        world.Dpvs.SurfaceBounds = Array.AsReadOnly(sortedSurfaceBounds);
        world.Dpvs.AuthoredSurfaceIndexByRuntimeSlot =
            Array.AsReadOnly(authoredSurfaceIndexByRuntimeSlot);
        world.Dpvs.SortedSurfIndex = Array.AsReadOnly(remappedSortedSurfIndex);
        world.Dpvs.SurfaceMaterials = Array.AsReadOnly(surfaceMaterials);
        world.Dpvs.SurfaceCastsSunShadow = Array.AsReadOnly(surfaceCastsSunShadow);

        // No additional platform-specific surface-bounds transformation is
        // applied here.
    }

    private static ushort[] RemapSortedSurfaceIndexes(
        GfxWorldAsset world,
        IReadOnlyList<int> runtimeSlotBySourceIndex,
        ReadOnlySpan<byte> materializedBytes)
    {
        IReadOnlyList<ushort> source = world.Dpvs.SortedSurfIndex;
        if (materializedBytes.Length != source.Count * sizeof(ushort))
            throw new InvalidDataException("Materialized sortedSurfIndex byte count is inconsistent.");

        var remapped = new ushort[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            ushort sourceSurfaceIndex = source[index];
            ushort materializedSourceIndex = BinaryPrimitives.ReadUInt16BigEndian(
                materializedBytes.Slice(index * sizeof(ushort), sizeof(ushort)));
            if (materializedSourceIndex != sourceSurfaceIndex)
            {
                throw new InvalidDataException(
                    $"GfxWorld '{world.Name}' sortedSurfIndex[{index}] semantic value {sourceSurfaceIndex} " +
                    $"does not match materialized value {materializedSourceIndex}.");
            }
            if (sourceSurfaceIndex >= runtimeSlotBySourceIndex.Count)
            {
                throw new InvalidDataException(
                    $"GfxWorld '{world.Name}' sortedSurfIndex[{index}] references surface {sourceSurfaceIndex}, " +
                    $"but only {runtimeSlotBySourceIndex.Count} surfaces exist.");
            }

            remapped[index] = checked((ushort)runtimeSlotBySourceIndex[sourceSurfaceIndex]);
        }
        return remapped;
    }

    private static int[] GetAuthoredSurfaceIndexes(
        GfxWorldAsset world,
        int surfaceCount)
    {
        if (world.Dpvs.AuthoredSurfaceIndexByRuntimeSlot.Count == 0)
            return Enumerable.Range(0, surfaceCount).ToArray();
        if (world.Dpvs.AuthoredSurfaceIndexByRuntimeSlot.Count != surfaceCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' retains {world.Dpvs.AuthoredSurfaceIndexByRuntimeSlot.Count} " +
                $"authored surface indexes for {surfaceCount} runtime slots.");
        }

        int[] indexes = world.Dpvs.AuthoredSurfaceIndexByRuntimeSlot.ToArray();
        var seen = new bool[surfaceCount];
        for (int runtimeSlot = 0; runtimeSlot < indexes.Length; runtimeSlot++)
        {
            int authoredIndex = indexes[runtimeSlot];
            if ((uint)authoredIndex >= (uint)surfaceCount || seen[authoredIndex])
            {
                throw new InvalidDataException(
                    $"GfxWorld '{world.Name}' has invalid authored surface index {authoredIndex} " +
                    $"at runtime slot {runtimeSlot}.");
            }
            seen[authoredIndex] = true;
        }
        return indexes;
    }

    private static uint[] RebuildSurfaceCastsSunShadow(
        GfxWorldAsset world,
        IReadOnlyList<GfxSurface> surfaces,
        IReadOnlyList<MaterialAsset> materials,
        int sortSurfaceCount)
    {
        int requiredWordCount = checked((sortSurfaceCount + 31) / 32);
        if (world.Dpvs.SurfaceCastsSunShadow.Count < requiredWordCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' surfaceCastsSunShadow has {world.Dpvs.SurfaceCastsSunShadow.Count} " +
                $"words, but {requiredWordCount} are required for the {sortSurfaceCount}-surface PS3 sort prefix.");
        }

        uint[] words = world.Dpvs.SurfaceCastsSunShadow.ToArray();
        Array.Clear(words, 0, requiredWordCount);
        for (int surfaceIndex = 0; surfaceIndex < sortSurfaceCount; surfaceIndex++)
        {
            if ((materials[surfaceIndex].Info.DrawSurf.Packed & CastsSunShadowMaterialMask) == 0 ||
                (surfaces[surfaceIndex].CastsSunShadow & 1) == 0)
            {
                continue;
            }

            words[surfaceIndex >> 5] |= 1u << (surfaceIndex & 31);
        }
        return words;
    }

    private static GfxSurface RebaseSurface(
        GfxSurface source,
        MaterialAsset activeMaterial,
        XBlockAddress materialPointerCellAddress)
    {
        return new GfxSurface
        {
            Triangles = source.Triangles,
            MaterialPointer = new XPointer<MaterialAsset>(
                source.MaterialPointer.Raw,
                source.MaterialPointer.ResolutionMode,
                materialPointerCellAddress),
            Material = activeMaterial,
            LightmapIndex = source.LightmapIndex,
            ReflectionProbeIndex = source.ReflectionProbeIndex,
            PrimaryLightIndex = source.PrimaryLightIndex,
            CastsSunShadow = source.CastsSunShadow
        };
    }

    private static MaterialAsset ResolveSurfaceMaterial(
        GfxWorldAsset world,
        GfxSurface surface,
        int surfaceIndex,
        XAssetPool assetPool)
    {
        MaterialAsset? material = surface.Material;
        if (material?.RuntimeAddress?.AssetPoolAddress is { } materialAddress)
        {
            material = assetPool.TryResolve(
                materialAddress.RawValue,
                XAssetType.Material,
                out MaterialAsset? activeMaterial)
                ? activeMaterial
                : null;
        }
        if (material is null)
        {
            assetPool.TryResolve(
                surface.MaterialPointer.Raw,
                XAssetType.Material,
                out material);
        }

        return material ?? throw new InvalidDataException(
            $"GfxWorld '{world.Name}' surface {surfaceIndex} has unresolved material pointer " +
            $"0x{surface.MaterialPointer.Raw:X8}.");
    }

    private static int GetMaterialSortedIndex(MaterialAsset material) =>
        (int)((material.Info.DrawSurf.Packed >> MaterialSortedIndexShift) & MaterialSortedIndexMask);

    private static void ValidateHeaderAndCollectionCounts(
        GfxWorldAsset world,
        int surfaceCount,
        int sortSurfaceCount)
    {
        if (world.SurfaceCount != surfaceCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' declares {world.SurfaceCount} surfaces but materialized {surfaceCount} rows.");
        }
        if (world.ModelCount != world.Models.Count)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' declares {world.ModelCount} brush models but materialized {world.Models.Count} rows.");
        }
        if (world.Dpvs.SurfaceBounds.Count != surfaceCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {world.Dpvs.SurfaceBounds.Count} surface-bounds rows for " +
                $"{surfaceCount} surfaces; PS3 range rotation moves the two arrays together.");
        }
        if (world.Dpvs.SurfaceMaterials.Count != surfaceCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {world.Dpvs.SurfaceMaterials.Count} surface-material rows for " +
                $"{surfaceCount} surfaces.");
        }
        if (world.Dpvs.StaticSurfaceCount != (uint)world.Dpvs.SortedSurfIndex.Count)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' declares {world.Dpvs.StaticSurfaceCount} static surfaces but materialized " +
                $"{world.Dpvs.SortedSurfIndex.Count} sorted-surface indexes.");
        }
        if (world.Dpvs.StaticSurfaceCount > (uint)surfaceCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' declares {world.Dpvs.StaticSurfaceCount} static surfaces for only " +
                $"{surfaceCount} total surfaces.");
        }

        const int surfaceVisibilityWordCountIndex = 7;
        if (world.Dpvs.VisibilityCounts.Count <= surfaceVisibilityWordCountIndex)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' does not retain the DPVS surface-visibility word-count header cell.");
        }

        uint declaredSunShadowWordCountValue =
            world.Dpvs.VisibilityCounts[surfaceVisibilityWordCountIndex];
        if (declaredSunShadowWordCountValue > int.MaxValue)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' declares an unsupported surface-visibility word count " +
                $"{declaredSunShadowWordCountValue}.");
        }

        int declaredSunShadowWordCount = (int)declaredSunShadowWordCountValue;
        // The native post-load pass clears and rebuilds this bitset only for
        // the model-zero surface-sort prefix; unsorted tail surfaces do not
        // increase its minimum coverage.
        int minimumSunShadowWordCount = checked((sortSurfaceCount + 31) / 32);
        if (declaredSunShadowWordCount < minimumSunShadowWordCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' declares {declaredSunShadowWordCount} surface-visibility words, " +
                $"but the {sortSurfaceCount}-surface PS3 sort prefix requires at least " +
                $"{minimumSunShadowWordCount}.");
        }
        if (world.Dpvs.SurfaceCastsSunShadow.Count != declaredSunShadowWordCount)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {world.Dpvs.SurfaceCastsSunShadow.Count} surface-casts-sun-shadow words, " +
                $"but its DPVS header declares {declaredSunShadowWordCount}. Native padding words must be retained.");
        }
    }

    private static void ValidateNonOverlappingPhysicalRanges(
        GfxWorldAsset world,
        IReadOnlyList<(string Name, XBlockAddress? Address, int ByteCount)> ranges)
    {
        for (int firstIndex = 0; firstIndex < ranges.Count; firstIndex++)
        {
            (string firstName, XBlockAddress? firstAddress, int firstByteCount) = ranges[firstIndex];
            if (firstByteCount == 0)
                continue;
            if (firstAddress is null || firstAddress.Value.Offset < 0)
            {
                throw new InvalidDataException(
                    $"GfxWorld '{world.Name}' {firstName} has an invalid non-empty runtime range.");
            }

            long firstEnd = (long)firstAddress.Value.Offset + firstByteCount;
            for (int secondIndex = firstIndex + 1; secondIndex < ranges.Count; secondIndex++)
            {
                (string secondName, XBlockAddress? secondAddress, int secondByteCount) = ranges[secondIndex];
                if (secondByteCount == 0)
                    continue;
                if (secondAddress is null || secondAddress.Value.Offset < 0)
                {
                    throw new InvalidDataException(
                        $"GfxWorld '{world.Name}' {secondName} has an invalid non-empty runtime range.");
                }
                if (firstAddress.Value.BlockType != secondAddress.Value.BlockType)
                    continue;

                long secondEnd = (long)secondAddress.Value.Offset + secondByteCount;
                if (firstAddress.Value.Offset < secondEnd &&
                    secondAddress.Value.Offset < firstEnd)
                {
                    throw new InvalidDataException(
                        $"GfxWorld '{world.Name}' runtime ranges {firstName} and {secondName} overlap in " +
                        $"{firstAddress.Value.BlockType}.");
                }
            }
        }
    }

    private static XBlockAddress? RequireAddress(
        XBlockAddress? address,
        int elementCount,
        GfxWorldAsset world,
        string memberName)
    {
        if (elementCount != 0 && address is null)
        {
            throw new InvalidDataException(
                $"GfxWorld '{world.Name}' has {elementCount} {memberName} element(s), but its runtime address is unresolved.");
        }
        return address;
    }

    private static byte[] ReadSnapshot(
        IXAssetSourceMemory blocks,
        XBlockAddress? address,
        int byteCount)
    {
        return byteCount == 0
            ? []
            : blocks.ReadBytes(address!.Value, byteCount);
    }

    private static void Write(
        IXAssetSourceMemory blocks,
        XBlockAddress? address,
        ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty)
            blocks.WriteBytes(address!.Value, bytes);
    }

    private static byte[] EncodeUInt16(IReadOnlyList<ushort> values)
    {
        var bytes = new byte[checked(values.Count * sizeof(ushort))];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                bytes.AsSpan(index * sizeof(ushort), sizeof(ushort)),
                values[index]);
        }
        return bytes;
    }

    private static byte[] EncodeUInt32(IReadOnlyList<uint> values)
    {
        var bytes = new byte[checked(values.Count * sizeof(uint))];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                values[index]);
        }
        return bytes;
    }
}
