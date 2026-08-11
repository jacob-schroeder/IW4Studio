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
/// Source-independent canonical zone linker. The current schema slice emits
/// RawFile providers while the provider selection and layout are zone-wide.
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
        IReadOnlyDictionary<AssetKey, SelectedProvider> selectedProviders =
            SelectProviders(request.Assets.Providers);

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
        var relocations = new List<ProviderRowRelocation>();
        var externalProviders = new Dictionary<AssetKey, ExternalProvider>();

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

            switch (root.Intent)
            {
                case LinkRootIntent.Owned:
                {
                    AssetKey key = root.Asset ?? throw new InvalidDataException(
                        $"Owned root '{root.EntryId}' has no logical asset key.");
                    if (!selectedProviders.TryGetValue(key, out SelectedProvider selected))
                    {
                        throw new InvalidDataException(
                            $"Owned root '{root.EntryId}' has no full provider for {key}.");
                    }
                    if (selected.Provider.SerializedType != root.SerializedType)
                    {
                        throw new InvalidDataException(
                            $"Owned root '{root.EntryId}' uses serialized type {root.SerializedType}, " +
                            $"but its selected provider uses {selected.Provider.SerializedType}.");
                    }

                    EncounterProvider(
                        selected.Symbol,
                        selected.Provider.Recipe,
                        providerCell,
                        providerCellSourceOffset,
                        output,
                        publications,
                        relocations);
                    break;
                }
                case LinkRootIntent.External:
                {
                    AssetKey key = root.Asset ?? throw new InvalidDataException(
                        $"External root '{root.EntryId}' has no logical asset key.");
                    string serializedName = root.OriginalSerializedName ??
                        throw new InvalidDataException(
                            $"External root '{root.EntryId}' has no serialized name.");
                    if (!externalProviders.TryGetValue(key, out ExternalProvider external))
                    {
                        int ordinal = checked(
                            request.Assets.Providers.Count + externalProviders.Count);
                        external = new ExternalProvider(
                            new ProviderSymbol(ordinal),
                            root.SerializedType,
                            RawFileLinkRecipe.CreateExternal(key, serializedName));
                        externalProviders.Add(key, external);
                    }
                    else if (external.SerializedType != root.SerializedType ||
                        !string.Equals(
                            external.Recipe.OriginalSerializedName,
                            serializedName,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"External roots for {key} make conflicting serialized claims.");
                    }

                    EncounterProvider(
                        external.Symbol,
                        external.Recipe,
                        providerCell,
                        providerCellSourceOffset,
                        output,
                        publications,
                        relocations);
                    break;
                }
                case LinkRootIntent.Null:
                    output.PatchInt32(providerCellSourceOffset, 0);
                    break;
                case LinkRootIntent.OpaqueNative:
                    throw new NotSupportedException(
                        "Canonical RawFile linking does not support opaque native roots.");
                default:
                    throw new InvalidDataException(
                        $"Unsupported root intent '{root.Intent}'.");
            }
        }

        ResolveProviderRelocations(output, publications, relocations);

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
            if (root.SerializedType != XAssetType.RawFile)
            {
                throw new NotSupportedException(
                    $"Canonical linking does not yet support {root.SerializedType} roots.");
            }
            if (root.Intent == LinkRootIntent.OpaqueNative)
            {
                throw new NotSupportedException(
                    "Canonical RawFile linking does not support opaque native roots.");
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

    private static IReadOnlyDictionary<AssetKey, SelectedProvider> SelectProviders(
        IReadOnlyList<LinkAssetProvider> providers)
    {
        var selected = new Dictionary<AssetKey, SelectedProvider>();
        for (int index = 0; index < providers.Count; index++)
        {
            LinkAssetProvider provider = providers[index];
            if (provider.IsReferencePlaceholder)
                continue;

            selected.TryAdd(
                provider.Key,
                new SelectedProvider(new ProviderSymbol(index), provider));
        }

        return selected;
    }

    private static void EncounterProvider(
        ProviderSymbol symbol,
        RawFileLinkRecipe recipe,
        XBlockAddress providerCell,
        int providerCellSourceOffset,
        ZoneEmissionWriter output,
        IDictionary<ProviderSymbol, XBlockAddress> publications,
        ICollection<ProviderRowRelocation> relocations)
    {
        bool ownsProvider = !publications.ContainsKey(symbol);
        if (ownsProvider)
            publications.Add(symbol, providerCell);

        relocations.Add(new ProviderRowRelocation(
            providerCellSourceOffset,
            providerCell,
            symbol,
            ownsProvider));

        if (ownsProvider)
            recipe.Emit(output);
    }

    private static void ResolveProviderRelocations(
        ZoneEmissionWriter output,
        IReadOnlyDictionary<ProviderSymbol, XBlockAddress> publications,
        IEnumerable<ProviderRowRelocation> relocations)
    {
        foreach (ProviderRowRelocation relocation in relocations)
        {
            if (!publications.TryGetValue(
                    relocation.Provider,
                    out XBlockAddress publishedCell))
            {
                throw new InvalidDataException(
                    $"Provider symbol {relocation.Provider.Ordinal} was never published.");
            }

            int raw;
            if (relocation.OwnsProvider)
            {
                if (publishedCell != relocation.DestinationCell)
                {
                    throw new InvalidDataException(
                        $"Provider symbol {relocation.Provider.Ordinal} has competing owners.");
                }

                raw = -1;
            }
            else
            {
                raw = XPointerCodec.Encode(publishedCell);
            }

            output.PatchInt32(relocation.SourceOffset, raw);
        }
    }

    private readonly record struct ProviderSymbol(int Ordinal);

    private readonly record struct SelectedProvider(
        ProviderSymbol Symbol,
        LinkAssetProvider Provider);

    private readonly record struct ExternalProvider(
        ProviderSymbol Symbol,
        XAssetType SerializedType,
        RawFileLinkRecipe Recipe);

    private readonly record struct ProviderRowRelocation(
        int SourceOffset,
        XBlockAddress DestinationCell,
        ProviderSymbol Provider,
        bool OwnsProvider);
}
