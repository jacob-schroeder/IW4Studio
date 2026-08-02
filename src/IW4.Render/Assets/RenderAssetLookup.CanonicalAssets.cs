using IW4.Assets.Assets;
using IW4.Assets.Assets.LightDef;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using System.Diagnostics.CodeAnalysis;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup
{
    /// <summary>
    /// Resolves the PS3 ComPrimaryLight.defName adapter lookup against one
    /// exact active canonical LightDef provider revision.
    /// </summary>
    public bool TryResolveCanonicalLightDef(
        string name,
        long expectedPoolRevision,
        [NotNullWhen(true)] out LightDefAsset? lightDef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        XAssetPool? pool = _assetPool;
        if (pool is null || pool.Revision != expectedPoolRevision ||
            !pool.TryResolve(XAssetType.LightDef, name, out lightDef) ||
            lightDef is null ||
            lightDef.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != XAssetType.LightDef ||
            pool.Revision != expectedPoolRevision)
        {
            lightDef = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a renderer-global Material identity against one exact active
    /// canonical provider revision. Fullscreen lifecycle adapters use this
    /// boundary instead of retaining a dependency-zone material reference.
    /// </summary>
    public bool TryResolveCanonicalMaterial(
        string name,
        long expectedPoolRevision,
        [NotNullWhen(true)] out MaterialAsset? material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        XAssetPool? pool = _assetPool;
        if (pool is null || pool.Revision != expectedPoolRevision ||
            !pool.TryResolve(XAssetType.Material, name, out material) ||
            material is null ||
            material.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != XAssetType.Material ||
            pool.Revision != expectedPoolRevision)
        {
            material = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves an exact canonical RawFile provider at one active pool
    /// revision. Map createart consumers must not retain a superseded zone
    /// object or accept an unresolved placeholder.
    /// </summary>
    public bool TryResolveCanonicalRawFile(
        string name,
        long expectedPoolRevision,
        [NotNullWhen(true)] out RawFileAsset? rawFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        XAssetPool? pool = _assetPool;
        if (pool is null || pool.Revision != expectedPoolRevision ||
            !pool.TryResolve(XAssetType.RawFile, name, out rawFile) ||
            rawFile is null ||
            rawFile.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != XAssetType.RawFile ||
            pool.Revision != expectedPoolRevision)
        {
            rawFile = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the exact canonical material and its current technique graph
    /// without using a draw-surface sorted-index alias.
    /// </summary>
    public bool TryResolveCanonicalMaterialTechniqueBinding(
        string name,
        long expectedPoolRevision,
        [NotNullWhen(true)] out MapRenderMaterialTechniqueBinding? binding)
    {
        binding = null;
        if (!TryResolveCanonicalMaterial(
                name,
                expectedPoolRevision,
                out MaterialAsset? material))
        {
            return false;
        }

        MaterialTechniqueSetAsset? techniqueSet =
            _techniqueSetsByMaterial.TryGetValue(
                material,
                out MaterialTechniqueSetAsset? ownedTechniqueSet)
                ? ownedTechniqueSet
                : material.TechniqueSet ??
                  ResolveTechniqueSet(material.TechniqueSetPointer);
        if (techniqueSet is null ||
            !TryResolveCurrentPoolAsset(
                techniqueSet,
                XAssetType.Techset,
                out MaterialTechniqueSetAsset? currentTechniqueSet,
                out IXAssetSourceMemory? currentTechniqueSetBlocks))
        {
            return false;
        }

        if (currentTechniqueSetBlocks is not null &&
            !_hydratedDependencyTechsets.Contains(currentTechniqueSet))
        {
            AddDependencyTechset(currentTechniqueSet, currentTechniqueSetBlocks);
            HydrateDependencyTechniqueGraphs();
        }

        binding = new MapRenderMaterialTechniqueBinding(
            material,
            currentTechniqueSet,
            ResolveTechniqueSlots(currentTechniqueSet));
        return true;
    }

    public bool HasCanonicalAssetPoolRevision(long expectedPoolRevision)
    {
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        return _assetPool?.Revision == expectedPoolRevision;
    }

}
