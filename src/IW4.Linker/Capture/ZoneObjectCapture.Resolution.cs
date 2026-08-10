using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Model;

namespace IW4.Linker;

public sealed partial class ZoneObjectCapture
{
    private static SerializedPointerForm FormFor(PointerType pointerType, XPointerResolutionMode mode) => pointerType switch
    {
        PointerType.Null => SerializedPointerForm.Null,
        PointerType.Inline => SerializedPointerForm.Inline,
        PointerType.Insert => SerializedPointerForm.Insert,
        PointerType.Offset when mode == XPointerResolutionMode.AliasCell => SerializedPointerForm.PackedAlias,
        PointerType.Offset => SerializedPointerForm.PackedDirect,
        _ => throw new InvalidDataException("Unknown serialized pointer form.")
    };

    private CapturedAllocation ResolvePackedTarget(
        CapturedPointer pointer,
        XBlockAddress address,
        int requiredLength,
        bool allowEnd)
    {
        ValidatedTarget? validated = pointer.ValidatedTarget;
        if (validated is { } value && value.Address != address)
            throw new InvalidDataException("Validated target address differs from the encoded pointer target.");
        CapturedAllocation? match = _allocationIndex.FindUniqueLiveRange(
            address,
            requiredLength,
            allowEnd,
            pointer.Occurrence.Value,
            pointer.TemporalEpoch,
            _tempLifetimeByEpoch);
        return match is not null
            ? match
            : throw new InvalidDataException($"Packed target {address} has no unique live allocation occurrence at pointer event {pointer.Occurrence.Value}.");
    }

    private TempLifetimeRecord LifetimeFor(long epoch) =>
        _tempLifetimeByEpoch.TryGetValue(epoch, out TempLifetimeRecord? lifetime)
            ? lifetime
            : throw new InvalidDataException($"TEMP lifetime {epoch} was not recorded.");

    private void FinalizeZeroByteTargets()
    {
        foreach (CapturedPointer pointer in _pointers)
        {
            if (pointer.ValidatedTarget is not { Length: 0 } validation ||
                validation.Owner is not null ||
                validation.Boundary is not null)
            {
                continue;
            }

            CapturedAllocation? owner = _allocationIndex.FindUniqueLiveRange(
                validation.Address,
                length: 0,
                allowEnd: true,
                pointer.Occurrence.Value,
                pointer.TemporalEpoch,
                _tempLifetimeByEpoch);
            CapturedBoundary? boundary = null;
            if (owner is null)
            {
                CaptureOccurrence occurrence = validation.BoundaryOccurrence
                    ?? throw new InvalidDataException("Zero-byte target validation has no reserved boundary identity.");
                boundary = new CapturedBoundary(new BoundaryEvent(
                    occurrence,
                    validation.Address.BlockType,
                    validation.Address.Offset,
                    validation.TargetTempEpoch));
                _boundaries.Add(boundary);
            }

            pointer.ValidatedTarget = validation with { Owner = owner, Boundary = boundary };
        }
    }

    private static AllocationReference ReferenceForAllocation(
        CapturedAllocation allocation,
        int addressOffset,
        bool allowsEnd,
        Func<CapturedAllocation, AllocationSymbol> symbolFor) =>
        new(symbolFor(allocation), checked(addressOffset - allocation.Event.DestinationOffset), allowsEnd);

    private static SymbolReference ReferenceFor(
        AllocationSymbol allocation,
        int targetOffset,
        bool allowsEnd,
        IReadOnlyDictionary<CaptureOccurrence, XStringSymbol> strings)
    {
        int addend = checked(targetOffset - allocation.Allocation.DestinationOffset);
        return addend == 0 && strings.TryGetValue(allocation.Occurrence, out XStringSymbol? text)
            ? new XStringReference(text)
            : new AllocationReference(allocation, addend, allowsEnd);
    }

    private static long? TargetLifetime(SymbolReference? target) => target switch
    {
        null => null,
        AllocationReference reference => reference.Symbol.Allocation.TempEpoch,
        AssetProviderReference { Symbol: LocalAssetProviderSymbol local } => local.ProviderCell.Symbol.Allocation.TempEpoch,
        AssetProviderReference => RootTempEpoch,
        XStringReference reference => reference.Symbol.Allocation.Allocation.TempEpoch,
        AliasCellReference reference => reference.Symbol.Allocation.Allocation.TempEpoch,
        BoundaryReference reference => reference.Symbol.Boundary.TempEpoch,
        _ => throw new InvalidDataException("Unknown symbolic pointer target.")
    };

