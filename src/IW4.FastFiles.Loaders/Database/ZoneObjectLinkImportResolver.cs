using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using IW4.Linker.Model;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Resolves retained loader pointer occurrences against one frozen symbolic
/// zone object. Physical coordinates are consumed only as occurrence lookup
/// data and never become identities in the canonical link request.
/// </summary>
internal sealed class ZoneObjectLinkImportResolver : ILinkAssetImportResolver
{
    private readonly LinkAssetImportIdentityScope _identityScope = new();
    private readonly Dictionary<BaseAsset, LocalAssetProviderSymbol> _providers;
    private readonly Dictionary<LocalAssetProviderSymbol, HashSet<AllocationSymbol>>
        _reachableAllocations = [];
    private readonly IReadOnlyDictionary<CaptureOccurrence, PointerRelocation>
        _relocationsByOccurrence;
    private readonly Dictionary<PointerSourceKey, List<PointerRelocation>>
        _relocationsBySource = [];
    private readonly Dictionary<AllocationSymbol, List<AllocationSymbol>>
        _allocationEdges = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AllocationSymbol, Dictionary<int, List<PointerRelocation>>>
        _relocationsByAllocationSource = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AllocationLocationKey, List<AllocationSymbol>>
        _allocationsByLocation = [];
    private readonly Dictionary<AliasCellSymbol, List<PointerRelocation>>
        _relocationsByPublication = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AliasCellSymbol> _aliases;

    public ZoneObjectLinkImportResolver(
        ZoneObjectFile objectFile,
        IReadOnlyDictionary<BaseAsset, CaptureOccurrence> providerOccurrences)
    {
        ArgumentNullException.ThrowIfNull(objectFile);
        ArgumentNullException.ThrowIfNull(providerOccurrences);

        Dictionary<CaptureOccurrence, LocalAssetProviderSymbol> symbols =
            objectFile.LocalAssetProviders.ToDictionary(provider => provider.Occurrence);
        _providers = new Dictionary<BaseAsset, LocalAssetProviderSymbol>(
            ReferenceEqualityComparer.Instance);
        foreach ((BaseAsset provider, CaptureOccurrence occurrence) in providerOccurrences)
        {
            if (!symbols.TryGetValue(occurrence, out LocalAssetProviderSymbol? symbol))
            {
                throw new InvalidDataException(
                    $"Provider capture occurrence {occurrence.Value} has no frozen local-provider symbol.");
            }
            if (!_providers.TryAdd(provider, symbol))
                throw new InvalidDataException("A provider object has competing frozen symbols.");
        }

        if (_providers.Count != objectFile.LocalAssetProviders.Count)
        {
            throw new InvalidDataException(
                "Loader provider occurrences do not cover every frozen local-provider symbol.");
        }

        _aliases = new HashSet<AliasCellSymbol>(
            objectFile.AliasCells,
            ReferenceEqualityComparer.Instance);
        var providerMaterializations = new HashSet<AllocationSymbol>(
            objectFile.LocalAssetProviders.Select(provider => provider.Materialization),
            ReferenceEqualityComparer.Instance);

        var relocationsByOccurrence = new Dictionary<CaptureOccurrence, PointerRelocation>();
        foreach (PointerRelocation relocation in objectFile.Relocations)
        {
            relocationsByOccurrence.Add(relocation.Occurrence, relocation);

            if (relocation.PublicationCell is { } publication)
            {
                AddCandidate(
                    _relocationsByPublication,
                    publication.Symbol,
                    relocation);
            }

            if (relocation.Source is not { } source)
                continue;

            AddCandidate(
                _relocationsBySource,
                new PointerSourceKey(
                    SourceAddress(source),
                    relocation.CapturedRaw,
                    relocation.ResolutionMode),
                relocation);

            if (providerMaterializations.Contains(source.Symbol))
            {
                if (!_relocationsByAllocationSource.TryGetValue(
                        source.Symbol,
                        out Dictionary<int, List<PointerRelocation>>? byAddend))
                {
                    byAddend = [];
                    _relocationsByAllocationSource.Add(source.Symbol, byAddend);
                }
                AddCandidate(byAddend, source.Addend, relocation);
            }

            AllocationSymbol? target = TargetAllocation(relocation.Target);
            if (target is not null)
                AddCandidate(_allocationEdges, source.Symbol, target);
        }
        _relocationsByOccurrence = relocationsByOccurrence;

        foreach (AllocationSymbol allocation in objectFile.Allocations)
        {
            AllocationEvent materialization = allocation.Allocation;
            if (materialization.Length <= 0)
                continue;

            AddCandidate(
                _allocationsByLocation,
                new AllocationLocationKey(
                    materialization.DestinationBlock,
                    materialization.TempEpoch,
                    materialization.DestinationOffset),
                allocation);
        }
    }

