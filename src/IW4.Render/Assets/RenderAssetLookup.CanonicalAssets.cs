using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
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
    internal bool TryResolveCanonicalImage(
        GfxImageAsset seed,
        [NotNullWhen(true)] out GfxImageAsset? image)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (_stagedImages.Contains(seed) &&
            seed.RuntimeAddress?.AssetPoolAddress is null &&
            seed.PayloadBytes.Count > 0)
        {
            image = seed;
            return true;
        }

        XAssetPool? pool = _assetPool;
        if (pool is null)
        {
            image = null;
            return false;
        }

        long poolRevision = pool.Revision;
        if (!MapRenderAssetProviderSnapshotFactory.TryCapture(
                pool,
                seed,
                XAssetType.Image,
                poolRevision,
                out image,
                out _) ||
            image is null ||
            pool.Revision != poolRevision)
        {
            image = null;
            return false;
        }

        return true;
    }

    internal bool TryResolveStagedMaterialTechniqueBinding(
        MaterialAsset material,
        long expectedPoolRevision,
        [NotNullWhen(true)] out MaterialTechniqueBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(material);
        binding = null;
        XAssetPool? pool = _assetPool;
        if (pool is null || pool.Revision != expectedPoolRevision ||
            !_stagedMaterials.Contains(material) ||
            material.RuntimeAddress?.AssetPoolAddress is not null)
        {
            return false;
        }

        MaterialAsset? stagedMaterial = AddPooledMaterialGraph(material, pool);
        if (!ReferenceEquals(stagedMaterial, material))
            return false;
        MaterialTechniqueSetAsset? techniqueSet =
            _techniqueSetsByMaterial.TryGetValue(
                material,
                out MaterialTechniqueSetAsset? resolved)
                ? resolved
                : material.TechniqueSet;
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
        if (pool.Revision != expectedPoolRevision)
            return false;

        binding = new MaterialTechniqueBinding(
            material,
            currentTechniqueSet,
            ResolveTechniqueSlots(currentTechniqueSet));
        return true;
    }

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
        [NotNullWhen(true)] out MaterialTechniqueBinding? binding)
    {
        binding = null;
        if (!TryResolveCanonicalMaterial(
                name,
                expectedPoolRevision,
                out MaterialAsset? material))
        {
            return false;
        }

        XAssetPool? pool = _assetPool;
        if (pool is null)
            return false;
        MaterialAsset? activeMaterial = AddPooledMaterialGraph(
            material,
            pool);
        if (!ReferenceEquals(activeMaterial, material) ||
            pool.Revision != expectedPoolRevision)
        {
            return false;
        }
        HydrateDependencyTechniqueGraphs();
        if (pool.Revision != expectedPoolRevision)
            return false;

        MaterialTechniqueSetAsset? techniqueSet =
            _techniqueSetsByMaterial.TryGetValue(
                activeMaterial,
                out MaterialTechniqueSetAsset? ownedTechniqueSet)
                ? ownedTechniqueSet
                : activeMaterial.TechniqueSet ??
                  ResolveTechniqueSet(activeMaterial.TechniqueSetPointer);
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

        IReadOnlyList<MaterialTechniqueSlot> slots =
            ResolveTechniqueSlots(currentTechniqueSet);
        if (pool.Revision != expectedPoolRevision)
            return false;

        binding = new MaterialTechniqueBinding(
            activeMaterial,
            currentTechniqueSet,
            slots);
        return true;
    }

    public bool HasCanonicalAssetPoolRevision(long expectedPoolRevision)
    {
        if (expectedPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPoolRevision));

        return _assetPool?.Revision == expectedPoolRevision;
    }

}
