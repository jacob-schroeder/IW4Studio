using IW4.FastFiles.Zone;

namespace IW4.Linker.Model;

public enum MaterializationKind
{
    StreamCopy,
    RuntimeZeroFill,
    VirtualReservation,
    InsertCell,
    CString
}

/// <summary>
/// Ordered materialization ledger. Destination coordinates are source-layout
/// hints only; references in the object graph use symbols.
/// </summary>
public sealed record AllocationEvent(
    CaptureOccurrence Occurrence,
    int? DecodedOffset,
    int Length,
    XFileBlockType DestinationBlock,
    int DestinationOffset,
    int Alignment,
    MaterializationKind Kind,
    long TempEpoch);

/// <summary>
/// A validated zero-byte target with no unambiguous backing allocation.
/// Destination coordinates are placement data; occurrence is its identity.
/// </summary>
public sealed record BoundaryEvent(
    CaptureOccurrence Occurrence,
    XFileBlockType DestinationBlock,
    int DestinationOffset,
    long TempEpoch);

/// <summary>One TEMP lifetime interval in capture event order.</summary>
public sealed record TempLifetime(long Epoch, long BeginSequence, long EndSequence);
