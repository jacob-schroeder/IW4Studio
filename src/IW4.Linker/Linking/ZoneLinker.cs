using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Model;

namespace IW4.Linker.Linking;

/// <summary>
/// Failure-atomic result of rebuilding a canonical decoded zone.
/// </summary>
public sealed class ZoneLinkResult
{
    private readonly byte[]? _decodedBytes;

    private ZoneLinkResult(
        byte[]? decodedBytes,
        XFile? xfile,
        IEnumerable<string> errors)
    {
        _decodedBytes = decodedBytes;
        XFile = xfile;
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public bool Succeeded => _decodedBytes is not null;
    public ReadOnlyMemory<byte>? DecodedBytes => _decodedBytes is null
        ? null
        : new ReadOnlyMemory<byte>(_decodedBytes);
    public XFile? XFile { get; }
    public IReadOnlyList<string> Errors { get; }

    internal static ZoneLinkResult Success(byte[] decodedBytes, XFile xfile) =>
        new(decodedBytes, xfile, []);

    internal static ZoneLinkResult Failure(string message) =>
        new(null, null, [message]);
}

/// <summary>
/// Source-independent canonical zone linker. Providers are selected by
/// logical identity, then emitted once from frozen schema recipes.
/// </summary>
public sealed class ZoneLinker
{
    private const int XAssetRowSize = 0x08;
    private const int DecodedPageSize = 0x10000;

    public ZoneLinkResult Link(ZoneLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return LinkCore(request);
        }
        catch (Exception exception) when (exception is
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            OverflowException or
            ArgumentException or
            KeyNotFoundException)
        {
            return ZoneLinkResult.Failure(exception.Message);
        }
    }

    private static ZoneLinkResult LinkCore(ZoneLinkRequest request)
    {
        ValidateRoots(request.Roots);
        ProviderSelection providerSelection =
            SelectProviders(request.Assets.Providers);
        ResolvedRoot[] resolvedRoots = ResolveRoots(
            request,
            providerSelection);
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure =
            ResolveDependencyClosure(resolvedRoots, providerSelection);

        var output = new ZoneEmissionWriter();
        int headerSourceOffset = output.ReserveSource(XFile.SerializedSize);
        if (headerSourceOffset != 0)
            throw new InvalidOperationException("XFile header was not emitted at source offset zero.");

        output.WriteInt32(0);
        output.WriteInt32(0);
        output.WriteInt32(request.Roots.Count);
        output.WriteInt32(request.Roots.Count == 0 ? 0 : -1);

        XBlockAddress? assetTable = null;
        int assetTableSourceOffset = -1;
        if (request.Roots.Count != 0)
        {
            int tableByteCount = checked(request.Roots.Count * XAssetRowSize);
            assetTable = output.Allocate(
                XFileBlockType.LARGE,
                tableByteCount,
                alignment: 4);
            assetTableSourceOffset = output.SourceLength;
            foreach (LinkRoot root in request.Roots)
            {
                output.WriteInt32((int)root.SerializedType);
                output.WriteInt32(0);
            }
        }

        var publications = new Dictionary<ProviderSymbol, XBlockAddress>();
        for (int index = 0; index < request.Roots.Count; index++)
        {
            LinkRoot root = request.Roots[index];
            XBlockAddress tableAddress = assetTable ?? throw new InvalidOperationException(
                "A nonempty root list has no XAsset table allocation.");
            var providerCell = new XBlockAddress(
                XFileBlockType.LARGE,
                checked(tableAddress.Offset + index * XAssetRowSize + sizeof(int)));
            int providerCellSourceOffset = checked(
                assetTableSourceOffset + index * XAssetRowSize + sizeof(int));

            if (resolvedRoots[index].Provider is { } provider)
            {
                EncounterProvider(
                    provider,
                    providerCell,
                    providerCellSourceOffset,
                    output,
                    publications,
                    dependencyClosure);
            }
            else
            {
                output.PatchInt32(providerCellSourceOffset, 0);
            }
        }

        int meaningfulLength = output.SourceLength;
        uint xfileSize = checked((uint)(meaningfulLength - XFile.SerializedSize));
        uint[] blockSizes = output.GetBlockSizes();
        output.PatchUInt32(0, xfileSize);
        output.PatchUInt32(sizeof(uint), 0);
        for (int index = 0; index < blockSizes.Length; index++)
        {
            output.PatchUInt32(
                checked(2 * sizeof(uint) + index * sizeof(uint)),
                blockSizes[index]);
        }

        var xfile = new XFile(xfileSize, 0, blockSizes);
        byte[] decoded = output.CompletePadded(DecodedPageSize);
        return ZoneLinkResult.Success(decoded, xfile);
    }