    public LinkAssetImportIdentityScope IdentityScope => _identityScope;

    public PointerRelocation ResolvePointer(
        BaseAsset provider,
        XPointerReference pointer,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        XBlockAddress cell = pointer.CellAddress
            ?? throw new NotSupportedException(
                $"{fieldPath} has no captured pointer-cell occurrence.");
        HashSet<AllocationSymbol>? reachable = cell.BlockType == XFileBlockType.TEMP
            ? ReachableAllocations(ProviderFor(provider, fieldPath))
            : null;

        _relocationsBySource.TryGetValue(
            new PointerSourceKey(cell, pointer.Raw, pointer.ResolutionMode),
            out List<PointerRelocation>? matches);
        IReadOnlyList<PointerRelocation> candidates = matches ?? [];
        return reachable is null
            ? RequireUnique(candidates, fieldPath, "captured pointer relocation")
            : RequireUniqueReachable(
                candidates,
                reachable,
                fieldPath,
                "captured pointer relocation");
    }

    public PointerRelocation ResolveProviderRootPointer(
        BaseAsset provider,
        int rootRelativeOffset,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        LocalAssetProviderSymbol providerSymbol = ProviderFor(provider, fieldPath);
        AllocationEvent root = providerSymbol.Materialization.Allocation;
        if (rootRelativeOffset < 0 ||
            rootRelativeOffset > root.Length - sizeof(int))
        {
            throw new InvalidDataException(
                $"{fieldPath} root pointer cell +0x{rootRelativeOffset:X} lies outside its provider body.");
        }

        List<PointerRelocation>? matches = null;
        if (_relocationsByAllocationSource.TryGetValue(
                providerSymbol.Materialization,
                out Dictionary<int, List<PointerRelocation>>? byAddend))
        {
            byAddend.TryGetValue(rootRelativeOffset, out matches);
        }
        return RequireUnique(
            matches ?? [],
            fieldPath,
            "provider-root pointer relocation");
    }

