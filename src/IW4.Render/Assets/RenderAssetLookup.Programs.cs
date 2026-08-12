using IW4.Assets.Assets;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using System.Diagnostics.CodeAnalysis;

namespace IW4.Render.Assets;

public sealed partial class RenderAssetLookup
{
    public MaterialShaderAsset? ResolveVertexShader(XPointer<MaterialShaderAsset> pointer)
    {
        return ResolveShader(pointer.Untyped, MaterialShaderKind.Vertex, _vertexShadersByAddress);
    }

    public MaterialShaderAsset? ResolveVertexShader(
        XPointer<MaterialShaderAsset> pointer,
        MaterialShaderAsset? namedFallback)
    {
        MaterialShaderAsset? direct = ResolveShader(
            pointer.Untyped,
            MaterialShaderKind.Vertex,
            _vertexShadersByAddress);
        MaterialShaderAsset? named = ResolveUniqueNamedShader(
            namedFallback,
            _vertexShadersByName,
            _ambiguousVertexShaderNames);
        return PreferUsableShader(direct, named, namedFallback);
    }

    public MaterialShaderAsset? ResolvePixelShader(XPointer<MaterialShaderAsset> pointer)
    {
        return ResolveShader(pointer.Untyped, MaterialShaderKind.Pixel, _pixelShadersByAddress);
    }

    public MaterialShaderAsset? ResolvePixelShader(
        XPointer<MaterialShaderAsset> pointer,
        MaterialShaderAsset? namedFallback)
    {
        MaterialShaderAsset? direct = ResolveShader(
            pointer.Untyped,
            MaterialShaderKind.Pixel,
            _pixelShadersByAddress);
        MaterialShaderAsset? named = ResolveUniqueNamedShader(
            namedFallback,
            _pixelShadersByName,
            _ambiguousPixelShaderNames);
        return PreferUsableShader(direct, named, namedFallback);
    }

    public IReadOnlyList<MaterialShaderArgumentAsset> ResolveShaderArgs(MaterialPassAsset pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        lock (_resolvedShaderArgsGate)
        {
            if (_resolvedShaderArgsByPass.TryGetValue(
                    pass,
                    out IReadOnlyList<MaterialShaderArgumentAsset>? cached) &&
                ReferenceEquals(pass.Args, cached))
            {
                return cached;
            }

            IReadOnlyList<MaterialShaderArgumentAsset> snapshot =
                SnapshotShaderArgs(pass);
            pass.Args = snapshot;
            _resolvedShaderArgsByPass[pass] = snapshot;
            return snapshot;
        }
    }

    public MapRenderSelectedPassProgramSources ResolveSources(
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        MapRenderSelectedTechniquePass selectedPass)
    {
        ArgumentNullException.ThrowIfNull(techniqueSet);
        ArgumentNullException.ThrowIfNull(technique);
        RenderAssetLookup sourceLookup =
            _dependencyLookupByTechset.TryGetValue(
                techniqueSet,
                out RenderAssetLookup? dependencyLookup)
                ? dependencyLookup
                : this;
        MaterialPassAsset pass = selectedPass.Pass;
        lock (_selectedPassProgramSourcesGate)
        {
            long poolRevision = _assetPool?.Revision ?? -1;
            if (_selectedPassProgramSourcePoolRevision != poolRevision)
            {
                _selectedPassProgramSources.Clear();
                _selectedPassProgramSourcePoolRevision = poolRevision;
            }
            var cacheKey = new SelectedPassProgramSourceCacheKey(
                sourceLookup,
                techniqueSet,
                technique,
                selectedPass.PassIndex,
                pass,
                poolRevision);
            if (_selectedPassProgramSources.TryGetValue(
                    cacheKey,
                    out MapRenderSelectedPassProgramSources? cached))
            {
                if ((_assetPool?.Revision ?? -1) != poolRevision)
                {
                    throw new InvalidOperationException(
                        "The canonical provider revision changed while reading cached selected-pass program sources.");
                }

                return cached;
            }

            MaterialVertexDeclarationAsset? declaration =
                pass.VertexDeclaration ??
                sourceLookup.ResolveVertexDeclaration(pass.VertexDeclPointer);
            MapRenderShaderProgramResolution vertex =
                sourceLookup.ResolveShaderProgram(
                    pass.VertexShaderPointer.Untyped,
                    pass.VertexShader,
                    MaterialShaderKind.Vertex,
                    _assetPool);
            MapRenderShaderProgramResolution pixel =
                sourceLookup.ResolveShaderProgram(
                    pass.PixelShaderPointer.Untyped,
                    pass.PixelShader,
                    MaterialShaderKind.Pixel,
                    _assetPool);
            int expected = pass.PerPrimArgCount + pass.PerObjArgCount +
                pass.StableArgCount;
            var result = new MapRenderSelectedPassProgramSources(
                declaration,
                vertex,
                pixel,
                sourceLookup.ResolveShaderArgs(pass),
                expected);
            if ((_assetPool?.Revision ?? -1) != poolRevision)
            {
                throw new InvalidOperationException(
                    "The canonical provider revision changed while resolving selected-pass program sources.");
            }
            _selectedPassProgramSources.Add(cacheKey, result);
            return result;
        }
    }