    private static void ValidateRoots(IReadOnlyList<LinkRoot> roots)
    {
        var intentByAsset = new Dictionary<AssetKey, LinkRootIntent>();
        foreach (LinkRoot root in roots)
        {
            if (root.SerializedType is not (
                XAssetType.RawFile or
                XAssetType.LightDef or
                XAssetType.Image))
            {
                throw new NotSupportedException(
                    $"Canonical linking does not yet support {root.SerializedType} roots.");
            }
            if (root.Intent == LinkRootIntent.OpaqueNative)
            {
                throw new NotSupportedException(
                    "Canonical linking does not support opaque native roots.");
            }
            if (root.Intent is not (LinkRootIntent.Owned or LinkRootIntent.External) ||
                root.Asset is not { } key)
            {
                continue;
            }

            if (intentByAsset.TryGetValue(key, out LinkRootIntent previous) &&
                previous != root.Intent)
            {
                throw new InvalidDataException(
                    $"Roots for {key} cannot mix {previous} and {root.Intent} intent.");
            }

            intentByAsset.TryAdd(key, root.Intent);
        }
    }

    private static ProviderSelection SelectProviders(
        IReadOnlyList<LinkAssetProvider> providers)
    {
        var full = new Dictionary<AssetKey, ProviderBinding>();
        var references = new Dictionary<AssetKey, ProviderBinding>();
        for (int index = 0; index < providers.Count; index++)
        {
            LinkAssetProvider provider = providers[index];
            var binding = new ProviderBinding(
                new ProviderSymbol(index),
                provider.Key,
                provider.SerializedType,
                provider.Recipe);
            if (provider.IsReferencePlaceholder)
            {
                references.TryAdd(provider.Key, binding);
            }
            else
            {
                full.TryAdd(provider.Key, binding);
            }
        }

        return new ProviderSelection(full, references);
    }

    private static ResolvedRoot[] ResolveRoots(
        ZoneLinkRequest request,
        ProviderSelection providerSelection)
    {
        var externalProviders = new Dictionary<AssetKey, ProviderBinding>();
        var result = new ResolvedRoot[request.Roots.Count];

        for (int index = 0; index < request.Roots.Count; index++)
        {
            LinkRoot root = request.Roots[index];
            ProviderBinding? provider = root.Intent switch
            {
                LinkRootIntent.Owned => ResolveOwnedRoot(root, providerSelection),
                LinkRootIntent.External => ResolveExternalRoot(
                    root,
                    request.Assets.Providers.Count,
                    providerSelection,
                    externalProviders),
                LinkRootIntent.Null => null,
                LinkRootIntent.OpaqueNative => throw new NotSupportedException(
                    "Canonical linking does not support opaque native roots."),
                _ => throw new InvalidDataException(
                    $"Unsupported root intent '{root.Intent}'.")
            };

            result[index] = new ResolvedRoot(provider);
        }

        return result;
    }

