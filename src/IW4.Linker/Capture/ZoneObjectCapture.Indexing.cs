using IW4.FastFiles.Zone;

namespace IW4.Linker;

public sealed partial class ZoneObjectCapture
{
    /// <summary>
    /// Mutable capture-time address index. Physical coordinates never escape
    /// this builder; frozen references are created from occurrence symbols.
    /// </summary>
    private sealed class AllocationIndex
    {
        private readonly Dictionary<AllocationRangeKey, AllocationRangeGroup> _ranges = [];
        private readonly Dictionary<PhysicalKey, List<CapturedAllocation>> _allocationsByStart = [];

        public void Add(CapturedAllocation allocation)
        {
            if (allocation.Event.Length > 0)
            {
                var rangeKey = new AllocationRangeKey(allocation.Key.Block, allocation.Key.TempEpoch);
                if (!_ranges.TryGetValue(rangeKey, out AllocationRangeGroup? ranges))
                {
                    ranges = new AllocationRangeGroup();
                    _ranges.Add(rangeKey, ranges);
                }
                ranges.Add(allocation);
            }

            if (!_allocationsByStart.TryGetValue(allocation.Key, out List<CapturedAllocation>? starts))
            {
                starts = [];
                _allocationsByStart.Add(allocation.Key, starts);
            }
            starts.Add(allocation);
        }

        public CapturedAllocation? LatestAtStart(PhysicalKey key) =>
            _allocationsByStart.TryGetValue(key, out List<CapturedAllocation>? allocations)
                ? allocations[^1]
                : null;

        public CapturedAllocation? FindUniqueInterior(PhysicalKey key, bool requireDecodedOffset)
        {
            var rangeKey = new AllocationRangeKey(key.Block, key.TempEpoch);
            if (!_ranges.TryGetValue(rangeKey, out AllocationRangeGroup? ranges))
                return null;

            var match = new UniqueAllocationMatch();
            ranges.AccumulateInteriorMatch(key, requireDecodedOffset, ref match);
            return match.Result;
        }

        public CapturedAllocation? FindUniqueLiveRange(
            XBlockAddress address,
            int length,
            bool allowEnd,
            long sequence,
            long activeTempEpoch,
            IReadOnlyDictionary<long, TempLifetimeRecord> lifetimes)
        {
            var match = new UniqueAllocationMatch();
            if (address.BlockType != XFileBlockType.TEMP)
            {
                AccumulateLifetimeRange(
                    address,
                    length,
                    allowEnd,
                    sequence,
                    RootTempEpoch,
                    lifetimes,
                    ref match);
                return match.Result;
            }

            long? epoch = activeTempEpoch;
            int remaining = lifetimes.Count;
            while (epoch is { } value)
            {
                if (remaining-- == 0)
                    throw new InvalidDataException("TEMP lifetime ancestry contains a cycle.");

                TempLifetimeRecord lifetime = GetLifetime(lifetimes, value);
                AccumulateLifetimeRange(
                    address,
                    length,
                    allowEnd,
                    sequence,
                    value,
                    lifetimes,
                    ref match);
                epoch = lifetime.ParentEpoch;
            }
            return match.Result;
        }

        private void AccumulateLifetimeRange(
            XBlockAddress address,
            int length,
            bool allowEnd,
            long sequence,
            long epoch,
            IReadOnlyDictionary<long, TempLifetimeRecord> lifetimes,
            ref UniqueAllocationMatch match)
        {
            TempLifetimeRecord lifetime = GetLifetime(lifetimes, epoch);
            if (lifetime.BeginSequence > sequence || sequence >= lifetime.EndSequence)
            {
                throw new InvalidDataException(
                    $"Pointer event {sequence} lies outside TEMP lifetime {epoch}.");
            }

            var rangeKey = new AllocationRangeKey(address.BlockType, epoch);
            if (length == 0)
            {
                var startKey = new PhysicalKey(address.BlockType, address.Offset, epoch);
                if (_allocationsByStart.TryGetValue(startKey, out List<CapturedAllocation>? starts))
                {
                    if (starts.Count == 1)
                        match.Add(starts[0]);
                    else if (starts.Count > 1)
                        match.MarkAmbiguous();
                }

                if (_ranges.TryGetValue(rangeKey, out AllocationRangeGroup? zeroRanges))
                {
                    zeroRanges.AccumulatePrecedingRangeMatch(
                        address,
                        allowEnd,
                        ref match);
                }
                return;
            }

            if (_ranges.TryGetValue(rangeKey, out AllocationRangeGroup? ranges))
                ranges.AccumulateRangeMatch(address, length, allowEnd, ref match);
        }

