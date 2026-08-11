using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Plans;

namespace IW4.Linker;

public sealed partial class ZoneObjectCapture
{
    private sealed record PhysicalKey(XFileBlockType Block, int Offset, long TempEpoch)
    {
        public XBlockAddress Address => new(Block, Offset);
        public static PhysicalKey For(XBlockAddress address, long epoch) =>
            new(address.BlockType, address.Offset, address.BlockType == XFileBlockType.TEMP ? epoch : RootTempEpoch);
        public override string ToString() => $"{Block}:0x{Offset:X}@{TempEpoch}";
    }

    private sealed class CapturedAllocation
    {
        public CapturedAllocation(AllocationEvent @event, PhysicalKey key) { Event = @event; Key = key; }
        public AllocationEvent Event { get; private set; }
        public PhysicalKey Key { get; }
        public bool ContainsInterior(PhysicalKey key) =>
            key.Block == Key.Block && key.TempEpoch == Key.TempEpoch &&
            Event.Length > 0 && key.Offset >= Key.Offset && key.Offset < Key.Offset + Event.Length;
        public bool ContainsRange(XBlockAddress address, int length, bool allowEnd)
        {
            if (address.BlockType != Key.Block || length < 0)
                return false;
            int start = Key.Offset;
            int end = checked(start + Event.Length);
            if (length == 0)
            {
                if (Event.Length == 0)
                    return address.Offset == start;
                return address.Offset >= start && (allowEnd ? address.Offset <= end : address.Offset < end);
            }
            return Event.Length > 0 && address.Offset >= start && checked(address.Offset + length) <= end;
        }
        public void ApplyAlignment(int alignment)
        {
            if (alignment <= Event.Alignment)
                return;
            Event = Event with { Alignment = alignment };
        }
    }

    private sealed class CapturedPointer
    {
        public CapturedPointer(CaptureOccurrence occurrence, int? tapeOffset, PhysicalKey? cell, int raw, XPointerResolutionMode mode, long temporalEpoch)
        {
            Occurrence = occurrence; TapeOffset = tapeOffset; Cell = cell; Raw = raw; ResolutionMode = mode; TemporalEpoch = temporalEpoch;
        }
        public CaptureOccurrence Occurrence { get; }
        public int? TapeOffset { get; }
        public PhysicalKey? Cell { get; }
        public int Raw { get; }
        public XPointerResolutionMode ResolutionMode { get; }
        public long TemporalEpoch { get; }
        public CapturedAllocation? InlineTarget { get; set; }
        public PhysicalKey? InsertCell { get; set; }
        public ValidatedTarget? ValidatedTarget { get; set; }
        public CapturedProvider? ProviderRegistration { get; set; }
    }

    private sealed class PendingInlineBinding
    {
        private PendingInlineBinding(PhysicalKey insertCell) { InsertCell = insertCell; IsUnclaimedInsertCell = true; }
        public PendingInlineBinding(CapturedPointer pointer, PhysicalKey target, int alignment, PhysicalKey? insertCell)
        { Pointer = pointer; TargetKey = target; Alignment = alignment; InsertCell = insertCell; }
        public static PendingInlineBinding ForInsertCell(PhysicalKey cell) => new(cell);
        public CapturedPointer? Pointer { get; }
        public PhysicalKey? TargetKey { get; }
        public int Alignment { get; }
        public PhysicalKey? InsertCell { get; private set; }
        public CapturedAllocation? Target { get; set; }
        public bool IsUnclaimedInsertCell { get; private set; }
        public PhysicalKey ClaimInsertCell()
        {
            if (!IsUnclaimedInsertCell) throw new InvalidDataException("Insert cell was claimed twice.");
            IsUnclaimedInsertCell = false;
            return InsertCell ?? throw new InvalidDataException("Insert cell staging record has no cell.");
        }

        public void AttachInsertCell(PhysicalKey cell)
        {
            if (InsertCell is not null)
                throw new InvalidDataException("Insert pointer already has a durable provider cell.");
            InsertCell = cell;
        }
    }

    private sealed record CapturedXString(CaptureOccurrence Occurrence, CapturedAllocation Allocation);
    private sealed record ValidatedTarget(
        XBlockAddress Address,
        int Length,
        long TargetTempEpoch,
        CaptureOccurrence? BoundaryOccurrence,
        CapturedAllocation? Owner,
        CapturedBoundary? Boundary);
    private sealed record CapturedBoundary(BoundaryEvent Event);
    private sealed class TempLifetimeRecord(long epoch, long begin, long? parentEpoch)
    {
        public long Epoch { get; } = epoch;
        public long BeginSequence { get; } = begin;
        public long? ParentEpoch { get; } = parentEpoch;
        public long EndSequence { get; set; } = long.MaxValue;
    }
    private sealed record CapturedProvider(
        CaptureOccurrence Occurrence,
        long IncomingRuntimeId,
        long ActiveRuntimeId,
        CapturedPointer SourcePointer,
        PhysicalKey ProviderCell,
        CapturedAllocation Materialization);
}
