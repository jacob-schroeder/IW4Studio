using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup
{
    private void AddMaterial(MaterialAsset? material)
    {
        IndexMaterialBySortedIndex(material);
        if (material?.StagingAddress is { } address)
            _materialsByAddress.TryAdd(address, material);
        if (material?.RuntimeAddress is { } runtimeAddress)
            _materialsByRuntimePointer.TryAdd(runtimeAddress.RawValue, material);
    }

    private void AddMaterial(MaterialAsset? material, XBlockAddress address)
    {
        if (material is not null)
        {
            IndexMaterialBySortedIndex(material);
            _materialsByAddress.TryAdd(address, material);
        }
    }

    private void AddMaterial(MaterialAsset? material, XBlockAddress? address)
    {
        if (address is { } cellAddress)
            AddMaterial(material, cellAddress);
    }

    private void IndexMaterialBySortedIndex(MaterialAsset? material)
    {
        if (material is null)
            return;

        int materialSortedIndex = material.Info.DrawSurf.MaterialSortedIndex;
        if (_ambiguousMaterialSortedIndices.Contains(materialSortedIndex))
            return;
        if (!_materialsBySortedIndex.TryGetValue(materialSortedIndex, out MaterialAsset? existing))
        {
            _materialsBySortedIndex.Add(materialSortedIndex, material);
            return;
        }
        if (ReferenceEquals(existing, material))
            return;

        _materialsBySortedIndex.Remove(materialSortedIndex);
        _ambiguousMaterialSortedIndices.Add(materialSortedIndex);
    }

    private void AddDependencyMaterial(MaterialAsset material, IXAssetSourceMemory? sourceBlocks)
    {
        AddMaterial(material);
        CollectMaterialImages(material);
        if (sourceBlocks is null || !_hydratedDependencyMaterials.Add(material))
            return;

        var sourceLookup = new RenderAssetLookup(sourceBlocks);
        foreach (GfxStateBits state in material.StateBits)
        {
            IReadOnlyList<uint> loadBits = sourceLookup.ResolveStateLoadBits(state);
            if (loadBits.Count > 0)
                _stateLoadBitsByObject[state] = loadBits;
        }
    }

    private void AddImage(GfxImageAsset? image)
    {
        if (image is null)
            return;

        if (image.RuntimeAddress?.AssetPoolAddress is { } poolAddress)
            _imagesByRuntimePointer.TryAdd(poolAddress.RawValue, image);
        if (image.StagingAddress is { } address)
            _imagesByAddress.TryAdd(address, image);
    }

    private void AddImage(GfxImageAsset? image, XBlockAddress? pointerCellAddress)
    {
        AddImage(image);
        if (image is not null && pointerCellAddress is { } address)
            _imagesByAddress.TryAdd(address, image);
    }

    private void AddTechset(MaterialTechniqueSetAsset? techset, bool allowBlockReads = true)
    {
        if (techset is null)
            return;

        _knownTechniqueSets.Add(techset);
        if (techset.StagingAddress is { } address)
            _techsetsByAddress.TryAdd(address, techset);
        if (techset.RuntimeAddress is { } runtimeAddress)
            _techsetsByRuntimePointer.TryAdd(runtimeAddress.RawValue, techset);
        CollectTechniqueSetShaders(techset, allowBlockReads);
    }

    private void AddDependencyTechset(MaterialTechniqueSetAsset techset, IXAssetSourceMemory? sourceBlocks)
    {
        if (sourceBlocks is not null && _hydratedDependencyTechsets.Add(techset))
        {
            if (!_dependencyLookupsByBlocks.TryGetValue(sourceBlocks, out RenderAssetLookup? sourceLookup))
            {
                sourceLookup = new RenderAssetLookup(
                    sourceBlocks,
                    _assetPool);
                _dependencyLookupsByBlocks.Add(sourceBlocks, sourceLookup);
            }

            sourceLookup.AddTechset(techset);
            _dependencyLookupByTechset[techset] = sourceLookup;
        }

        AddTechset(techset, allowBlockReads: false);
    }

    private void HydrateDependencyTechniqueGraphs()
    {
        foreach ((MaterialTechniqueSetAsset techset, RenderAssetLookup sourceLookup) in _dependencyLookupByTechset)
        {
            IReadOnlyList<MaterialTechniqueSlot> resolvedSlots = sourceLookup.ResolveTechniqueSlots(techset);
            foreach (MaterialPassAsset pass in resolvedSlots
                         .Where(slot => slot.Technique is not null)
                         .SelectMany(slot => slot.Technique!.Passes))
            {
                pass.VertexDeclaration ??= sourceLookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
                pass.VertexShader = sourceLookup.ResolveVertexShader(pass.VertexShaderPointer, pass.VertexShader);
                pass.PixelShader = sourceLookup.ResolvePixelShader(pass.PixelShaderPointer, pass.PixelShader);
                sourceLookup.ResolveShaderArgs(pass);
            }

            // The packed technique and shader pointers belong to the dependency's
            // block streams. Preserve the resolved object graph instead of
            // re-reading those addresses against this lookup's current zone.
            _resolvedTechniqueSlotsBySet[techset] = resolvedSlots;
        }
    }

    private MaterialAsset? AddPooledMaterialGraph(MaterialAsset material, XAssetPool? assetPool)
    {
        MaterialAsset activeMaterial;
        if (assetPool?.TryGetEntry(material, out XAssetPoolEntry? materialEntry) == true)
        {
            if (materialEntry.IsReferencePlaceholder)
                return null;
            activeMaterial = materialEntry.Asset as MaterialAsset
                ?? throw new InvalidDataException(
                    $"Canonical material slot '{materialEntry.Name}' contains {materialEntry.Asset.GetType().Name}.");
            AddDependencyMaterial(activeMaterial, materialEntry.SourceBlocks);
        }
        else if (assetPool is not null && material.RuntimeAddress?.AssetPoolAddress is not null)
        {
            // A pool address with no current slot is a fully retired provider,
            // not an independent material graph.
            return null;
        }
        else
        {
            activeMaterial = material;
            AddMaterial(activeMaterial);
            CollectMaterialImages(activeMaterial);
        }

        if (activeMaterial.TechniqueSet is not { } techset)
            return activeMaterial;

        MaterialTechniqueSetAsset? activeTechset = AddPooledTechniqueSetGraph(techset, assetPool);
        if (activeTechset is not null)
            _techniqueSetsByMaterial[activeMaterial] = activeTechset;
        else
            _materialsWithUnresolvedTechniqueSet.Add(activeMaterial);
        return activeMaterial;
    }

    private MaterialTechniqueSetAsset? AddPooledTechniqueSetGraph(
        MaterialTechniqueSetAsset techset,
        XAssetPool? assetPool)
    {
        if (assetPool?.TryGetEntry(techset, out XAssetPoolEntry? techsetEntry) == true)
        {
            if (techsetEntry.IsReferencePlaceholder)
                return null;
            MaterialTechniqueSetAsset activeTechset = techsetEntry.Asset as MaterialTechniqueSetAsset
                ?? throw new InvalidDataException(
                    $"Canonical technique-set slot '{techsetEntry.Name}' contains {techsetEntry.Asset.GetType().Name}.");
            AddDependencyTechset(activeTechset, techsetEntry.SourceBlocks);
            return activeTechset;
        }
        if (assetPool is not null && techset.RuntimeAddress?.AssetPoolAddress is not null)
        {
            // A canonical slot address that no longer resolves belongs to a
            // retired provider and must not become a standalone fallback.
            return null;
        }

        AddTechset(techset);
        return techset;
    }

    private void AddTechset(MaterialTechniqueSetAsset? techset, XBlockAddress? pointerCellAddress)
    {
        AddTechset(techset);
        if (techset is not null && pointerCellAddress is { } address)
            _techsetsByAddress.TryAdd(address, techset);
    }

    private void CollectTechniqueSetShaders(MaterialTechniqueSetAsset? techset, bool allowBlockReads)
    {
        if (techset is null)
            return;

        foreach (MaterialPassAsset pass in techset.TechniqueSlots
                     .Where(slot => slot.Technique is not null)
                     .SelectMany(slot => slot.Technique!.Passes))
        {
            IndexShaderByName(pass.VertexShader, _vertexShadersByName, _ambiguousVertexShaderNames);
            IndexShaderByName(pass.PixelShader, _pixelShadersByName, _ambiguousPixelShaderNames);
            if (allowBlockReads)
            {
                CachePatchedShader(pass.VertexShaderPointer.Untyped, pass.VertexShader, _vertexShadersByAddress);
                CachePatchedShader(pass.PixelShaderPointer.Untyped, pass.PixelShader, _pixelShadersByAddress);
                IndexShaderByName(
                    ResolveShader(pass.VertexShaderPointer.Untyped, MaterialShaderKind.Vertex, _vertexShadersByAddress),
                    _vertexShadersByName,
                    _ambiguousVertexShaderNames);
                IndexShaderByName(
                    ResolveShader(pass.PixelShaderPointer.Untyped, MaterialShaderKind.Pixel, _pixelShadersByAddress),
                    _pixelShadersByName,
                    _ambiguousPixelShaderNames);
            }
        }

    }

    private static void IndexShaderByName(
        MaterialShaderAsset? shader,
        Dictionary<string, MaterialShaderAsset> cache,
        HashSet<string> ambiguousNames)
    {
        if (shader?.Data is not { Length: > 0 } data || string.IsNullOrWhiteSpace(shader.Name))
            return;
        if (ambiguousNames.Contains(shader.Name))
            return;
        if (!cache.TryGetValue(shader.Name, out MaterialShaderAsset? existing))
        {
            cache.Add(shader.Name, shader);
            return;
        }
        if (existing.Data is { Length: > 0 } existingData && existingData.AsSpan().SequenceEqual(data))
            return;

        cache.Remove(shader.Name);
        ambiguousNames.Add(shader.Name);
    }

    private void IndexTopLevelShader(
        MaterialShaderAsset shader,
        XAssetPool? assetPool)
    {
        ArgumentNullException.ThrowIfNull(shader);
        MaterialShaderAsset indexed = shader;
        if (assetPool?.TryGetEntry(shader, out XAssetPoolEntry? entry) == true)
        {
            if (entry.IsReferencePlaceholder)
                return;
            indexed = entry.Asset as MaterialShaderAsset
                ?? throw new InvalidDataException(
                    $"Canonical shader slot '{entry.Name}' contains {entry.Asset.GetType().Name}.");
        }

        Dictionary<string, MaterialShaderAsset> names = indexed.Kind ==
            MaterialShaderKind.Vertex
                ? _vertexShadersByName
                : _pixelShadersByName;
        HashSet<string> ambiguous = indexed.Kind == MaterialShaderKind.Vertex
            ? _ambiguousVertexShaderNames
            : _ambiguousPixelShaderNames;
        IndexShaderByName(indexed, names, ambiguous);
        if (indexed.RuntimeAddress?.BlockAddress is { } address &&
            address.BlockType != XFileBlockType.TEMP)
        {
            Dictionary<XBlockAddress, MaterialShaderAsset> cache = indexed.Kind ==
                MaterialShaderKind.Vertex
                    ? _vertexShadersByAddress
                    : _pixelShadersByAddress;
            cache[address] = indexed;
        }
    }

    private static MaterialShaderAsset? ResolveUniqueNamedShader(
        MaterialShaderAsset? fallback,
        Dictionary<string, MaterialShaderAsset> cache,
        HashSet<string> ambiguousNames)
    {
        return !string.IsNullOrWhiteSpace(fallback?.Name) &&
               !ambiguousNames.Contains(fallback.Name) &&
               cache.TryGetValue(fallback.Name, out MaterialShaderAsset? shader)
            ? shader
            : null;
    }

    internal static MaterialShaderAsset? PreferUsableShader(
        MaterialShaderAsset? direct,
        MaterialShaderAsset? uniqueNamed,
        MaterialShaderAsset? namedFallback)
    {
        if (direct?.Data is { Length: > 0 })
            return direct;
        if (uniqueNamed?.Data is { Length: > 0 })
            return uniqueNamed;

        // A precompiled pass can preserve only its serialized shader name while
        // the alias cell currently resolves to a data-less placeholder. Keep that
        // name available for later dependency resolution instead of replacing
        // it with the anonymous placeholder.
        if (!string.IsNullOrWhiteSpace(namedFallback?.Name))
            return namedFallback;

        return direct ?? uniqueNamed ?? namedFallback;
    }

}