    public IReadOnlyList<AllocationReference> ResolveDirectStorageRange(
        PointerRelocation pointer,
        int byteLength,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        if (byteLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        PointerRelocation capturedPointer = RequireCaptured(pointer, fieldPath);
        if (capturedPointer.Target is not AllocationReference first)
        {
            throw new InvalidDataException(
                $"{fieldPath} does not target captured direct storage.");
        }

        AllocationEvent firstEvent = first.Symbol.Allocation;
        if (first.Addend < 0 || first.Addend >= firstEvent.Length)
            throw new InvalidDataException($"{fieldPath} has an invalid captured storage addend.");

        var result = new List<AllocationReference> { first };
        int covered = Math.Min(byteLength, firstEvent.Length - first.Addend);
        int nextOffset = checked(firstEvent.DestinationOffset + firstEvent.Length);
        while (covered < byteLength)
        {
            _allocationsByLocation.TryGetValue(
                new AllocationLocationKey(
                    firstEvent.DestinationBlock,
                    firstEvent.TempEpoch,
                    nextOffset),
                out List<AllocationSymbol>? next);
            AllocationSymbol segment = RequireUnique(
                next ?? [],
                fieldPath,
                $"contiguous captured segment at {firstEvent.DestinationBlock}:0x{nextOffset:X}");
            result.Add(new AllocationReference(segment));
            int used = Math.Min(
                byteLength - covered,
                segment.Allocation.Length);
            covered = checked(covered + used);
            nextOffset = checked(
                segment.Allocation.DestinationOffset +
                segment.Allocation.Length);
        }

        return result.AsReadOnly();
    }

    public AliasCellSymbol ResolveAliasCell(
        PointerRelocation pointer,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        PointerRelocation capturedPointer = RequireCaptured(pointer, fieldPath);
        if (capturedPointer.PublicationCell is { } publication)
            return RequireAlias(publication.Symbol, fieldPath);
        if (capturedPointer.Target is AliasCellReference reference)
            return RequireAlias(reference.Symbol, fieldPath);

        throw new InvalidDataException(
            $"{fieldPath} has no captured alias publication identity.");
    }

    public SymbolReference? ResolveAliasCellValue(
        AliasCellSymbol aliasCell,
        string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        AliasCellSymbol capturedAlias = RequireAlias(aliasCell, fieldPath);
        _relocationsByPublication.TryGetValue(
            capturedAlias,
            out List<PointerRelocation>? matches);
        return RequireUnique(
            matches ?? [],
            fieldPath,
            "captured alias-cell value").Target;
    }

    private LocalAssetProviderSymbol ProviderFor(
        BaseAsset provider,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _providers.TryGetValue(provider, out LocalAssetProviderSymbol? symbol)
            ? symbol
            : throw new InvalidDataException(
                $"{fieldPath} provider does not belong to this zone capture.");
    }

    private HashSet<AllocationSymbol> ReachableAllocations(
        LocalAssetProviderSymbol provider)
    {
        if (_reachableAllocations.TryGetValue(provider, out HashSet<AllocationSymbol>? cached))
            return cached;

        var reachable = new HashSet<AllocationSymbol>(ReferenceEqualityComparer.Instance)
        {
            provider.Materialization
        };
        var pending = new Queue<AllocationSymbol>();
        pending.Enqueue(provider.Materialization);
        while (pending.TryDequeue(out AllocationSymbol? source))
        {
            if (!_allocationEdges.TryGetValue(
                    source,
                    out List<AllocationSymbol>? targets))
                continue;

            foreach (AllocationSymbol target in targets)
            {
                if (reachable.Add(target))
                    pending.Enqueue(target);
            }
        }

        _reachableAllocations.Add(provider, reachable);
        return reachable;
    }

    private PointerRelocation RequireCaptured(
        PointerRelocation pointer,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        if (!_relocationsByOccurrence.TryGetValue(
                pointer.Occurrence,
                out PointerRelocation? captured) ||
            captured != pointer)
        {
            throw new InvalidDataException(
                $"{fieldPath} pointer relocation does not belong to this zone capture.");
        }

        return captured;
    }

    private AliasCellSymbol RequireAlias(
        AliasCellSymbol alias,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return _aliases.Contains(alias)
            ? alias
            : throw new InvalidDataException(
                $"{fieldPath} alias cell does not belong to this zone capture.");
    }

    private static AllocationSymbol? TargetAllocation(SymbolReference? target) =>
        target switch
        {
            AllocationReference allocation => allocation.Symbol,
            XStringReference text => text.Symbol.Allocation,
            AliasCellReference alias => alias.Symbol.Allocation,
            _ => null
        };

    private static void AddCandidate<TKey, TValue>(
        Dictionary<TKey, List<TValue>> index,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out List<TValue>? candidates))
        {
            candidates = [];
            index.Add(key, candidates);
        }
        candidates.Add(value);
    }

    private static XBlockAddress SourceAddress(AllocationReference source) =>
        new(
            source.Symbol.Allocation.DestinationBlock,
            checked(source.Symbol.Allocation.DestinationOffset + source.Addend));

    private static T RequireUnique<T>(
        IReadOnlyList<T> matches,
        string fieldPath,
        string role)
    {
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"{fieldPath} has {matches.Count} {role} candidates; exact occurrence identity is required.");
        }

        return matches[0];
    }

    private static PointerRelocation RequireUniqueReachable(
        IReadOnlyList<PointerRelocation> candidates,
        HashSet<AllocationSymbol> reachable,
        string fieldPath,
        string role)
    {
        PointerRelocation? match = null;
        int count = 0;
        foreach (PointerRelocation candidate in candidates)
        {
            if (candidate.Source is not { } source ||
                !reachable.Contains(source.Symbol))
            {
                continue;
            }

            match = candidate;
            count++;
        }

        if (count != 1)
        {
            throw new InvalidDataException(
                $"{fieldPath} has {count} {role} candidates; exact occurrence identity is required.");
        }

        return match!;
    }

    private readonly record struct PointerSourceKey(
        XBlockAddress Address,
        int CapturedRaw,
        XPointerResolutionMode ResolutionMode);

    private readonly record struct AllocationLocationKey(
        XFileBlockType Block,
        long TempEpoch,
        int DestinationOffset);
}
