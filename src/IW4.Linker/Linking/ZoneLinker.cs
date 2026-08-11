using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
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
    private readonly IReadOnlyList<DbHeaderImageStreamLanguageTable>
        _imageStreamLanguageTables;

    private ZoneLinkResult(
        byte[]? decodedBytes,
        XFile? xfile,
        uint languageMask,
        uint selectedLanguageMask,
        IEnumerable<DbHeaderImageStreamLanguageTable> imageStreamLanguageTables,
        IEnumerable<string> errors)
    {
        _decodedBytes = decodedBytes;
        XFile = xfile;
        LanguageMask = languageMask;
        SelectedLanguageMask = selectedLanguageMask;
        _imageStreamLanguageTables = Array.AsReadOnly(imageStreamLanguageTables
            .Select(table => table ?? throw new ArgumentException(
                "Image-stream language tables cannot contain null.",
                nameof(imageStreamLanguageTables)))
            .Select(table => new DbHeaderImageStreamLanguageTable(
                table.SerializedIndex,
                table.LanguageMask,
                table.ImageStreamEntries))
            .ToArray());
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public bool Succeeded => _decodedBytes is not null;
    public ReadOnlyMemory<byte>? DecodedBytes => _decodedBytes is null
        ? null
        : new ReadOnlyMemory<byte>(_decodedBytes);
    public XFile? XFile { get; }
    public uint LanguageMask { get; }
    public uint SelectedLanguageMask { get; }
    public IReadOnlyList<DbHeaderImageStreamLanguageTable>
        ImageStreamLanguageTables => _imageStreamLanguageTables;
    public IReadOnlyList<string> Errors { get; }

    internal static ZoneLinkResult Success(
        byte[] decodedBytes,
        XFile xfile,
        uint languageMask,
        uint selectedLanguageMask,
        IEnumerable<DbHeaderImageStreamLanguageTable> imageStreamLanguageTables) =>
        new(
            decodedBytes,
            xfile,
            languageMask,
            selectedLanguageMask,
            imageStreamLanguageTables,
            []);

    internal static ZoneLinkResult Failure(string message) =>
        new(null, null, 0, 0, [], [message]);
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
        IReadOnlyDictionary<AssetKey, ProviderBinding> rootAuthorities =
            BindRootAuthorities(resolvedRoots);
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure =
            ResolveDependencyClosure(
                resolvedRoots,
                providerSelection,
                rootAuthorities);
        AssetRow[] assetRows = BuildAssetRows(
            request.Roots,
            resolvedRoots,
            dependencyClosure);
        ValidateLanguageCount(
            request.LanguageCount,
            resolvedRoots,
            dependencyClosure);
        ValidateAssetRowPublicationOrder(assetRows, dependencyClosure);
        ScriptStringTable scriptStrings = BuildScriptStringTable(
            assetRows,
            dependencyClosure);

        var output = new ZoneEmissionWriter();
        int headerSourceOffset = output.ReserveSource(XFile.SerializedSize);
        if (headerSourceOffset != 0)
            throw new InvalidOperationException("XFile header was not emitted at source offset zero.");

        output.WriteInt32(scriptStrings.Values.Count);
        output.WriteInt32(scriptStrings.Values.Count == 0 ? 0 : -1);
        output.WriteInt32(assetRows.Length);
        output.WriteInt32(assetRows.Length == 0 ? 0 : -1);

        var storageState = new LinkStorageEmissionState(
            output,
            scriptStrings.Indices);
        if (scriptStrings.Values.Count != 0)
        {
            storageState.EmitDetached(
                CreateScriptStringStorage(scriptStrings.Values),
                "XAssetList.ScriptStrings");
        }

        XBlockAddress? assetTable = null;
        int assetTableSourceOffset = -1;
        if (assetRows.Length != 0)
        {
            int tableByteCount = checked(assetRows.Length * XAssetRowSize);
            assetTable = output.Allocate(
                XFileBlockType.LARGE,
                tableByteCount,
                alignment: 4);
            assetTableSourceOffset = output.SourceLength;
            foreach (AssetRow row in assetRows)
            {
                output.WriteInt32((int)row.SerializedType);
                output.WriteInt32(0);
            }
        }

        var publications = new Dictionary<ProviderSymbol, XBlockAddress>();
        Dictionary<uint, List<DbHeaderImageStreamEntry>> imageStreams =
            CreateImageStreamCollectors(request.LanguageMask);
        for (int index = 0; index < assetRows.Length; index++)
        {
            AssetRow row = assetRows[index];
            XBlockAddress tableAddress = assetTable ?? throw new InvalidOperationException(
                "A nonempty root list has no XAsset table allocation.");
            var providerCell = new XBlockAddress(
                XFileBlockType.LARGE,
                checked(tableAddress.Offset + index * XAssetRowSize + sizeof(int)));
            int providerCellSourceOffset = checked(
                assetTableSourceOffset + index * XAssetRowSize + sizeof(int));

            if (row.Provider is { } provider)
            {
                EncounterProvider(
                    provider,
                    providerCell,
                    providerCellSourceOffset,
                    storageState,
                    publications,
                    dependencyClosure,
                    imageStreams);
            }
            else
            {
                output.PatchInt32(
                    providerCellSourceOffset,
                    row.OpaqueHeader ?? 0);
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
        DbHeaderImageStreamLanguageTable[] imageStreamLanguageTables = imageStreams
            .OrderBy(pair => pair.Key)
            .Select((pair, index) => new DbHeaderImageStreamLanguageTable(
                index,
                pair.Key,
                pair.Value))
            .ToArray();
        return ZoneLinkResult.Success(
            decoded,
            xfile,
            request.LanguageMask,
            request.SelectedLanguageMask,
            imageStreamLanguageTables);
    }

    private static void ValidateRoots(IReadOnlyList<LinkRoot> roots)
    {
        var intentByAsset = new Dictionary<AssetKey, LinkRootIntent>();
        foreach (LinkRoot root in roots)
        {
            bool nativeNoOp =
                XAssetTypeDispatchCatalog.IsNativeNoOp(root.SerializedType);
            if (root.Intent == LinkRootIntent.OpaqueNative)
            {
                if (!nativeNoOp)
                {
                    throw new InvalidDataException(
                        $"Root '{root.EntryId}' uses opaque-native intent for " +
                        $"provider-backed type {root.SerializedType}.");
                }

                continue;
            }
            if (nativeNoOp)
            {
                throw new InvalidDataException(
                    $"Native no-op root '{root.EntryId}' ({root.SerializedType}) " +
                    "must preserve its opaque stock XAssetHeader word.");
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
                LinkRootIntent.OpaqueNative => null,
                _ => throw new InvalidDataException(
                    $"Unsupported root intent '{root.Intent}'.")
            };

            result[index] = new ResolvedRoot(
                provider,
                root.Intent == LinkRootIntent.OpaqueNative
                    ? root.OpaqueHeader
                    : null);
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
        ExternalAssetLinkRecipe.CreateSynthetic(
            key,
            serializedType,
            serializedName);

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
            ProviderSelection providerSelection,
            IReadOnlyDictionary<AssetKey, ProviderBinding> rootAuthorities)
    {
        var edges = new Dictionary<DependencyEdge, ProviderBinding>();
        var complete = new HashSet<ProviderSymbol>();
        var active = new HashSet<ProviderSymbol>();
        var visitedStorage = new HashSet<LinkStorageSymbol>(
            ReferenceEqualityComparer.Instance);

        foreach (ResolvedRoot root in roots)
        {
            if (root.Provider is { } provider)
            {
                VisitProvider(
                    provider,
                    providerSelection,
                    rootAuthorities,
                    edges,
                    complete,
                    active,
                    visitedStorage);
            }
        }

        return edges;
    }

    private static void VisitProvider(
        ProviderBinding provider,
        ProviderSelection providerSelection,
        IReadOnlyDictionary<AssetKey, ProviderBinding> rootAuthorities,
        Dictionary<DependencyEdge, ProviderBinding> edges,
        ISet<ProviderSymbol> complete,
        ISet<ProviderSymbol> active,
        ISet<LinkStorageSymbol> visitedStorage)
    {
        if (complete.Contains(provider.Symbol) || !active.Add(provider.Symbol))
            return;

        provider.Recipe.VisitReferences(
            VisitProviderDependency,
            VisitDependencyOnly,
            _ => { },
            visitedStorage);

        active.Remove(provider.Symbol);
        complete.Add(provider.Symbol);

        void VisitProviderDependency(AssetDependency dependency)
        {
            ProviderBinding dependencyProvider = ResolveDependency(
                provider,
                dependency,
                providerSelection,
                rootAuthorities);
            edges.TryAdd(
                new DependencyEdge(dependency),
                dependencyProvider);
            VisitProvider(
                dependencyProvider,
                providerSelection,
                rootAuthorities,
                edges,
                complete,
                active,
                visitedStorage);
        }

        void VisitDependencyOnly(AssetDependency dependency)
        {
            if (!TryResolveDependency(
                    provider,
                    dependency,
                    providerSelection,
                    rootAuthorities,
                    out ProviderBinding dependencyProvider))
            {
                return;
            }

            edges.TryAdd(
                new DependencyEdge(dependency),
                dependencyProvider);
            VisitProvider(
                dependencyProvider,
                providerSelection,
                rootAuthorities,
                edges,
                complete,
                active,
                visitedStorage);
        }
    }

    private static ProviderBinding ResolveDependency(
        ProviderBinding owner,
        AssetDependency dependency,
        ProviderSelection providerSelection,
        IReadOnlyDictionary<AssetKey, ProviderBinding> rootAuthorities)
    {
        if (!TryResolveDependency(
                owner,
                dependency,
                providerSelection,
                rootAuthorities,
                out ProviderBinding provider))
        {
            throw new InvalidDataException(
                $"{dependency.FieldPath} on provider {owner.Key} depends on " +
                $"{dependency.Key}, but the pool contains no provider.");
        }

        return provider;
    }

    private static bool TryResolveDependency(
        ProviderBinding owner,
        AssetDependency dependency,
        ProviderSelection providerSelection,
        IReadOnlyDictionary<AssetKey, ProviderBinding> rootAuthorities,
        out ProviderBinding provider)
    {
        if (!rootAuthorities.TryGetValue(dependency.Key, out provider) &&
            !providerSelection.Full.TryGetValue(dependency.Key, out provider) &&
            !providerSelection.References.TryGetValue(dependency.Key, out provider))
        {
            return false;
        }

        if (provider.SerializedType != dependency.SerializedType)
        {
            throw new InvalidDataException(
                $"{dependency.FieldPath} on provider {owner.Key} expects " +
                $"dependency {dependency.Key} as " +
                $"{dependency.SerializedType}, but its selected provider uses {provider.SerializedType}.");
        }

        return true;
    }

    private static IReadOnlyDictionary<AssetKey, ProviderBinding>
        BindRootAuthorities(IEnumerable<ResolvedRoot> roots)
    {
        var authorities = new Dictionary<AssetKey, ProviderBinding>();
        foreach (ResolvedRoot root in roots)
        {
            if (root.Provider is not { } provider)
                continue;

            if (authorities.TryGetValue(provider.Key, out ProviderBinding previous) &&
                previous.Symbol != provider.Symbol)
            {
                throw new InvalidDataException(
                    $"Roots select more than one authoritative provider for {provider.Key}.");
            }

            authorities.TryAdd(provider.Key, provider);
        }

        return authorities;
    }

    private static void ValidateLanguageCount(
        int languageCount,
        IEnumerable<ResolvedRoot> roots,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure)
    {
        int requiredCount = languageCount;
        var visitedProviders = new HashSet<ProviderSymbol>();
        var visitedStorage = new HashSet<LinkStorageSymbol>(
            ReferenceEqualityComparer.Instance);
        foreach (ResolvedRoot root in roots)
        {
            if (root.Provider is { } provider)
                Visit(provider);
        }

        void Visit(ProviderBinding provider)
        {
            if (!visitedProviders.Add(provider.Symbol))
                return;

            if (provider.Recipe is SoundLinkRecipe
                {
                    RequiredLanguageCount: { } soundFileCount
                } &&
                soundFileCount != requiredCount)
            {
                throw new InvalidDataException(
                    $"Sound provider {provider.Key} requires {soundFileCount} " +
                    $"SoundFile row(s) per alias, but the zone language count is " +
                    $"{languageCount}.");
            }

            provider.Recipe.VisitReferences(
                VisitDependency,
                VisitDependencyIfAvailable,
                _ => { },
                visitedStorage);

            void VisitDependency(AssetDependency dependency)
            {
                var edge = new DependencyEdge(dependency);
                if (!dependencyClosure.TryGetValue(
                        edge,
                        out ProviderBinding dependencyProvider))
                {
                    throw new InvalidDataException(
                        $"The precomputed closure has no {dependency.FieldPath} edge " +
                        $"from {provider.Key} to {dependency.Key}.");
                }

                Visit(dependencyProvider);
            }

            void VisitDependencyIfAvailable(AssetDependency dependency)
            {
                if (TryResolveClosureEdge(
                        dependency,
                        dependencyClosure,
                        out ProviderBinding dependencyProvider))
                {
                    Visit(dependencyProvider);
                }
            }
        }
    }

    private static AssetRow[] BuildAssetRows(
        IReadOnlyList<LinkRoot> roots,
        IReadOnlyList<ResolvedRoot> resolvedRoots,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure)
    {
        var explicitRootIndex = new Dictionary<ProviderSymbol, int>();
        for (int index = 0; index < resolvedRoots.Count; index++)
        {
            if (resolvedRoots[index].Provider is not { } provider)
                continue;

            if (explicitRootIndex.TryGetValue(
                    provider.Symbol,
                    out int previousIndex))
            {
                throw new InvalidDataException(
                    $"Roots '{roots[previousIndex].EntryId}' and '{roots[index].EntryId}' " +
                    $"both select provider {provider.Key}. Canonical root rows must each " +
                    "own a distinct inline provider definition.");
            }

            explicitRootIndex.Add(provider.Symbol, index);
        }

        var rowRequired = new HashSet<ProviderSymbol>();
        var collectedProviders = new HashSet<ProviderSymbol>();
        var collectedStorage = new HashSet<LinkStorageSymbol>(
            ReferenceEqualityComparer.Instance);
        foreach (ResolvedRoot root in resolvedRoots)
        {
            if (root.Provider is { } provider)
                CollectRowRequirements(provider);
        }

        var rows = new List<AssetRow>();
        var rowProviders = new HashSet<ProviderSymbol>();
        var plannedProviders = new HashSet<ProviderSymbol>();
        var activeProviders = new HashSet<ProviderSymbol>();
        var plannedStorage = new HashSet<LinkStorageSymbol>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < roots.Count; index++)
        {
            LinkRoot root = roots[index];
            if (resolvedRoots[index].Provider is { } provider)
            {
                PlanDependencies(provider, index);
                if (!rowProviders.Add(provider.Symbol))
                {
                    throw new InvalidDataException(
                        $"Root '{root.EntryId}' cannot own an inline row because " +
                        $"provider {provider.Key} was already scheduled.");
                }

                rows.Add(new AssetRow(
                    root.SerializedType,
                    provider,
                    OpaqueHeader: null,
                    $"root '{root.EntryId}'"));
            }
            else
            {
                rows.Add(new AssetRow(
                    root.SerializedType,
                    Provider: null,
                    resolvedRoots[index].OpaqueHeader,
                    $"root '{root.EntryId}'"));
            }
        }

        foreach (ProviderSymbol required in rowRequired)
        {
            if (!rowProviders.Contains(required))
            {
                throw new InvalidDataException(
                    "The selected closure contains an indirect asset dependency " +
                    "without a canonical XAsset row.");
            }
        }

        return rows.ToArray();

        void CollectRowRequirements(ProviderBinding provider)
        {
            if (!collectedProviders.Add(provider.Symbol))
                return;

            provider.Recipe.VisitReferences(
                VisitProviderDependency,
                VisitIndirectDependency,
                _ => { },
                collectedStorage);

            void VisitProviderDependency(AssetDependency dependency) =>
                CollectRowRequirements(ResolveClosureEdge(
                    provider,
                    dependency,
                    dependencyClosure));

            void VisitIndirectDependency(AssetDependency dependency)
            {
                if (!TryResolveClosureEdge(
                    dependency,
                    dependencyClosure,
                    out ProviderBinding target))
                {
                    return;
                }

                rowRequired.Add(target.Symbol);
                CollectRowRequirements(target);
            }
        }

        void PlanDependencies(ProviderBinding provider, int currentRootIndex)
        {
            if (!activeProviders.Add(provider.Symbol))
                return;
            if (!plannedProviders.Add(provider.Symbol))
            {
                activeProviders.Remove(provider.Symbol);
                return;
            }

            provider.Recipe.VisitReferences(
                PlanProviderDependency,
                PlanDependencyIfAvailable,
                _ => { },
                plannedStorage);
            activeProviders.Remove(provider.Symbol);

            void PlanProviderDependency(AssetDependency dependency) =>
                PlanDependency(ResolveClosureEdge(
                    provider,
                    dependency,
                    dependencyClosure));

            void PlanDependencyIfAvailable(AssetDependency dependency)
            {
                if (TryResolveClosureEdge(
                        dependency,
                        dependencyClosure,
                        out ProviderBinding target))
                {
                    // A name-only dependency has no native provider-pointer
                    // cell to publish before its owner. Preserve an explicit
                    // root's stock position; only synthesize a row here when
                    // the selected roots do not already own that provider.
                    if (explicitRootIndex.ContainsKey(target.Symbol))
                        return;

                    PlanDependency(target);
                }
            }

            void PlanDependency(ProviderBinding target)
            {
                if (explicitRootIndex.TryGetValue(
                        target.Symbol,
                        out int targetRootIndex) &&
                    targetRootIndex > currentRootIndex)
                {
                    throw new InvalidDataException(
                        $"Root '{roots[currentRootIndex].EntryId}' reaches provider " +
                        $"{target.Key} before its later root " +
                        $"'{roots[targetRootIndex].EntryId}'. Move the dependency " +
                        "root before its owner, or remove it as an explicit root, " +
                        "so every canonical top-level provider row remains inline.");
                }

                PlanDependencies(target, currentRootIndex);
                if (rowRequired.Contains(target.Symbol) &&
                    !explicitRootIndex.ContainsKey(target.Symbol) &&
                    rowProviders.Add(target.Symbol))
                {
                    rows.Add(new AssetRow(
                        target.SerializedType,
                        target,
                        OpaqueHeader: null,
                        $"indirect dependency {target.Key}"));
                }
            }
        }
    }

    private static void ValidateAssetRowPublicationOrder(
        IReadOnlyList<AssetRow> rows,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure)
    {
        var rowIndexByProvider = new Dictionary<ProviderSymbol, int>();
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Provider is { } provider)
                rowIndexByProvider.Add(provider.Symbol, index);
        }

        var encountered = new HashSet<ProviderSymbol>();
        var visitedStorage = new HashSet<LinkStorageSymbol>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Provider is { } provider)
            {
                VisitPublicationOrder(
                    provider,
                    index,
                    rows,
                    rowIndexByProvider,
                    dependencyClosure,
                    encountered,
                    visitedStorage);
            }
        }
    }

    private static void VisitPublicationOrder(
        ProviderBinding provider,
        int currentRowIndex,
        IReadOnlyList<AssetRow> rows,
        IReadOnlyDictionary<ProviderSymbol, int> rootIndexByProvider,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure,
        ISet<ProviderSymbol> encountered,
        ISet<LinkStorageSymbol> visitedStorage)
    {
        if (encountered.Contains(provider.Symbol))
            return;

        if (rootIndexByProvider.TryGetValue(
                provider.Symbol,
                out int providerRootIndex) &&
            providerRootIndex > currentRowIndex)
        {
            throw new InvalidDataException(
                $"Asset row {rows[currentRowIndex].Description} reaches provider " +
                $"{provider.Key} before its later {rows[providerRootIndex].Description}. " +
                "The stock-compatible profile requires every XAsset row to own an " +
                "inline provider definition.");
        }

        encountered.Add(provider.Symbol);
        provider.Recipe.VisitReferences(
            dependency =>
            {
                var edge = new DependencyEdge(dependency);
                if (!dependencyClosure.TryGetValue(
                        edge,
                        out ProviderBinding dependencyProvider))
                {
                    throw new InvalidDataException(
                        $"The precomputed closure has no {dependency.FieldPath} edge " +
                        $"from {provider.Key} to {dependency.Key}.");
                }

                VisitPublicationOrder(
                    dependencyProvider,
                    currentRowIndex,
                    rows,
                    rootIndexByProvider,
                    dependencyClosure,
                    encountered,
                    visitedStorage);
            },
            dependency =>
            {
                if (!TryResolveClosureEdge(
                        dependency,
                        dependencyClosure,
                        out ProviderBinding dependencyProvider))
                {
                    return;
                }

                if (!rootIndexByProvider.ContainsKey(dependencyProvider.Symbol))
                {
                    throw new InvalidDataException(
                        $"{dependency.FieldPath} on provider {provider.Key} names " +
                        $"indirect dependency {dependencyProvider.Key}, but that " +
                        "provider has no canonical XAsset row.");
                }
            },
            _ => { },
            visitedStorage);
    }

    private static ProviderBinding ResolveClosureEdge(
        ProviderBinding owner,
        AssetDependency dependency,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure)
    {
        var edge = new DependencyEdge(dependency);
        if (!dependencyClosure.TryGetValue(edge, out ProviderBinding provider))
        {
            throw new InvalidDataException(
                $"The precomputed closure has no {dependency.FieldPath} edge " +
                $"from {owner.Key} to {dependency.Key}.");
        }

        return provider;
    }

    private static bool TryResolveClosureEdge(
        AssetDependency dependency,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure,
        out ProviderBinding provider) =>
        dependencyClosure.TryGetValue(new DependencyEdge(dependency), out provider);

    private static void EncounterProvider(
        ProviderBinding provider,
        XBlockAddress providerCell,
        int providerCellSourceOffset,
        LinkStorageEmissionState storageState,
        IDictionary<ProviderSymbol, XBlockAddress> publications,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure,
        IDictionary<uint, List<DbHeaderImageStreamEntry>> imageStreams)
    {
        ZoneEmissionWriter output = storageState.Output;
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
        CollectImageStreams(provider, imageStreams);

        provider.Recipe.Emit(new LinkEmissionContext(
            storageState,
            (dependency, dependencyCell, dependencySourceOffset) =>
            {
                var edge = new DependencyEdge(dependency);
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
                    storageState,
                    publications,
                    dependencyClosure,
                    imageStreams);
            }));
    }

    private static Dictionary<uint, List<DbHeaderImageStreamEntry>>
        CreateImageStreamCollectors(uint languageMask)
    {
        var result = new Dictionary<uint, List<DbHeaderImageStreamEntry>>();
        for (int bitIndex = 0; bitIndex < DbLanguageMask.BitCount; bitIndex++)
        {
            uint bit = 1u << bitIndex;
            if ((languageMask & bit) != 0)
                result.Add(bit, []);
        }

        return result;
    }

    private static void CollectImageStreams(
        ProviderBinding provider,
        IDictionary<uint, List<DbHeaderImageStreamEntry>> streams)
    {
        if (provider.Recipe is not GfxImageLinkRecipe image ||
            image.StreamReferences.Count == 0)
        {
            return;
        }

        Dictionary<uint, ImageFileStreamLanguageReferences> byLanguage =
            image.StreamReferences.ToDictionary(references => references.LanguageMask);
        if (byLanguage.Count != streams.Count ||
            streams.Keys.Any(mask => !byLanguage.ContainsKey(mask)) ||
            byLanguage.Keys.Any(mask => !streams.ContainsKey(mask)))
        {
            throw new InvalidDataException(
                $"Streamed GfxImage provider {provider.Key} does not carry exactly " +
                "the zone's requested language tables.");
        }

        foreach ((uint mask, List<DbHeaderImageStreamEntry> destination) in streams)
        {
            destination.AddRange(byLanguage[mask].References
                .Select(reference => reference.Entry));
        }
    }

    private static ScriptStringTable BuildScriptStringTable(
        IEnumerable<AssetRow> rows,
        IReadOnlyDictionary<DependencyEdge, ProviderBinding> dependencyClosure)
    {
        var values = new List<string?>();
        var indices = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var visited = new HashSet<ProviderSymbol>();
        var visitedStorage = new HashSet<LinkStorageSymbol>(
            ReferenceEqualityComparer.Instance);

        foreach (AssetRow row in rows)
        {
            if (row.Provider is { } provider)
                Visit(provider);
        }

        return new ScriptStringTable(
            Array.AsReadOnly(values.ToArray()),
            indices);

        void Visit(ProviderBinding provider)
        {
            if (!visited.Add(provider.Symbol))
                return;

            provider.Recipe.VisitReferences(
                VisitDependency,
                VisitDependencyIfAvailable,
                script =>
                {
                    if (script.Text is null || indices.ContainsKey(script.Text))
                        return;
                    if (values.Count == 0)
                        values.Add(null);
                    if (values.Count > ushort.MaxValue)
                    {
                        throw new InvalidDataException(
                            "The selected asset graph exceeds the 16-bit script-string index range.");
                    }

                    ushort index = checked((ushort)values.Count);
                    indices.Add(script.Text, index);
                    values.Add(script.Text);
                },
                visitedStorage);

            void VisitDependency(AssetDependency dependency)
            {
                var edge = new DependencyEdge(dependency);
                if (!dependencyClosure.TryGetValue(
                        edge,
                        out ProviderBinding dependencyProvider))
                {
                    throw new InvalidDataException(
                        $"The precomputed closure has no {dependency.FieldPath} edge " +
                        $"from {provider.Key} to {dependency.Key}.");
                }

                Visit(dependencyProvider);
            }

            void VisitDependencyIfAvailable(AssetDependency dependency)
            {
                if (TryResolveClosureEdge(
                        dependency,
                        dependencyClosure,
                        out ProviderBinding dependencyProvider))
                {
                    Visit(dependencyProvider);
                }
            }
        }
    }

    private static LinkStorageSymbol CreateScriptStringStorage(
        IReadOnlyList<string?> values)
    {
        LinkStorageSymbol?[] strings = values
            .Select((value, index) => value is null
                ? null
                : LinkStorageSymbol.CString(
                    value,
                    $"XAssetList.ScriptStrings[{index}]"))
            .ToArray();
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            new byte[checked(values.Count * sizeof(int))],
            alignment: 4,
            table => strings
                .Select((value, index) => (value, index))
                .Where(item => item.value is not null)
                .Select(item => new XStringLinkOperation(
                    new LinkStorageCell(
                        table,
                        checked(item.index * sizeof(int))),
                    LinkStorageView.Whole(item.value!),
                    CanMaterializeRoot: true,
                    $"XAssetList.ScriptStrings[{item.index}]")));
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

    private readonly record struct ResolvedRoot(
        ProviderBinding? Provider,
        int? OpaqueHeader);

    private readonly record struct AssetRow(
        XAssetType SerializedType,
        ProviderBinding? Provider,
        int? OpaqueHeader,
        string Description);

    private readonly record struct DependencyEdge(AssetDependency Dependency);

    private readonly record struct ScriptStringTable(
        IReadOnlyList<string?> Values,
        IReadOnlyDictionary<string, ushort> Indices);
}