    private readonly record struct SelectedPassProgramSourceCacheKey(
        RenderAssetLookup SourceLookup,
        MaterialTechniqueSetAsset TechniqueSet,
        MaterialTechniqueAsset Technique,
        int PassIndex,
        MaterialPassAsset Pass,
        long AssetPoolRevision);

    private IReadOnlyList<MaterialShaderArgumentAsset> SnapshotShaderArgs(
        MaterialPassAsset pass)
    {
        int count = pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount;
        if (count <= 0 || pass.Args.Count == count)
            return Array.AsReadOnly(pass.Args.ToArray());

        if (ResolveAddress(pass.ArgsPointer.Untyped) is not { } address)
            return Array.AsReadOnly(pass.Args.ToArray());

        byte[] bytes;
        try
        {
            bytes = _blocks.ReadBytes(address, checked(count * ShaderArgSize));
        }
        catch (InvalidDataException)
        {
            return Array.AsReadOnly(pass.Args.ToArray());
        }

        var cursor = new FastFileCursor(bytes, address);
        var args = new MaterialShaderArgumentAsset[count];
        var argumentPointers = new XPointerReference[count];
        for (int i = 0; i < args.Length; i++)
        {
            int offset = cursor.Offset;
            var type = (MaterialShaderArgumentType)cursor.ReadUInt16();
            ushort dest = cursor.ReadUInt16();
            XBlockAddress? valueCell = cursor.AddressAt(cursor.Offset);
            XPointerReference argumentPointer = XPointerReference.FromRaw(
                cursor.ReadInt32(),
                XPointerResolutionMode.Direct,
                valueCell);
            argumentPointers[i] = argumentPointer;
            args[i] = new MaterialShaderArgumentAsset(offset, type, dest, argumentPointer.Raw, LiteralConstant: null);
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Type is not (MaterialShaderArgumentType.LiteralVertexConst or MaterialShaderArgumentType.LiteralPixelConst))
                continue;

            if (ReadLiteralFloat4(argumentPointers[i]) is { } literal)
                args[i] = args[i] with { LiteralConstant = literal };
        }

