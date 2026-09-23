using IW4.Game.Assets;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Loaders.Database;

/// <summary>Retained decoded input and ordered physical observations from one zone load.</summary>
public sealed class ZoneObjectObservation
{
    internal ZoneObjectObservation(byte[] decodedTape, XFile layout, IReadOnlyList<ZoneObjectObservationEvent> events)
    {
        DecodedTape = decodedTape;
        Layout = new XFile(layout.Size, layout.ExternalSize, layout.BlockSizes);
        Events = Array.AsReadOnly(events.ToArray());
    }

    public ReadOnlyMemory<byte> DecodedTape { get; }
    public XFile Layout { get; }
    public IReadOnlyList<ZoneObjectObservationEvent> Events { get; }
}

public enum ZoneMaterializationKind
{
    StreamCopy,
    CString,
    RuntimeZeroFill,
    VirtualReservation,
    InsertCell
}

public abstract record ZoneObjectObservationEvent;
public sealed record TempEpochEntered(long Epoch) : ZoneObjectObservationEvent;
public sealed record TempEpochRetired(long Epoch) : ZoneObjectObservationEvent;
public sealed record ZoneMaterialized(long Id, int? DecodedOffset, int Length, XBlockAddress Destination,
    int Alignment, ZoneMaterializationKind Kind, long TempEpoch) : ZoneObjectObservationEvent;
public sealed record ZonePointerRead(long Id, int? DecodedOffset, XBlockAddress? Cell, int Raw,
    XPointerResolutionMode ResolutionMode, long TemporalEpoch, long CellTempEpoch) : ZoneObjectObservationEvent;
public sealed record ZoneInsertCellStaged(XBlockAddress Cell, long TempEpoch) : ZoneObjectObservationEvent;
public sealed record ZoneInlineTargetBound(long PointerId, XBlockAddress Target, int Alignment,
    long TargetTempEpoch) : ZoneObjectObservationEvent;
public sealed record ZoneValidatedTargetBound(long PointerId, XBlockAddress Target, int Length,
    long TargetTempEpoch) : ZoneObjectObservationEvent;
public sealed record ZoneXStringMarked(long MaterializationId) : ZoneObjectObservationEvent;
public sealed record ZoneProviderRegistered(long PointerId, XBlockAddress Materialization,
    long IncomingIdentity, long ActiveIdentity, XBlockAddress? InsertCell,
    BaseAsset Provider) : ZoneObjectObservationEvent;
