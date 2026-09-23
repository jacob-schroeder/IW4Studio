using IW4.Game.Assets;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.IO;
using IW4.Loaders.Pointers;

namespace IW4.Loaders.Database;

/// <summary>
/// Associates transient stream addresses with loader-owned observations.
/// </summary>
internal sealed class ZoneObjectCaptureBridge
{
    private readonly ZoneObjectObservationBuilder _capture;
    private readonly Stack<long> _activeTempEpochs = new([1]);
    private readonly Dictionary<PhysicalCell, long> _pointerOccurrences = [];
    private readonly HashSet<BaseAsset> _providers =
        new(ReferenceEqualityComparer.Instance);
    private bool _frozen;
    private long _tempEpoch = 1;

    public ZoneObjectCaptureBridge(ReadOnlySpan<byte> decodedTape, XFile layout) => _capture = new(decodedTape, layout);

    public long CurrentTempEpoch => _tempEpoch;

    public void Push(XFileBlockType block)
    {
        if (block != XFileBlockType.TEMP)
            return;

        _tempEpoch = _capture.EnterTempEpoch();
        _activeTempEpochs.Push(_tempEpoch);
    }

    public void Pop(XFileBlockType block, long restoredEpoch)
    {
        if (block != XFileBlockType.TEMP)
            return;
        if (_activeTempEpochs.Count <= 1 || _activeTempEpochs.Peek() != _tempEpoch)
            throw new InvalidDataException("TEMP capture bridge lifetime stack is unbalanced.");

        _capture.RetireTempEpoch(_tempEpoch);
        _activeTempEpochs.Pop();
        if (_activeTempEpochs.Peek() != restoredEpoch)
            throw new InvalidDataException("TEMP capture bridge restored the wrong parent lifetime.");
        _tempEpoch = restoredEpoch;
    }

    public long RecordLoad(
        FastFileCursor cursor,
        int sourceOffset,
        int length,
        XBlockAddress destination,
        int alignment,
        ZoneMaterializationKind kind) =>
        _capture.RecordMaterialization(
            cursor.DecodedTapeOffsetAt(sourceOffset),
            length,
            destination,
            alignment,
            kind,
            EpochForDestination(destination));

    public long RecordDestination(
        int length,
        XBlockAddress destination,
        int alignment,
        ZoneMaterializationKind kind) =>
        _capture.RecordMaterialization(null, length, destination, alignment, kind, EpochForDestination(destination));

    public void RecordInsertPointerCell(XBlockAddress cell) =>
        _capture.RecordInsertPointerCell(cell, EpochForDestination(cell));

    public long RecordPointer(
        FastFileCursor cursor,
        int cellOffset,
        XBlockAddress? cellAddress,
        int raw,
        XPointerResolutionMode mode)
    {
        long cellEpoch = cellAddress is { } cell ? EpochForNewCell(cell) : _tempEpoch;
        long occurrence = _capture.RecordPointer(
            cursor.DecodedTapeOffsetAt(cellOffset),
            cellAddress,
            raw,
            mode,
            _tempEpoch,
            cellEpoch);

        if (cellAddress is { } recordedCell)
        {
            var key = new PhysicalCell(recordedCell, cellEpoch);
            if (!_pointerOccurrences.TryAdd(key, occurrence))
                throw new InvalidDataException($"Serialized pointer cell {recordedCell} was captured more than once in one TEMP lifetime.");
        }
        return occurrence;
    }

    public void BindInlineTarget(
        XBlockAddress? cell,
        XBlockAddress target,
        int alignment = 0,
        XPointerReadHandle? sourceHandle = null)
    {
        long occurrence = ResolvePointerOccurrence(cell, sourceHandle);
        _capture.BindInlineTarget(occurrence, target, alignment, EpochForDestination(target));
    }

    /// <summary>
    /// Observes the first serialized -1/-2 cell rewrite. Manual loaders use
    /// this path as well as pointer-reader helpers, so binding remains a
    /// property of the stream boundary rather than individual asset readers.
    /// </summary>
    public void ObservePointerCellWrite(XBlockAddress cell, int value, int pendingAlignment)
    {
        if (XPointerCodec.GetType(value) != PointerType.Offset)
            return;

        if (!XPointerCodec.TryDecodeBlockAddress(value, out XBlockAddress target))
            return;

        if (cell.BlockType != XFileBlockType.TEMP)
        {
            if (_pointerOccurrences.TryGetValue(
                    new PhysicalCell(cell, 1),
                    out long occurrence))
            {
                TryBindObservedPointer(occurrence, target, pendingAlignment);
            }
            return;
        }

        foreach (long epoch in _activeTempEpochs)
        {
            if (_pointerOccurrences.TryGetValue(
                    new PhysicalCell(cell, epoch),
                    out long occurrence) &&
                TryBindObservedPointer(occurrence, target, pendingAlignment))
            {
                return;
            }
        }
    }

