using IW4.Game.Assets;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Loaders.Database;

/// <summary>Records loader facts without interpreting them as link symbols.</summary>
internal sealed class ZoneObjectObservationBuilder
{
    private readonly byte[] _tape;
    private readonly XFile _layout;
    private readonly List<ZoneObjectObservationEvent> _events = [];
    private readonly Dictionary<long, ZonePointerRead> _pointers = [];
    private readonly Dictionary<long, ZoneInlineTargetBound> _inlineTargets = [];
    private readonly Stack<long> _tempEpochs = new([1]);
    private long _nextId;
    private long _nextTempEpoch = 1;
    private bool _frozen;

    public ZoneObjectObservationBuilder(ReadOnlySpan<byte> tape, XFile layout)
    {
        _tape = tape.ToArray();
        _layout = new XFile(layout.Size, layout.ExternalSize, layout.BlockSizes);
    }

    public long EnterTempEpoch()
    {
        ThrowIfFrozen();
        long epoch = checked(++_nextTempEpoch);
        _tempEpochs.Push(epoch);
        _events.Add(new TempEpochEntered(epoch));
        return epoch;
    }

    public void RetireTempEpoch(long epoch)
    {
        ThrowIfFrozen();
        if (epoch == 1 || _tempEpochs.Count <= 1 || _tempEpochs.Peek() != epoch)
            throw new InvalidDataException("TEMP observation lifetime stack is unbalanced.");
        _tempEpochs.Pop();
        _events.Add(new TempEpochRetired(epoch));
    }

    public long RecordMaterialization(int? decodedOffset, int length, XBlockAddress destination,
        int alignment, ZoneMaterializationKind kind, long tempEpoch)
    {
        ThrowIfFrozen();
        long id = checked(++_nextId);
        _events.Add(new ZoneMaterialized(id, decodedOffset, length, destination, alignment, kind, tempEpoch));
        return id;
    }

    public long RecordPointer(int? decodedOffset, XBlockAddress? cell, int raw,
        XPointerResolutionMode mode, long temporalEpoch, long cellTempEpoch)
    {
        ThrowIfFrozen();
        long id = checked(++_nextId);
        var read = new ZonePointerRead(id, decodedOffset, cell, raw, mode, temporalEpoch, cellTempEpoch);
        _events.Add(read);
        _pointers.Add(id, read);
        return id;
    }

    public void RecordInsertPointerCell(XBlockAddress cell, long tempEpoch)
    {
        ThrowIfFrozen();
        _events.Add(new ZoneInsertCellStaged(cell, tempEpoch));
    }

    public void BindInlineTarget(long pointerId, XBlockAddress target, int alignment, long targetTempEpoch)
    {
        ThrowIfFrozen();
        if (!_pointers.TryGetValue(pointerId, out ZonePointerRead? pointer) ||
            XPointerCodec.GetType(pointer.Raw) is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Pointer observation {pointerId} is not an inline/insert source sentinel.");
        if (alignment < 0 || (alignment > 0 && target.Offset % alignment != 0))
            throw new InvalidDataException("Inline target does not satisfy its requested alignment.");
        var binding = new ZoneInlineTargetBound(pointerId, target, alignment, targetTempEpoch);
        if (_inlineTargets.TryGetValue(pointerId, out ZoneInlineTargetBound? existing))
        {
            if (existing != binding)
                throw new InvalidDataException($"Pointer observation {pointerId} was bound to incompatible inline targets.");
        }
        else
            _inlineTargets.Add(pointerId, binding);
        _events.Add(binding);
    }

    public void BindValidatedTarget(long pointerId, XBlockAddress target, int length, long targetTempEpoch)
    {
        ThrowIfFrozen();
        _events.Add(new ZoneValidatedTargetBound(pointerId, target, length, targetTempEpoch));
    }

    public void MarkXString(long materializationId)
    {
        ThrowIfFrozen();
        _events.Add(new ZoneXStringMarked(materializationId));
    }

    public void RecordProviderRegistration(long pointerId, XBlockAddress materialization,
        long incomingIdentity, long activeIdentity, XBlockAddress? insertCell, BaseAsset provider)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(provider);
        _events.Add(new ZoneProviderRegistered(pointerId, materialization, incomingIdentity,
            activeIdentity, insertCell, provider));
    }

    public ZoneObjectObservation Freeze()
    {
        ThrowIfFrozen();
        _frozen = true;
        var observation = new ZoneObjectObservation(_tape, _layout, _events);
        _events.Clear();
        _pointers.Clear();
        _inlineTargets.Clear();
        return observation;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("Zone-object observations are already frozen.");
    }
}
