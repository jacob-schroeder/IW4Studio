using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Render.Materials;
using System.Diagnostics.CodeAnalysis;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup
{
    public int TechsetCount => _techsetsByAddress.Count;
    public IEnumerable<GfxImageAsset> Images => _imagesByAddress.Values.Distinct();

    /// <summary>
    /// Resolves the material table entry addressed by a PS3 draw-group key and
    /// binds only the technique set and slots owned by that material. Pooled
    /// identities are revalidated against their current canonical providers on
    /// every call so zone retirement cannot revive a cached provider graph.
    /// </summary>
    public bool TryResolveMaterialTechniqueBinding(
        int materialSortedIndex,
        [NotNullWhen(true)] out MaterialTechniqueBinding? binding)
    {
        binding = null;
        if ((uint)materialSortedIndex >= MaterialSortedIndexCount ||
            _ambiguousMaterialSortedIndices.Contains(materialSortedIndex) ||
            !_materialsBySortedIndex.TryGetValue(materialSortedIndex, out MaterialAsset? material))
        {
            return false;
        }

        if (!TryResolveCurrentPoolAsset(
                material,
                XAssetType.Material,
                out MaterialAsset? currentMaterial,
                out _))
            return false;

        int currentMaterialSortedIndex = checked((int)(
            (currentMaterial.Info.DrawSurf.Packed >> 30) & (MaterialSortedIndexCount - 1)));
        if (currentMaterialSortedIndex != materialSortedIndex)
            return false;

        if (_assetPool is null && _materialsWithUnresolvedTechniqueSet.Contains(currentMaterial))
            return false;

        MaterialTechniqueSetAsset? techniqueSet =
            _techniqueSetsByMaterial.TryGetValue(currentMaterial, out MaterialTechniqueSetAsset? ownedTechniqueSet)
                ? ownedTechniqueSet
                : currentMaterial.TechniqueSet ?? ResolveTechniqueSet(currentMaterial.TechniqueSetPointer);
        if (techniqueSet is null)
            return false;

        if (!TryResolveCurrentPoolAsset(
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

        binding = new MaterialTechniqueBinding(
            currentMaterial,
            currentTechniqueSet,
            ResolveTechniqueSlots(currentTechniqueSet));
        return true;
    }

    private bool TryResolveCurrentPoolAsset<TAsset>(
        TAsset asset,
        XAssetType expectedType,
        [NotNullWhen(true)] out TAsset? currentAsset,
        out IXAssetSourceMemory? sourceBlocks)
        where TAsset : BaseAsset
    {
        currentAsset = asset;
        sourceBlocks = null;
        if (_assetPool is null ||
            asset.RuntimeAddress?.AssetPoolAddress is not { } poolAddress)
        {
            return true;
        }

        if (poolAddress.AssetType != expectedType ||
            !_assetPool.TryGetEntry(asset, out XAssetPoolEntry? entry) ||
            entry.Address != poolAddress ||
            entry.AssetType != expectedType ||
            entry.IsReferencePlaceholder ||
            entry.Asset is not TAsset typedAsset ||
            typedAsset.RuntimeAddress?.AssetPoolAddress != entry.Address)
        {
            currentAsset = null;
            return false;
        }

        currentAsset = typedAsset;
        sourceBlocks = entry.SourceBlocks;
        return true;
    }

    public MaterialAsset? ResolveMaterial(XPointer<MaterialAsset> pointer)
    {
        if (_materialsByRuntimePointer.TryGetValue(pointer.Raw, out MaterialAsset? runtimeMaterial))
            return runtimeMaterial;

        if (pointer.PackedAddress is { } cell && _materialsByAddress.TryGetValue(cell, out MaterialAsset? cellMaterial))
            return cellMaterial;

        if (pointer.ResolutionMode == XPointerResolutionMode.AliasCell && pointer.PackedAddress is { } aliasCell)
        {
            try
            {
                int aliasedRaw = _blocks.ReadInt32(aliasCell);
                if (_materialsByRuntimePointer.TryGetValue(aliasedRaw, out runtimeMaterial))
                    return runtimeMaterial;
            }
            catch (InvalidDataException)
            {
                // A shared-pool material may originate in a previously loaded zone.
            }
        }

        return ResolveAddress(pointer.Untyped) is { } address && _materialsByAddress.TryGetValue(address, out MaterialAsset? material)
            ? material
            : null;
    }

    /// <summary>
    /// Resolves one canonical loaded material by exact asset name. Built-in
    /// renderer materials such as depthprepass are not necessarily referenced
    /// by the current GfxWorld, so pointer-only lookup is insufficient. An
    /// ambiguous name is deliberately rejected.
    /// </summary>
    public MaterialAsset? ResolveUniqueMaterialByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_assetPool is not null)
        {
            long revision = _assetPool.Revision;
            if (!_assetPool.TryResolve(
                    XAssetType.Material,
                    name,
                    out MaterialAsset? pooledMaterial) ||
                pooledMaterial is null ||
                _assetPool.Revision != revision)
            {
                return null;
            }

            MaterialAsset? activeMaterial = AddPooledMaterialGraph(
                pooledMaterial,
                _assetPool);
            if (activeMaterial is null ||
                _assetPool.Revision != revision ||
                !string.Equals(
                    NormalizeMaterialName(activeMaterial.Info.Name),
                    name,
                    StringComparison.Ordinal))
            {
                return null;
            }

            HydrateDependencyTechniqueGraphs();
            return _assetPool.Revision == revision
                ? activeMaterial
                : null;
        }

        MaterialAsset[] matches = _materialsByAddress.Values
            .Concat(_materialsByRuntimePointer.Values)
            .Distinct<MaterialAsset>(ReferenceEqualityComparer.Instance)
            .Where(material => string.Equals(
                NormalizeMaterialName(material.Info.Name),
                name,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string NormalizeMaterialName(string? name)
    {
        ReadOnlySpan<char> normalized = name.AsSpan().Trim();
        if (!normalized.IsEmpty && normalized[0] == ',')
            normalized = normalized[1..];
        return normalized.ToString();
    }

    public GfxImageAsset? ResolveImage(XPointerReference pointer)
    {
        if (_imagesByRuntimePointer.TryGetValue(pointer.Raw, out GfxImageAsset? runtimeImage))
            return runtimeImage;

        if (pointer.PackedAddress is { } cell && _imagesByAddress.TryGetValue(cell, out GfxImageAsset? cellImage))
            return cellImage;

        if (pointer.ResolutionMode == XPointerResolutionMode.AliasCell && pointer.PackedAddress is { } aliasCell)
        {
            try
            {
                int aliasedRaw = _blocks.ReadInt32(aliasCell);
                if (_imagesByRuntimePointer.TryGetValue(aliasedRaw, out runtimeImage))
                    return runtimeImage;
            }
            catch (InvalidDataException)
            {
                // A shared-pool image may originate in a previously loaded zone.
            }
        }

        return ResolveAddress(pointer) is { } address && _imagesByAddress.TryGetValue(address, out GfxImageAsset? image)
            ? image
            : null;
    }

    public MaterialTechniqueSetAsset? ResolveTechniqueSet(XPointer<MaterialTechniqueSetAsset> pointer)
    {
        if (_techsetsByRuntimePointer.TryGetValue(pointer.Raw, out MaterialTechniqueSetAsset? runtimeTechset))
            return runtimeTechset;

        if (pointer.PackedAddress is { } cell && _techsetsByAddress.TryGetValue(cell, out MaterialTechniqueSetAsset? cellTechset))
            return cellTechset;

        if (pointer.ResolutionMode == XPointerResolutionMode.AliasCell && pointer.PackedAddress is { } aliasCell)
        {
            int aliasedRaw = _blocks.ReadInt32(aliasCell);
            if (_techsetsByRuntimePointer.TryGetValue(aliasedRaw, out runtimeTechset))
                return runtimeTechset;
        }

        return ResolveAddress(pointer.Untyped) is { } address && _techsetsByAddress.TryGetValue(address, out MaterialTechniqueSetAsset? techset)
            ? techset
            : null;
    }

    public MaterialVertexDeclarationAsset? ResolveVertexDeclaration(XPointer<MaterialVertexDeclarationAsset> pointer)
    {
        if (ResolveAddress(pointer.Untyped) is not { } address)
            return null;

        if (_vertexDeclsByAddress.TryGetValue(address, out MaterialVertexDeclarationAsset? declaration))
            return declaration;

        try
        {
            declaration = ReadVertexDeclaration(address);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        _vertexDeclsByAddress[address] = declaration;
        return declaration;
    }

    public IReadOnlyList<MaterialTechniqueSlot> ResolveTechniqueSlots(MaterialTechniqueSetAsset techset)
    {
        if (_resolvedTechniqueSlotsBySet.TryGetValue(techset, out IReadOnlyList<MaterialTechniqueSlot>? cached))
            return cached;

        IReadOnlyList<MaterialTechniqueSlot> resolved =
            _materialTechniqueGraph.ResolveSlots(techset);
        foreach (MaterialTechniqueSlot slot in resolved)
        {
            // Offset shader pointers in loader-materialized techniques can
            // target rewound TEMP cells, so their object references are null
            // even though the serialized precompiled-name payload follows the
            // pass table. This render-only provenance recovery remains outside
            // the shared structural graph resolver.
            if (slot.Technique is { } technique &&
                ResolveAddress(slot.Pointer.Untyped) is { } techniqueAddress)
            {
                HydratePrecompiledShaderNames(
                    techniqueAddress,
                    technique.Passes);
            }
        }

        _resolvedTechniqueSlotsBySet[techset] = resolved;
        return resolved;
    }

    public IReadOnlyList<uint> ResolveStateLoadBits(GfxStateBits state)
    {
        if (state.LoadBits.Count > 0)
            return state.LoadBits;
        if (_stateLoadBitsByObject.TryGetValue(state, out IReadOnlyList<uint>? cached))
            return cached;

        if (ResolveAddress(state.LoadBitsPointer) is not { } address)
            return [];

        try
        {
            IReadOnlyList<uint> loadBits =
            [
                unchecked((uint)_blocks.ReadInt32(address)),
                unchecked((uint)_blocks.ReadInt32(address.Add(sizeof(int))))
            ];
            _stateLoadBitsByObject[state] = loadBits;
            return loadBits;
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

}