    public void BindValidatedTarget(
        XPointerReference pointer,
        XBlockAddress address,
        int byteCount,
        XPointerReadHandle? sourceHandle = null)
    {
        _capture.BindValidatedTarget(
            ResolvePointerOccurrence(pointer.CellAddress, sourceHandle),
            address,
            byteCount,
            EpochForDestination(address));
    }

    public void MarkXString(CStringMaterializationHandle handle) =>
        _capture.MarkXString(handle.Occurrence);

    public ProviderRegistrationOccurrence CreateProviderRegistrationOccurrence(
        XBlockAddress sourceCell,
        XBlockAddress? insertProviderCell) =>
        new(sourceCell, EpochForNewCell(sourceCell), insertProviderCell);

    public void RecordProvider(
        ProviderRegistrationOccurrence providerRegistration,
        XBlockAddress materialization,
        long incomingProviderIdentity,
        long activeProviderIdentity,
        BaseAsset provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _capture.RecordProviderRegistration(
            FindPointerOccurrence(
                providerRegistration.SourcePointerCell,
                providerRegistration.SourceEpoch),
            materialization,
            incomingProviderIdentity,
            activeProviderIdentity,
            providerRegistration.InsertProviderCell,
            provider);
        if (!_providers.Add(provider))
        {
            throw new InvalidDataException(
                "One provider object was registered by more than one captured source occurrence.");
        }
    }

    public ZoneObjectObservation Freeze()
    {
        if (_frozen)
            throw new InvalidOperationException("Zone-object capture was frozen more than once.");
        _frozen = true;
        return _capture.Freeze();
    }

    private long FindPointerOccurrence(XBlockAddress address)
    {
        if (address.BlockType != XFileBlockType.TEMP)
        {
            return _pointerOccurrences.TryGetValue(
                    new PhysicalCell(address, 1),
                    out long occurrence)
                ? occurrence
                : throw new InvalidDataException(
                    $"Pointer cell {address} has no unique captured source occurrence in its active lifetime.");
        }

        long match = default;
        bool found = false;
        foreach (long epoch in _activeTempEpochs)
        {
            if (!_pointerOccurrences.TryGetValue(
                    new PhysicalCell(address, epoch),
                    out long occurrence))
            {
                continue;
            }
            if (found)
            {
                throw new InvalidDataException(
                    $"Pointer cell {address} has no unique captured source occurrence in its active lifetime.");
            }
            match = occurrence;
            found = true;
        }

        return found
            ? match
            : throw new InvalidDataException(
                $"Pointer cell {address} has no unique captured source occurrence in its active lifetime.");
    }

    private long FindPointerOccurrence(XBlockAddress address, long epoch)
    {
        var key = new PhysicalCell(address, epoch);
        return _pointerOccurrences.TryGetValue(key, out long occurrence)
            ? occurrence
            : throw new InvalidDataException(
                $"Pointer cell {address} has no captured source occurrence in TEMP epoch {epoch}.");
    }

    private bool TryBindObservedPointer(
        long occurrence,
        XBlockAddress target,
        int pendingAlignment)
    {
        try
        {
            _capture.BindInlineTarget(
                occurrence,
                target,
                pendingAlignment,
                EpochForDestination(target));
            return true;
        }
        catch (InvalidDataException)
        {
            // A cell can subsequently be rewritten to canonical runtime
            // data. Only the original inline/insert occurrence is bound.
            return false;
        }
    }

    private long ResolvePointerOccurrence(
        XBlockAddress? cell,
        XPointerReadHandle? sourceHandle)
    {
        if (sourceHandle is { } handle)
        {
            if (cell is { } sourceAddress && FindPointerOccurrence(sourceAddress) != handle.Occurrence)
                throw new InvalidDataException("Pointer read handle does not match its serialized source cell.");
            return handle.Occurrence;
        }

        return cell is { } address
            ? FindPointerOccurrence(address)
            : throw new InvalidDataException(
                "A tape-only pointer source requires its exact pointer-read handle for target binding.");
    }

    private long EpochForNewCell(XBlockAddress address) =>
        address.BlockType == XFileBlockType.TEMP ? _tempEpoch : 1;

    private long EpochForDestination(XBlockAddress address) =>
        address.BlockType == XFileBlockType.TEMP ? _tempEpoch : 1;

    private readonly record struct PhysicalCell(XBlockAddress Address, long Epoch);
}