        return Array.AsReadOnly(args);
    }

    private MapRenderShaderProgramResolution ResolveShaderProgram(
        XPointerReference pointer,
        MaterialShaderAsset? namedFallback,
        MaterialShaderKind kind,
        XAssetPool? assetPool)
    {
        XAssetType expectedType = MaterialShaderAsset.GetAssetType(kind);
        if (assetPool?.TryResolve(
                pointer.Raw,
                expectedType,
                out MaterialShaderAsset? pointerCanonical) == true &&
            pointerCanonical is not null &&
            TryGetActiveProviderIdentity(
                pointerCanonical,
                assetPool,
                out MapRenderShaderProgramProviderIdentity? pointerProvider))
        {
            return new MapRenderShaderProgramResolution(
                pointer,
                kind,
                pointerCanonical,
                MapRenderShaderProgramResolutionKind.CanonicalActiveProvider,
                pointerProvider);
        }

        MaterialShaderAsset? aliasOwner = ResolveAliasCellOwner(
            pointer,
            GetShaderAliasCellCache(kind));
        if (aliasOwner is not null)
        {
            MaterialShaderAsset resolved = ResolveCurrentActiveShaderProvider(
                aliasOwner,
                kind,
                assetPool,
                out MapRenderShaderProgramProviderIdentity? providerIdentity);
            return new MapRenderShaderProgramResolution(
                pointer,
                kind,
                resolved,
                MapRenderShaderProgramResolutionKind.AliasCellOwner,
                providerIdentity);
        }

        Dictionary<XBlockAddress, MaterialShaderAsset> addressCache = kind ==
            MaterialShaderKind.Vertex
                ? _vertexShadersByAddress
                : _pixelShadersByAddress;
        if (ResolveAddress(pointer) is { BlockType: not XFileBlockType.TEMP } address)
        {
            MaterialShaderAsset? persistent = addressCache.TryGetValue(
                address,
                out MaterialShaderAsset? cached)
                ? cached
                : ReadShader(address, kind);
            if (persistent is not null)
            {
                addressCache[address] = persistent;
                MaterialShaderAsset resolved = ResolveCurrentActiveShaderProvider(
                    persistent,
                    kind,
                    assetPool,
                    out MapRenderShaderProgramProviderIdentity? providerIdentity);
                return new MapRenderShaderProgramResolution(
                    pointer,
                    kind,
                    resolved,
                    MapRenderShaderProgramResolutionKind.PersistentBlockAddress,
                    providerIdentity);
            }
        }

        if (namedFallback is not null &&
            TryGetCanonicalShader(
                namedFallback,
                kind,
                assetPool,
                out MaterialShaderAsset? fallbackCanonical,
                out MapRenderShaderProgramProviderIdentity? fallbackProvider))
        {
            return new MapRenderShaderProgramResolution(
                pointer,
                kind,
                fallbackCanonical,
                MapRenderShaderProgramResolutionKind.HydratedActiveProvider,
                fallbackProvider);
        }

        Dictionary<string, MaterialShaderAsset> nameCache = kind ==
            MaterialShaderKind.Vertex
                ? _vertexShadersByName
                : _pixelShadersByName;
        HashSet<string> ambiguousNames = kind == MaterialShaderKind.Vertex
            ? _ambiguousVertexShaderNames
            : _ambiguousPixelShaderNames;
        MaterialShaderAsset? unique = ResolveUniqueNamedShader(
            namedFallback,
            nameCache,
            ambiguousNames);
        if (unique is not null)
        {
            return new MapRenderShaderProgramResolution(
                pointer,
                kind,
                unique,
                MapRenderShaderProgramResolutionKind.UniqueNameFallback,
                providerIdentity: null);
        }

        bool referencePlaceholder = namedFallback is not null &&
            IsReferencePlaceholderShader(namedFallback, assetPool);
        bool hydratedObject = !referencePlaceholder &&
            (namedFallback?.Data is { Length: > 0 } ||
             namedFallback?.ProgramBytes is { Length: > 0 });
        bool namedPlaceholder = referencePlaceholder ||
            !hydratedObject && !string.IsNullOrWhiteSpace(namedFallback?.Name);
        return new MapRenderShaderProgramResolution(
            pointer,
            kind,
            namedFallback,
            hydratedObject
                ? MapRenderShaderProgramResolutionKind.HydratedObjectWithoutActiveProvider
                : namedPlaceholder
                    ? MapRenderShaderProgramResolutionKind.NamedPlaceholder
                    : MapRenderShaderProgramResolutionKind.Unresolved,
            providerIdentity: null);
    }

    private static bool IsReferencePlaceholderShader(
        MaterialShaderAsset shader,
        XAssetPool? assetPool)
    {
        return assetPool is not null &&
            shader.RuntimeAddress?.AssetPoolAddress is { } address &&
            assetPool.TryGetSlot(address, out XAssetSlot? slot) &&
            slot is not null &&
            slot.ActiveProvider.IsReferencePlaceholder &&
            ReferenceEquals(slot.CanonicalAsset, shader);
    }

    internal static MaterialShaderAsset ResolveCurrentActiveShaderProvider(
        MaterialShaderAsset candidate,
        MaterialShaderKind kind,
        XAssetPool? assetPool,
        out MapRenderShaderProgramProviderIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (TryGetCanonicalShader(
            candidate,
            kind,
            assetPool,
            out MaterialShaderAsset? canonical,
            out identity))
        {
            return canonical;
        }

        identity = null;
        return candidate;
    }

    private static bool TryGetCanonicalShader(
        MaterialShaderAsset shader,
        MaterialShaderKind kind,
        XAssetPool? assetPool,
        [NotNullWhen(true)] out MaterialShaderAsset? canonical,
        [NotNullWhen(true)] out MapRenderShaderProgramProviderIdentity? identity)
    {
        canonical = null;
        identity = null;
        if (assetPool is null ||
            shader.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !assetPool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.ActiveProvider.IsReferencePlaceholder ||
            slot.CanonicalAsset is not MaterialShaderAsset active ||
            active.Kind != kind)
        {
            return false;
        }

        canonical = active;
        identity = CreateProviderIdentity(slot, active);
        return true;
    }

    private static bool TryGetActiveProviderIdentity(
        MaterialShaderAsset shader,
        XAssetPool? assetPool,
        [NotNullWhen(true)] out MapRenderShaderProgramProviderIdentity? identity)
    {
        identity = null;
        if (assetPool is null ||
            shader.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !assetPool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.ActiveProvider.IsReferencePlaceholder ||
            !ReferenceEquals(slot.CanonicalAsset, shader))
        {
            return false;
        }

        identity = CreateProviderIdentity(slot, shader);
        return true;
    }

    private static MapRenderShaderProgramProviderIdentity CreateProviderIdentity(
        XAssetSlot slot,
        MaterialShaderAsset shader)
    {
        XAssetProviderContribution provider = slot.ActiveProvider;
        return new MapRenderShaderProgramProviderIdentity(
            slot.Address,
            provider.Id,
            provider.Owner,
            provider.RegistrationSequence,
            provider.StagingAddress,
            shader.RuntimeAddress,
            provider.IsReferencePlaceholder,
            IsActiveCanonicalProvider:
                !provider.IsReferencePlaceholder &&
                ReferenceEquals(slot.CanonicalAsset, shader));
    }

    private void CachePatchedShader(
        XPointerReference pointer,
        MaterialShaderAsset? shader,
        Dictionary<XBlockAddress, MaterialShaderAsset> cache)
    {
        if (shader is null || pointer.CellAddress is not { } cellAddress)
            return;

        // Inline shader roots live in TEMP, which is rewound after each loader
        // call. The stable identity retained by later alias pointers is the
        // destination pointer cell, not the transient TEMP address written into
        // it. Cache the loader-materialized object by that cell before TEMP can
        // be reused by an unrelated shader.
        if (pointer.Type is PointerType.Inline or PointerType.Insert)
        {
            Dictionary<XBlockAddress, MaterialShaderAsset> aliasCellCache =
                GetShaderAliasCellCache(shader.Kind);
            if (!aliasCellCache.TryGetValue(cellAddress, out MaterialShaderAsset? existing) ||
                existing.Data is not { Length: > 0 } && shader.Data is { Length: > 0 })
            {
                aliasCellCache[cellAddress] = shader;
            }
        }

        try
        {
            int runtimeRaw = _blocks.ReadInt32(cellAddress);
            if (XPointerCodec.TryDecodeBlockAddress(runtimeRaw, out XBlockAddress address))
            {
                // A post-load read of a rewound TEMP address is not stable shader
                // identity. The destination-cell cache above is authoritative for
                // inline payloads; only persistent block targets are safe here.
                if (address.BlockType == XFileBlockType.TEMP)
                    return;

                MaterialShaderAsset cached = shader;
                if (shader.Data is not { Length: > 0 } &&
                    ReadShader(address, shader.Kind) is { Data.Length: > 0 } hydrated)
                {
                    cached = new MaterialShaderAsset
                    {
                        Offset = shader.Offset,
                        RuntimeAddress = hydrated.RuntimeAddress ?? shader.RuntimeAddress,
                        Kind = shader.Kind,
                        NamePointer = shader.NamePointer,
                        Name = shader.Name,
                        DataPointer = hydrated.DataPointer,
                        DataSize = hydrated.DataSize,
                        ProgramBytes = hydrated.ProgramBytes.Length > 0 ? hydrated.ProgramBytes : shader.ProgramBytes,
                        Data = hydrated.Data
                    };
                }

                if (!cache.TryGetValue(address, out MaterialShaderAsset? existing) ||
                    existing.Data is not { Length: > 0 } && cached.Data is { Length: > 0 })
                {
                    cache[address] = cached;
                }
            }
        }
        catch (InvalidDataException)
        {
            // TEMP-backed shader roots are optional cache entries; unresolved offsets stay diagnostic-only.
        }
    }

}
