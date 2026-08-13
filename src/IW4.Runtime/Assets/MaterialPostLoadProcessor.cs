using System.Buffers.Binary;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Sorts the registered material set and replaces each staged draw-surface
/// value with its runtime key.
/// </summary>
public static class MaterialPostLoadProcessor
{
    private const int MaterialDrawSurfOffset = 0x08;
    private const int MaxSortedMaterialCount =
        GfxDrawSurf.MaterialSortedIndexMask + 1;
    internal const int EmissiveTechniqueSlot =
        (int)MaterialTechniqueType.Emissive;
    internal const int LitTechniqueSlot = (int)MaterialTechniqueType.Lit;

    public static void RebuildDrawSurfs(XAssetPool assetPool)
    {
        ArgumentNullException.ThrowIfNull(assetPool);
        RebuildDrawSurfs(assetPool, assetPool.Entries);
    }

    internal static void RebuildDrawSurfs(
        XAssetPool assetPool,
        IReadOnlyCollection<XAssetPoolEntry> activeEntries)
    {
        ArgumentNullException.ThrowIfNull(assetPool);
        ArgumentNullException.ThrowIfNull(activeEntries);

        XAssetPoolEntry[] materialEntries = activeEntries
            .Where(entry => entry.AssetType == XAssetType.Material &&
                            !entry.IsReferencePlaceholder &&
                            entry.Asset is MaterialAsset)
            .ToArray();
        if (materialEntries.Length > MaxSortedMaterialCount)
        {
            throw new InvalidDataException(
                $"default_mp materialSortedIndex is 13 bits, but {materialEntries.Length} canonical materials are registered.");
        }

        var resolver = new MaterialGraphResolver(assetPool);
        Array.Sort(materialEntries, new MaterialEntryComparer(resolver));
        for (int sortedIndex = 0; sortedIndex < materialEntries.Length; sortedIndex++)
        {
            XAssetPoolEntry entry = materialEntries[sortedIndex];
            var material = (MaterialAsset)entry.Asset;
            ulong packed = PackDrawSurf(material, sortedIndex, resolver);
            material.Info.DrawSurf = new GfxDrawSurf(packed);

            // Keep the retained source header synchronized with the managed value.
            entry.SourceBlocks?.WriteUInt64(entry.StagingAddress.Add(MaterialDrawSurfOffset), packed);
            if (entry.HeaderBytes.Length >= MaterialDrawSurfOffset + sizeof(ulong))
            {
                BinaryPrimitives.WriteUInt64BigEndian(
                    entry.HeaderBytes.AsSpan(MaterialDrawSurfOffset, sizeof(ulong)),
                    packed);
            }
        }
    }

    public static ulong PackDrawSurf(MaterialAsset material, int sortedIndex, XAssetPool assetPool)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(assetPool);
        if ((uint)sortedIndex >= MaxSortedMaterialCount)
            throw new ArgumentOutOfRangeException(nameof(sortedIndex));
        if ((byte)material.Info.SortKey >= 0x40)
            throw new InvalidDataException($"Material '{material.Info.Name}' has invalid 6-bit sort key {(byte)material.Info.SortKey}.");

        return PackDrawSurf(material, sortedIndex, new MaterialGraphResolver(assetPool));
    }

    private static ulong PackDrawSurf(MaterialAsset material, int sortedIndex, MaterialGraphResolver resolver)
    {
        ulong primarySortKey =
            (ulong)(byte)material.Info.SortKey << GfxDrawSurf.PrimarySortKeyShift;
        ulong prepass =
            (ulong)(byte)GetStandardPrepassSortKey(material, resolver) <<
            GfxDrawSurf.PrepassShift;
        ulong customIndex =
            (ulong)((byte)(material.Info.GameFlags &
                MaterialGameFlags.ShadowCasterRouteMask) >> 6) <<
            GfxDrawSurf.CustomIndexShift;
        ulong materialSortedIndex =
            (ulong)sortedIndex << GfxDrawSurf.MaterialSortedIndexShift;
        return primarySortKey | prepass | customIndex | materialSortedIndex;
    }

    internal static MaterialPrepassType GetStandardPrepassSortKey(
        MaterialAsset material,
        MaterialGraphResolver resolver)
    {
        MaterialTechniqueAsset? technique = resolver.GetTechnique(
            material,
            (int)MaterialTechniqueType.DepthPrepass);
        if (technique is null ||
            (material.StateFlags & MaterialStateFlags.Decal) != 0)
        {
            return MaterialPrepassType.None;
        }

        return (technique.Flags & MaterialTechniqueFlags.ZPrepass) != 0
            ? MaterialPrepassType.Standard
            : MaterialPrepassType.Alpha;
    }

    internal static MaterialShaderAsset? GetShader(
        MaterialPassAsset pass,
        MaterialShaderKind kind,
        MaterialGraphResolver resolver)
    {
        MaterialShaderAsset? direct = kind == MaterialShaderKind.Vertex
            ? pass.VertexShader
            : pass.PixelShader;
        if (direct is not null)
            return direct;

        // A block-hydrated graph can leave a packed shader reference unresolved.
        // In that case the comparator continues with its next tie-breaker.
        return null;
    }
}