    private static ProviderBinding ResolveOwnedRoot(
        LinkRoot root,
        ProviderSelection providerSelection)
    {
        AssetKey key = root.Asset ?? throw new InvalidDataException(
            $"Owned root '{root.EntryId}' has no logical asset key.");
        if (!providerSelection.Full.TryGetValue(key, out ProviderBinding provider))
        {
            throw new InvalidDataException(
                $"Owned root '{root.EntryId}' has no full provider for {key}.");
        }

        ValidateProviderClaim(root, provider);
        return provider;
    }

    private static ProviderBinding ResolveExternalRoot(
        LinkRoot root,
        int poolProviderCount,
        ProviderSelection providerSelection,
        IDictionary<AssetKey, ProviderBinding> externalProviders)
    {
        AssetKey key = root.Asset ?? throw new InvalidDataException(
            $"External root '{root.EntryId}' has no logical asset key.");
        string serializedName = root.OriginalSerializedName ??
            throw new InvalidDataException(
                $"External root '{root.EntryId}' has no serialized name.");

        if (providerSelection.References.TryGetValue(
                key,
                out ProviderBinding poolReference))
        {
            ValidateExternalProviderClaim(root, poolReference, serializedName);
            return poolReference;
        }

        if (!externalProviders.TryGetValue(key, out ProviderBinding provider))
        {
            provider = new ProviderBinding(
                new ProviderSymbol(checked(poolProviderCount + externalProviders.Count)),
                key,
                root.SerializedType,
                CreateExternalRecipe(key, root.SerializedType, serializedName));
            externalProviders.Add(key, provider);
        }
        else if (provider.SerializedType != root.SerializedType ||
            !string.Equals(
                provider.Recipe.OriginalSerializedName,
                serializedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"External roots for {key} make conflicting serialized claims.");
        }

        return provider;
    }

    private static void ValidateExternalProviderClaim(
        LinkRoot root,
        ProviderBinding provider,
        string serializedName)
    {
        if (provider.SerializedType != root.SerializedType ||
            !string.Equals(
                provider.Recipe.OriginalSerializedName,
                serializedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"External root '{root.EntryId}' does not match its selected " +
                $"reference provider for {provider.Key}.");
        }
    }

    private static AssetLinkRecipe CreateExternalRecipe(
        AssetKey key,
        XAssetType serializedType,
        string serializedName) =>
        serializedType switch
        {
            XAssetType.RawFile => RawFileLinkRecipe.CreateExternal(key, serializedName),
            XAssetType.Image => GfxImageLinkRecipe.CreateExternal(key, serializedName),
            _ => throw new NotSupportedException(
                $"Canonical linking does not yet support external {serializedType} roots.")
        };

    private static void ValidateProviderClaim(
        LinkRoot root,
        ProviderBinding provider)
    {
        if (provider.SerializedType != root.SerializedType)
        {
            throw new InvalidDataException(
                $"Owned root '{root.EntryId}' uses serialized type {root.SerializedType}, " +
                $"but its selected provider uses {provider.SerializedType}.");
        }

        if (!string.Equals(
                root.OriginalSerializedName,
                provider.Recipe.OriginalSerializedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Owned root '{root.EntryId}' does not match the exact serialized name " +
                $"of its selected provider for {provider.Key}.");
        }
    }

    private static IReadOnlyDictionary<DependencyEdge, ProviderBinding>
        ResolveDependencyClosure(
            IEnumerable<ResolvedRoot> roots,
            ProviderSelection providerSelection)
    {
        var edges = new Dictionary<DependencyEdge, ProviderBinding>();
        var complete = new HashSet<ProviderSymbol>();
        var active = new HashSet<ProviderSymbol>();

        foreach (ResolvedRoot root in roots)
        {
            if (root.Provider is { } provider)
            {
                VisitProvider(
                    provider,
                    providerSelection,
                    edges,
                    complete,
                    active);
            }
        }

        return edges;
    }

