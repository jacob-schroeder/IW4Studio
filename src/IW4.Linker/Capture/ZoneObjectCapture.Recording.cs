using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Plans;

namespace IW4.Linker;

public sealed partial class ZoneObjectCapture
{
    public CaptureOccurrence RecordMaterialization(
        int? decodedOffset,
        int length,
        XBlockAddress destination,
        int alignment,
        MaterializationKind kind,
        long tempEpoch)
    {
        ThrowIfFrozen();
        ValidateMaterialization(decodedOffset, length, destination, alignment, kind, tempEpoch);
        var allocation = new CapturedAllocation(
            new AllocationEvent(
                NextOccurrence(),
                decodedOffset,
                length,
                destination.BlockType,
                destination.Offset,
                alignment,
                kind,
                tempEpoch),
            PhysicalKey.For(destination, tempEpoch));
        _allocationIndex.Add(allocation);
        _allocations.Add(allocation);
        if (!_allocationByOccurrence.TryAdd(allocation.Event.Occurrence, allocation))
            throw new InvalidDataException("A materialization occurrence was recorded more than once.");
        BindMaterialization(allocation);
        return allocation.Event.Occurrence;
    }

    public CaptureOccurrence RecordPointer(
        int? tapeOffset,
        XBlockAddress? cellAddress,
        int raw,
        XPointerResolutionMode resolutionMode,
        long temporalEpoch,
        long cellTempEpoch)
    {
        ThrowIfFrozen();
        if (temporalEpoch <= 0 || cellTempEpoch <= 0)
            throw new InvalidDataException("Pointer capture has an invalid TEMP epoch.");
        if (cellAddress is null && tapeOffset is null)
            throw new InvalidDataException("A serialized pointer read has neither destination nor tape coordinate.");

        var pointer = new CapturedPointer(
            NextOccurrence(),
            tapeOffset,
            cellAddress is { } cell ? PhysicalKey.For(cell, cellTempEpoch) : null,
            raw,
            resolutionMode,
            temporalEpoch);
        _pointers.Add(pointer);
        if (!_pointerByOccurrence.TryAdd(pointer.Occurrence, pointer))
            throw new InvalidDataException("A pointer occurrence was recorded more than once.");
        return pointer.Occurrence;
    }

    /// <summary>Records a durable LARGE insert cell awaiting its -2 source occurrence.</summary>
    public void RecordInsertPointerCell(XBlockAddress cellAddress, long tempEpoch)
    {
        ThrowIfFrozen();
        PhysicalKey key = PhysicalKey.For(cellAddress, tempEpoch);
        CapturedAllocation? allocation = _allocationIndex.LatestAtStart(key);
        if (_allocations.Count == 0 || !ReferenceEquals(_allocations[^1], allocation) ||
            allocation.Event.Kind != MaterializationKind.InsertCell)
        {
            throw new InvalidDataException("An insert pointer cell must be materialized before it is staged for a source pointer.");
        }

        _unclaimedInsertCells.AddLast(PendingInlineBinding.ForInsertCell(key));
    }

