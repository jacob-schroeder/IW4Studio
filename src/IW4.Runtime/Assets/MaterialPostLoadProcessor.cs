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
    private const int MaxSortedMaterialCount = 0x2000;
    internal const int EmissiveTechniqueSlot = 5;
    internal const int LitTechniqueSlot = 9;

    public static void RebuildDrawSurfs(XAssetPool assetPool)
    {
        ArgumentNullException.ThrowIfNull(assetPool);

        XAssetPoolEntry[] materialEntries = assetPool.Entries
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
        if (material.Info.SortKey >= 0x40)
            throw new InvalidDataException($"Material '{material.Info.Name}' has invalid 6-bit sort key {material.Info.SortKey}.");

        return PackDrawSurf(material, sortedIndex, new MaterialGraphResolver(assetPool));
    }

    private static ulong PackDrawSurf(MaterialAsset material, int sortedIndex, MaterialGraphResolver resolver)
    {
        ulong primarySortKey = (ulong)material.Info.SortKey << 58;
        ulong prepass = (ulong)GetStandardPrepassSortKey(material, resolver) << 43;
        ulong customIndex = (ulong)((material.Info.GameFlags >> 6) & 0x03) << 25;
        ulong materialSortedIndex = (ulong)sortedIndex << 30;
        return primarySortKey | prepass | customIndex | materialSortedIndex;
    }

    internal static int GetStandardPrepassSortKey(MaterialAsset material, MaterialGraphResolver resolver)
    {
        MaterialTechniqueAsset? technique = resolver.GetTechnique(material, 0);
        if (technique is null || (material.StateFlags & 0x04) != 0)
            return 3;

        return ((technique.Flags ^ 0x04) >> 2) & 1;
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
