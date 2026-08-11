using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker;
using IW4.Linker.Plans;
using IW4.Runtime.IO;
using IW4.FastFiles.Loaders.Pointers;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Loader-only translation between transient stream addresses and the Linker
/// capture protocol. Runtime provider ids do not escape this bridge.
/// </summary>
internal sealed class ZoneObjectCaptureBridge
{
    private readonly ZoneObjectCapture _capture;
    private readonly Stack<long> _activeTempEpochs = new([1]);
    private readonly Dictionary<PhysicalCell, CaptureOccurrence> _pointerOccurrences = [];
    private readonly Dictionary<BaseAsset, CaptureOccurrence> _providerOccurrences =
        new(ReferenceEqualityComparer.Instance);
    private ZoneObjectLinkImportResolver? _importResolver;
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

    public CaptureOccurrence RecordLoad(
        FastFileCursor cursor,
        int sourceOffset,
        int length,
        XBlockAddress destination,
        int alignment,
        MaterializationKind kind) =>
        _capture.RecordMaterialization(
            cursor.DecodedTapeOffsetAt(sourceOffset),
            length,
            destination,
            alignment,
            kind,
            EpochForDestination(destination));

    public CaptureOccurrence RecordDestination(
        int length,
        XBlockAddress destination,
        int alignment,
        MaterializationKind kind) =>
        _capture.RecordMaterialization(null, length, destination, alignment, kind, EpochForDestination(destination));

    public void RecordInsertPointerCell(XBlockAddress cell) =>
        _capture.RecordInsertPointerCell(cell, EpochForDestination(cell));

    public CaptureOccurrence RecordPointer(
        FastFileCursor cursor,
        int cellOffset,
        XBlockAddress? cellAddress,
        int raw,
        XPointerResolutionMode mode)
    {
        long cellEpoch = cellAddress is { } cell ? EpochForNewCell(cell) : _tempEpoch;
        CaptureOccurrence occurrence = _capture.RecordPointer(
            cursor.DecodedTapeOffsetAt(cellOffset) ??
            (cellAddress is { } address ? _capture.FindTapeOffset(address, cellEpoch) : null),
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
        CaptureOccurrence occurrence = ResolvePointerOccurrence(cell, sourceHandle);
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

        CaptureOccurrence[] candidates = FindPointerOccurrences(cell);
        if (candidates.Length == 0)
            return;

        if (!XPointerCodec.TryDecodeBlockAddress(value, out XBlockAddress target))
            return;
        foreach (CaptureOccurrence occurrence in candidates)
        {
            try
            {
                _capture.BindInlineTarget(occurrence, target, pendingAlignment, EpochForDestination(target));
                return;
            }
            catch (InvalidDataException)
            {
                // A cell can subsequently be rewritten to canonical runtime
                // data. Only the original inline/insert occurrence is bound.
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
        CaptureOccurrence occurrence = _capture.RecordProviderRegistration(
            FindPointerOccurrence(
                providerRegistration.SourcePointerCell,
                providerRegistration.SourceEpoch),
            materialization,
            incomingProviderIdentity,
            activeProviderIdentity,
            providerRegistration.InsertProviderCell);
        if (!_providerOccurrences.TryAdd(provider, occurrence))
        {
            throw new InvalidDataException(
                "One provider object was registered by more than one captured source occurrence.");
        }
    }

    public ZoneObjectFile Freeze()
    {
        if (_importResolver is not null)
            throw new InvalidOperationException("Zone-object capture was frozen more than once.");

        ZoneObjectFile objectFile = _capture.Freeze();
        _importResolver = new ZoneObjectLinkImportResolver(
            objectFile,
            _providerOccurrences);
        return objectFile;
    }

    public ZoneObjectLinkImportResolver ImportResolver =>
        _importResolver ?? throw new InvalidOperationException(
            "Zone-object capture has not been frozen.");

    private CaptureOccurrence FindPointerOccurrence(XBlockAddress address)
    {
        CaptureOccurrence[] matches = FindPointerOccurrences(address);
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException($"Pointer cell {address} has no unique captured source occurrence in its active lifetime.");
    }

    private CaptureOccurrence FindPointerOccurrence(XBlockAddress address, long epoch)
    {
        var key = new PhysicalCell(address, epoch);
        return _pointerOccurrences.TryGetValue(key, out CaptureOccurrence occurrence)
            ? occurrence
            : throw new InvalidDataException(
                $"Pointer cell {address} has no captured source occurrence in TEMP epoch {epoch}.");
    }

    private CaptureOccurrence[] FindPointerOccurrences(XBlockAddress address)
    {
        IEnumerable<long> epochs = address.BlockType == XFileBlockType.TEMP
            ? _activeTempEpochs
            : [1L];
        return epochs
            .Select(epoch => new PhysicalCell(address, epoch))
            .Where(key => _pointerOccurrences.TryGetValue(key, out _))
            .Select(key => _pointerOccurrences[key])
            .ToArray();
    }

    private CaptureOccurrence ResolvePointerOccurrence(
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