    /// <summary>
    /// Binds an inline/-2 source occurrence to the following materialization.
    /// Repeated observation is accepted only when it names the same source
    /// occurrence and target coordinate.
    /// </summary>
    public void BindInlineTarget(
        CaptureOccurrence pointerOccurrence,
        XBlockAddress targetAddress,
        int alignment,
        long targetTempEpoch)
    {
        ThrowIfFrozen();
        CapturedPointer pointer = FindPointer(pointerOccurrence);
        if (XPointerCodec.GetType(pointer.Raw) is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Pointer occurrence {pointerOccurrence.Value} is not an inline/insert source sentinel.");
        if (alignment < 0 || (alignment > 0 && targetAddress.Offset % alignment != 0))
            throw new InvalidDataException("Inline target does not satisfy its requested alignment.");

        PhysicalKey target = PhysicalKey.For(targetAddress, targetTempEpoch);
        if (_inlineBindingByPointer.TryGetValue(pointer.Occurrence, out PendingInlineBinding? existing))
        {
            if (existing.TargetKey != target || existing.Alignment != alignment)
                throw new InvalidDataException($"Pointer occurrence {pointerOccurrence.Value} was bound to incompatible inline targets.");
            if (XPointerCodec.GetType(pointer.Raw) == PointerType.Insert && existing.InsertCell is null)
                existing.AttachInsertCell(ClaimOnlyStagedInsertCell());
            return;
        }

        PhysicalKey? insertCell = null;
        if (XPointerCodec.GetType(pointer.Raw) == PointerType.Insert)
        {
            insertCell = TryClaimOnlyStagedInsertCell();
        }

        var binding = new PendingInlineBinding(pointer, target, alignment, insertCell);
        _inlineBindingByPointer.Add(pointer.Occurrence, binding);
        if (!_unboundInlineBindingsByTarget.TryGetValue(target, out List<PendingInlineBinding>? bindings))
        {
            bindings = [];
            _unboundInlineBindingsByTarget.Add(target, bindings);
        }
        bindings.Add(binding);
    }

    /// <summary>
    /// Associates the source pointer with a range validated by the loader.
    /// This captures legitimate zero-byte and one-past target views without
    /// loosening ordinary pointer resolution.
    /// </summary>
    public void BindValidatedTarget(
        CaptureOccurrence pointerOccurrence,
        XBlockAddress address,
        int byteCount,
        long targetTempEpoch)
    {
        ThrowIfFrozen();
        if (byteCount < 0 || targetTempEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        if (address.BlockType == XFileBlockType.TEMP && !_activeTempEpochs.Contains(targetTempEpoch))
            throw new InvalidDataException("TEMP target validation was recorded outside its active lifetime.");
        if (address.BlockType != XFileBlockType.TEMP && targetTempEpoch != RootTempEpoch)
            throw new InvalidDataException("A non-TEMP target validation cannot carry a TEMP lifetime identity.");
        CapturedPointer pointer = FindPointer(pointerOccurrence);
        if (pointer.ValidatedTarget is { } existing && (existing.Address != address || existing.Length != byteCount))
            throw new InvalidDataException($"Pointer occurrence {pointerOccurrence.Value} was validated against incompatible target ranges.");
        if (pointer.ValidatedTarget is not null)
            return;

        CaptureOccurrence? boundaryOccurrence = byteCount == 0 ? NextOccurrence() : null;
        pointer.ValidatedTarget = new ValidatedTarget(
            address,
            byteCount,
            targetTempEpoch,
            boundaryOccurrence,
            null,
            null);
    }

    public void MarkXString(CaptureOccurrence allocationOccurrence)
    {
        ThrowIfFrozen();
        CapturedAllocation allocation = _allocationByOccurrence.GetValueOrDefault(allocationOccurrence)
            ?? throw new InvalidDataException(
                $"XString materialization occurrence {allocationOccurrence.Value} was not recorded.");
        if (allocation.Event.Kind != MaterializationKind.CString)
        {
            throw new InvalidDataException(
                $"XString occurrence {allocationOccurrence.Value} does not identify a CString materialization.");
        }

        _strings.Add(new CapturedXString(NextOccurrence(), allocation));
    }

    /// <summary>
    /// Records runtime-only provider ids solely for load-time correlation. The
    /// frozen graph contains opaque local/import provider symbols instead.
    /// </summary>
    public CaptureOccurrence RecordProviderRegistration(
        CaptureOccurrence sourcePointer,
        XBlockAddress expectedMaterialization,
        long incomingProviderIdentity,
        long activeProviderIdentity,
        XBlockAddress? insertProviderCell)
    {
        ThrowIfFrozen();
        if (incomingProviderIdentity <= 0 || activeProviderIdentity <= 0)
            throw new InvalidDataException("Provider registration has an invalid correlation identity.");
        CapturedPointer pointer = FindPointer(sourcePointer);
        CapturedAllocation materialized = pointer.InlineTarget
            ?? throw new InvalidDataException("Provider registration source was not bound to its provider body.");
        if (materialized.Event.DestinationBlock != expectedMaterialization.BlockType ||
            materialized.Event.DestinationOffset != expectedMaterialization.Offset)
        {
            throw new InvalidDataException(
                "Provider registration incoming staging address does not match its source pointer's bound materialization.");
        }
        if (pointer.Cell is null)
            throw new InvalidDataException("Provider registration source has no concrete serialized pointer cell.");
        if (pointer.ProviderRegistration is not null)
            throw new InvalidDataException("A serialized provider source pointer was registered more than once.");

        PointerType sourceForm = XPointerCodec.GetType(pointer.Raw);
        if (sourceForm == PointerType.Insert && insertProviderCell is null)
            throw new InvalidDataException("An insert provider registration has no durable LARGE cell.");
        if (sourceForm != PointerType.Insert && insertProviderCell is not null)
            throw new InvalidDataException("A non-insert provider registration cannot claim a durable insert cell.");

        PhysicalKey providerCell = insertProviderCell is { } insertCell
            ? PhysicalKey.For(insertCell, RootTempEpoch)
            : pointer.Cell;
        if (sourceForm == PointerType.Insert && pointer.InsertCell != providerCell)
            throw new InvalidDataException("Provider registration does not match the insert cell bound to its source pointer.");
        pointer.ProviderRegistration = new CapturedProvider(
            NextOccurrence(),
            incomingProviderIdentity,
            activeProviderIdentity,
            pointer,
            providerCell,
            materialized);
        _providers.Add(pointer.ProviderRegistration);
        return pointer.ProviderRegistration.Occurrence;
    }

    public int? FindTapeOffset(XBlockAddress cellAddress, long tempEpoch)
    {
        PhysicalKey key = PhysicalKey.For(cellAddress, tempEpoch);
        CapturedAllocation? owner = _allocationIndex.FindUniqueInterior(key, requireDecodedOffset: true);
        return owner is not null
            ? checked(owner.Event.DecodedOffset!.Value + (cellAddress.Offset - owner.Event.DestinationOffset))
            : null;
    }
}