    private void BindMaterialization(CapturedAllocation allocation)
    {
        if (!_unboundInlineBindingsByTarget.TryGetValue(
                allocation.Key,
                out List<PendingInlineBinding>? candidates))
        {
            return;
        }
        if (candidates.Count > 1)
        {
            throw new InvalidDataException(
                $"Materialization {allocation.Key} has more than one pending inline source occurrence.");
        }

        PendingInlineBinding binding = candidates[0];
        allocation.ApplyAlignment(binding.Alignment);
        binding.Target = allocation;
        binding.Pointer!.InlineTarget = allocation;
        binding.Pointer.InsertCell = binding.InsertCell;
        _unboundInlineBindingsByTarget.Remove(allocation.Key);
    }

    private CapturedPointer FindPointer(CaptureOccurrence occurrence) =>
        _pointerByOccurrence.GetValueOrDefault(occurrence)
        ?? throw new InvalidDataException($"Pointer occurrence {occurrence.Value} was not recorded.");

    private PhysicalKey? TryClaimOnlyStagedInsertCell()
    {
        if (_unclaimedInsertCells.Count == 0)
            return null;
        if (_unclaimedInsertCells.Count > 1)
            throw new InvalidDataException("Insert pointer has multiple plausible staged durable LARGE cells.");

        PendingInlineBinding cell = _unclaimedInsertCells.First!.Value;
        _unclaimedInsertCells.RemoveFirst();
        return cell.ClaimInsertCell();
    }

    private PhysicalKey ClaimOnlyStagedInsertCell() =>
        TryClaimOnlyStagedInsertCell()
        ?? throw new InvalidDataException("Insert pointer has no staged durable LARGE cell.");

    private CapturedAllocation ResolveConcreteOwner(PhysicalKey key, string role)
    {
        CapturedAllocation? match = _allocationIndex.FindUniqueInterior(key, requireDecodedOffset: false);
        return match is not null
            ? match
            : throw new InvalidDataException($"{role} {key} has no unique source allocation occurrence.");
    }

    private void ValidateMaterialization(
        int? decodedOffset,
        int length,
        XBlockAddress destination,
        int alignment,
        MaterializationKind kind,
        long tempEpoch)
    {
        if (length < 0 || alignment < 0 || destination.Offset < 0 || tempEpoch <= 0)
            throw new InvalidDataException("Invalid materialization capture coordinates.");
        if (kind is MaterializationKind.StreamCopy or MaterializationKind.CString && decodedOffset is null)
            throw new InvalidDataException($"{kind} materialization requires a decoded source coordinate.");
        if (destination.BlockType == XFileBlockType.TEMP && !_activeTempEpochs.Contains(tempEpoch))
            throw new InvalidDataException("TEMP materialization was recorded outside its active lifetime.");
        if (destination.BlockType != XFileBlockType.TEMP && tempEpoch != RootTempEpoch)
            throw new InvalidDataException("A non-TEMP allocation cannot carry a TEMP lifetime identity.");
        if (alignment > 0 && destination.Offset % alignment != 0)
            throw new InvalidDataException("Materialization destination does not satisfy its recorded alignment.");
        if (decodedOffset is { } offset && (offset < 0 || offset > _tape.Length - length))
            throw new InvalidDataException("Materialization source range lies outside the decoded tape.");
    }

    private void ValidateLayout()
    {
        if (_layout.BlockSizes.Count != XFile.BlockCount)
            throw new InvalidDataException("XFile does not declare the seven PS3 block extents.");
        foreach (CapturedAllocation allocation in _allocations)
        {
            int block = (int)allocation.Event.DestinationBlock;
            if (block < 0 || block >= _layout.BlockSizes.Count ||
                _layout.BlockSizes[block] < 0 ||
                allocation.Event.DestinationOffset > _layout.BlockSizes[block] - allocation.Event.Length)
            {
                throw new InvalidDataException($"Allocation occurrence {allocation.Event.Occurrence.Value} lies outside its declared XFile block extent.");
            }
        }
        foreach (CapturedBoundary boundary in _boundaries)
        {
            int block = (int)boundary.Event.DestinationBlock;
            if (boundary.Event.TempEpoch <= 0 || block < 0 || block >= _layout.BlockSizes.Count ||
                _layout.BlockSizes[block] < 0 || boundary.Event.DestinationOffset < 0 ||
                boundary.Event.DestinationOffset > _layout.BlockSizes[block])
            {
                throw new InvalidDataException(
                    $"Boundary occurrence {boundary.Event.Occurrence.Value} lies outside its declared XFile block extent.");
            }
        }
    }

    private CaptureOccurrence NextOccurrence() => CaptureOccurrence.Create(checked(++_nextOccurrence));
    private long NextSequence => checked(_nextOccurrence + 1);
    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("Zone-object capture is already frozen.");
    }
}
