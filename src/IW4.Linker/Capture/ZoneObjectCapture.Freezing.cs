using System.Buffers.Binary;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Plans;

namespace IW4.Linker;

public sealed partial class ZoneObjectCapture
{
    public ZoneObjectFile Freeze()
    {
        ThrowIfFrozen();
        _frozen = true;
        if (_activeTempEpochs.Count != 1 || _activeTempEpochs.Peek() != RootTempEpoch)
            throw new InvalidDataException("TEMP lifetime stack was not balanced before freezing the zone object.");
        LifetimeFor(RootTempEpoch).EndSequence = long.MaxValue;
        if (_unboundInlineBindingsByTarget.Count != 0)
            throw new InvalidDataException("An inline pointer has no subsequent materialization occurrence.");
        if (_inlineBindingByPointer.Values.Any(value =>
                XPointerCodec.GetType(value.Pointer!.Raw) == PointerType.Insert &&
                value.InsertCell is null))
        {
            throw new InvalidDataException("An insert pointer was not associated with its durable LARGE cell.");
        }
        if (_unclaimedInsertCells.Count != 0)
            throw new InvalidDataException("A durable insert pointer cell was never associated with a -2 source pointer.");

        FinalizeZeroByteTargets();
        ValidateLayout();
        var allocationSymbols = _allocations.Select(value => new AllocationSymbol(value.Event)).ToArray();
        var allocationSymbolByOccurrence = allocationSymbols.ToDictionary(value => value.Occurrence);
        AllocationSymbol SymbolFor(CapturedAllocation allocation) =>
            allocationSymbolByOccurrence[allocation.Event.Occurrence];
        var boundarySymbols = _boundaries.Select(value => new BoundarySymbol(value.Event)).ToArray();
        var boundarySymbolByOccurrence = boundarySymbols.ToDictionary(value => value.Occurrence);
        BoundarySymbol BoundarySymbolFor(CapturedBoundary boundary) =>
            boundarySymbolByOccurrence[boundary.Event.Occurrence];

        var stringSymbols = new List<XStringSymbol>(_strings.Count);
        var stringByAllocationOccurrence = new Dictionary<CaptureOccurrence, XStringSymbol>();
        foreach (CapturedXString text in _strings)
        {
            var symbol = new XStringSymbol(text.Occurrence, SymbolFor(text.Allocation));
            if (!stringByAllocationOccurrence.TryAdd(text.Allocation.Event.Occurrence, symbol))
                throw new InvalidDataException("One materialization occurrence was marked as more than one XString.");
            stringSymbols.Add(symbol);
        }

        var localProviders = new List<LocalAssetProviderSymbol>(_providers.Count);
        var providersByRuntimeId = new Dictionary<long, LocalAssetProviderSymbol>();
        foreach (CapturedProvider provider in _providers)
        {
            AllocationReference cell = ReferenceForAllocation(
                ResolveConcreteOwner(provider.ProviderCell, "provider cell"),
                provider.ProviderCell.Offset,
                false,
                SymbolFor);
            var local = new LocalAssetProviderSymbol(
                provider.Occurrence,
                cell,
                SymbolFor(provider.Materialization));
            if (!providersByRuntimeId.TryAdd(provider.IncomingRuntimeId, local))
                throw new InvalidDataException("A provider correlation identity was registered more than once in one zone object.");
            localProviders.Add(local);
        }

        var imports = new List<ImportedAssetProviderSymbol>();
        var importByRuntimeId = new Dictionary<long, ImportedAssetProviderSymbol>();
        AssetProviderSymbol ActiveProviderFor(CapturedProvider provider)
        {
            if (providersByRuntimeId.TryGetValue(provider.ActiveRuntimeId, out LocalAssetProviderSymbol? local))
                return local;
            if (!importByRuntimeId.TryGetValue(provider.ActiveRuntimeId, out ImportedAssetProviderSymbol? imported))
            {
                imported = new ImportedAssetProviderSymbol(NextOccurrence());
                importByRuntimeId.Add(provider.ActiveRuntimeId, imported);
                imports.Add(imported);
            }
            return imported;
        }

        var selections = new List<AssetProviderSelectionEvent>(_providers.Count);
        var selectionBySource = new Dictionary<CaptureOccurrence, AssetProviderSelectionEvent>();
        var providerByCell = new Dictionary<PhysicalKey, LocalAssetProviderSymbol>();
        foreach (CapturedProvider provider in _providers)
        {
            var selection = new AssetProviderSelectionEvent(
                NextOccurrence(),
                providersByRuntimeId[provider.IncomingRuntimeId],
                ActiveProviderFor(provider));
            selections.Add(selection);
            selectionBySource.Add(provider.SourcePointer.Occurrence, selection);
            if (!providerByCell.TryAdd(provider.ProviderCell, selection.Incoming))
                throw new InvalidDataException("More than one local provider claims the same durable provider cell.");
        }

        var aliasSymbols = new List<AliasCellSymbol>();
        var aliasByAllocationAndOffset = new Dictionary<(CaptureOccurrence, int), AliasCellSymbol>();
        AliasCellSymbol AliasFor(CapturedAllocation allocation, int addressOffset)
        {
            var key = (allocation.Event.Occurrence, addressOffset);
            if (aliasByAllocationAndOffset.TryGetValue(key, out AliasCellSymbol? existing))
                return existing;
            var alias = new AliasCellSymbol(
                NextOccurrence(),
                SymbolFor(allocation),
                checked(addressOffset - allocation.Event.DestinationOffset));
            aliasByAllocationAndOffset.Add(key, alias);
            aliasSymbols.Add(alias);
            return alias;
        }

        var seenTapeOffsets = new HashSet<int>();
        var relocations = new List<PointerRelocation>(_pointers.Count);
        foreach (CapturedPointer pointer in _pointers)
        {
            int tapeOffset = pointer.TapeOffset ?? (pointer.Cell is { } cell ? FindTapeOffset(cell.Address, cell.TempEpoch) : null)
                ?? throw new InvalidDataException($"Pointer occurrence {pointer.Occurrence.Value} has no unique decoded-tape coordinate.");
            if (tapeOffset < 0 || tapeOffset > _tape.Length - sizeof(int) || !seenTapeOffsets.Add(tapeOffset))
                throw new InvalidDataException($"Pointer occurrence {pointer.Occurrence.Value} has a duplicate or invalid relocation tape cell.");
            if (BinaryPrimitives.ReadInt32BigEndian(_tape.AsSpan(tapeOffset, sizeof(int))) != pointer.Raw)
                throw new InvalidDataException($"Pointer occurrence {pointer.Occurrence.Value} is not idempotent with its decoded tape word.");

            AllocationReference? source = pointer.Cell is { } sourceCell
                ? ReferenceForAllocation(ResolveConcreteOwner(sourceCell, "pointer source"), sourceCell.Offset, false, SymbolFor)
                : null;
            PointerType pointerType = XPointerCodec.GetType(pointer.Raw);
            SerializedPointerForm form = FormFor(pointerType, pointer.ResolutionMode);
            SymbolReference? target = TargetFor(pointer, pointerType);
            AliasCellReference? publicationCell = PublicationCellFor(
                pointer,
                pointerType);
            relocations.Add(new PointerRelocation(
                pointer.Occurrence,
                tapeOffset,
                sizeof(int),
                SerializedByteOrder.BigEndian,
                pointer.Raw,
                form,
                pointer.ResolutionMode,
                source,
                pointer.TemporalEpoch,
                source?.Symbol.Allocation.TempEpoch,
                TargetLifetime(target),
                target,
                publicationCell));
        }

        return new ZoneObjectFile(
            _tape,
            _layout,
            _allocations.Select(value => value.Event),
            _tempLifetimes.Select(value => new TempLifetime(value.Epoch, value.BeginSequence, value.EndSequence)),
            allocationSymbols,
            localProviders,
            imports,
            selections,
            stringSymbols,
            aliasSymbols,
            boundarySymbols,
            relocations);

        SymbolReference? TargetFor(CapturedPointer pointer, PointerType pointerType)
        {
            if (pointerType == PointerType.Null)
                return null;
            if (selectionBySource.TryGetValue(pointer.Occurrence, out AssetProviderSelectionEvent? selection))
            {
                return new AssetProviderReference(selection.Incoming);
            }
            if (pointerType is PointerType.Inline or PointerType.Insert)
            {
                CapturedAllocation allocation = pointer.InlineTarget
                    ?? throw new InvalidDataException($"Inline pointer occurrence {pointer.Occurrence.Value} has no materialization occurrence.");
                return ReferenceFor(SymbolFor(allocation), allocation.Event.DestinationOffset, false, stringByAllocationOccurrence);
            }

            XBlockAddress targetAddress = XPointerCodec.Decode(pointer.Raw);
            if (pointer.ResolutionMode == XPointerResolutionMode.AliasCell)
            {
                CapturedAllocation aliasOwner = ResolvePackedTarget(pointer, targetAddress, sizeof(int), false);
                PhysicalKey aliasCell = PhysicalKey.For(targetAddress, aliasOwner.Event.TempEpoch);
                if (providerByCell.TryGetValue(aliasCell, out LocalAssetProviderSymbol? provider))
                    return new AssetProviderReference(provider);
                return new AliasCellReference(AliasFor(aliasOwner, targetAddress.Offset));
            }

            if (pointer.ValidatedTarget is { Length: 0 } zeroView)
            {
                if (zeroView.Address != targetAddress)
                    throw new InvalidDataException("Validated target address differs from the encoded pointer target.");
                if (zeroView.Owner is { } owner)
                    return ReferenceFor(SymbolFor(owner), targetAddress.Offset, true, stringByAllocationOccurrence);
                return new BoundaryReference(BoundarySymbolFor(zeroView.Boundary
                    ?? throw new InvalidDataException("Zero-byte target has no bound occurrence identity.")));
            }

            CapturedAllocation target = ResolvePackedTarget(pointer, targetAddress, requiredLength: 1, allowEnd: false);
            return ReferenceFor(SymbolFor(target), targetAddress.Offset, false, stringByAllocationOccurrence);
        }

        AliasCellReference? PublicationCellFor(
            CapturedPointer pointer,
            PointerType pointerType)
        {
            if (pointer.ResolutionMode != XPointerResolutionMode.AliasCell ||
                selectionBySource.ContainsKey(pointer.Occurrence))
            {
                return null;
            }

            PhysicalKey? publicationCell = pointerType switch
            {
                PointerType.Inline => pointer.Cell,
                PointerType.Insert => pointer.InsertCell,
                _ => null
            };
            if (publicationCell is not { } cell)
                return null;

            CapturedAllocation owner = ResolveConcreteOwner(
                cell,
                "alias publication cell");
            return new AliasCellReference(AliasFor(owner, cell.Offset));
        }
    }
}