    private static void VisitProvider(
        ProviderBinding provider,
        ProviderSelection providerSelection,
        Dictionary<DependencyEdge, ProviderBinding> edges,
        ISet<ProviderSymbol> complete,
        ISet<ProviderSymbol> active)
    {
        if (complete.Contains(provider.Symbol) || !active.Add(provider.Symbol))
            return;

        foreach (AssetDependency dependency in provider.Recipe.Dependencies)
        {
            ProviderBinding dependencyProvider = ResolveDependency(
                provider,
                dependency,
                providerSelection);
            edges.TryAdd(
                new DependencyEdge(provider.Symbol, dependency),
                dependencyProvider);
            VisitProvider(
                dependencyProvider,
                providerSelection,
                edges,
                complete,
                active);
        }

        active.Remove(provider.Symbol);
        complete.Add(provider.Symbol);
    }

    private static ProviderBinding ResolveDependency(
        ProviderBinding owner,
        AssetDependency dependency,
        ProviderSelection providerSelection)
    {
        ProviderBinding provider;
        if (!providerSelection.Full.TryGetValue(dependency.Key, out provider) &&
            !providerSelection.References.TryGetValue(dependency.Key, out provider))
        {
            throw new InvalidDataException(
                $"{dependency.FieldPath} on provider {owner.Key} depends on " +
                $"{dependency.Key}, but the pool contains no provider.");
        }

        if (provider.SerializedType != dependency.SerializedType)
        {
            throw new InvalidDataException(
                $"{dependency.FieldPath} on provider {owner.Key} expects " +
                $"dependency {dependency.Key} as " +
                $"{dependency.SerializedType}, but its selected provider uses {provider.SerializedType}.");
        }

        return provider;
    }

    private static void EncounterProvider(
        ProviderBinding provider,
        XBlockAddress providerCell,
        int providerCellSourceOffset,
        ZoneEmissionWriter output,
        IDictionary<ProviderSymbol, XBlockAddress> publications,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure)
    {
        if (publications.TryGetValue(provider.Symbol, out XBlockAddress publication))
        {
            output.PatchInt32(
                providerCellSourceOffset,
                XPointerCodec.Encode(publication));
            return;
        }

        int ownerMarker;
        switch (providerCell.BlockType)
        {
            case XFileBlockType.LARGE:
                publication = providerCell;
                ownerMarker = -1;
                break;
            case XFileBlockType.TEMP:
                publication = output.Allocate(
                    XFileBlockType.LARGE,
                    sizeof(int),
                    alignment: 4);
                ownerMarker = -2;
                break;
            default:
                throw new InvalidDataException(
                    $"Provider cells in {providerCell.BlockType} are not supported by the current schema recipes.");
        }

        publications.Add(provider.Symbol, publication);
        output.PatchInt32(providerCellSourceOffset, ownerMarker);

        provider.Recipe.Emit(
            output,
            (dependency, dependencyCell, dependencySourceOffset) =>
            {
                var edge = new DependencyEdge(provider.Symbol, dependency);
                if (!dependencyClosure.TryGetValue(edge, out ProviderBinding dependencyProvider))
                {
                    throw new InvalidDataException(
                        $"The precomputed closure has no {dependency.FieldPath} edge " +
                        $"from {provider.Key} to {dependency.Key}.");
                }

                EncounterProvider(
                    dependencyProvider,
                    dependencyCell,
                    dependencySourceOffset,
                    output,
                    publications,
                    dependencyClosure);
            });
    }

    private readonly record struct ProviderSymbol(int Ordinal);

    private readonly record struct ProviderBinding(
        ProviderSymbol Symbol,
        AssetKey Key,
        XAssetType SerializedType,
        AssetLinkRecipe Recipe);

    private readonly record struct ProviderSelection(
        IReadOnlyDictionary<AssetKey, ProviderBinding> Full,
        IReadOnlyDictionary<AssetKey, ProviderBinding> References);

    private readonly record struct ResolvedRoot(ProviderBinding? Provider);

    private readonly record struct DependencyEdge(
        ProviderSymbol Owner,
        AssetDependency Dependency);
}