        private static TempLifetimeRecord GetLifetime(
            IReadOnlyDictionary<long, TempLifetimeRecord> lifetimes,
            long epoch) =>
            lifetimes.TryGetValue(epoch, out TempLifetimeRecord? lifetime)
                ? lifetime
                : throw new InvalidDataException($"TEMP lifetime {epoch} was not recorded.");
    }

    private readonly record struct AllocationRangeKey(XFileBlockType Block, long TempEpoch);

    private struct UniqueAllocationMatch
    {
        private CapturedAllocation? _allocation;
        private bool _ambiguous;

        public readonly CapturedAllocation? Result => _ambiguous ? null : _allocation;

        public void Add(CapturedAllocation allocation)
        {
            if (_allocation is null)
                _allocation = allocation;
            else if (!ReferenceEquals(_allocation, allocation))
                _ambiguous = true;
        }

        public void MarkAmbiguous() => _ambiguous = true;
    }

    /// <summary>
    /// Positive ranges for one physical block lifetime. Stream positions are
    /// monotonic within a lifetime; TEMP rewind always begins a new lifetime.
    /// </summary>
    private sealed class AllocationRangeGroup
    {
        private readonly List<CapturedAllocation> _allocations = [];

        public void Add(CapturedAllocation allocation)
        {
            if (allocation.Event.Length <= 0)
                throw new InvalidDataException("Only positive materialization ranges can enter the range index.");
            if (_allocations.Count != 0)
            {
                CapturedAllocation previous = _allocations[^1];
                long previousEnd = (long)previous.Event.DestinationOffset + previous.Event.Length;
                if (allocation.Event.DestinationOffset < previousEnd)
                {
                    throw new InvalidDataException(
                        $"Materialization {allocation.Key} overlaps or precedes another positive range in the same block lifetime.");
                }
            }
            _allocations.Add(allocation);
        }

        public void AccumulateInteriorMatch(
            PhysicalKey key,
            bool requireDecodedOffset,
            ref UniqueAllocationMatch match)
        {
            int index = UpperBound(key.Offset) - 1;
            if (index < 0)
                return;

            CapturedAllocation allocation = _allocations[index];
            if ((!requireDecodedOffset || allocation.Event.DecodedOffset is not null) &&
                allocation.ContainsInterior(key))
            {
                match.Add(allocation);
            }
        }

        public void AccumulateRangeMatch(
            XBlockAddress address,
            int length,
            bool allowEnd,
            ref UniqueAllocationMatch match)
        {
            int index = UpperBound(address.Offset) - 1;
            if (index < 0)
                return;

            CapturedAllocation allocation = _allocations[index];
            if (allocation.ContainsRange(address, length, allowEnd))
            {
                match.Add(allocation);
            }
        }

        public void AccumulatePrecedingRangeMatch(
            XBlockAddress address,
            bool allowEnd,
            ref UniqueAllocationMatch match)
        {
            int index = LowerBound(address.Offset) - 1;
            if (index < 0)
                return;

            CapturedAllocation allocation = _allocations[index];
            if (allocation.ContainsRange(address, 0, allowEnd))
            {
                match.Add(allocation);
            }
        }

        private int UpperBound(int offset)
        {
            int low = 0;
            int high = _allocations.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (_allocations[middle].Event.DestinationOffset <= offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int LowerBound(int offset)
        {
            int low = 0;
            int high = _allocations.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (_allocations[middle].Event.DestinationOffset < offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }
    }
}
