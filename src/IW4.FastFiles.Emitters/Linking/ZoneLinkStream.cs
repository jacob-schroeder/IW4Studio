using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Linking;

/// <summary>
/// A byte offset on the sequential decoded-zone source tape. It is
/// deliberately distinct from <see cref="EmissionAddress"/>, which addresses
/// one of the seven destination blocks.
/// </summary>
internal readonly record struct SourceTapeOffset
{
    public SourceTapeOffset(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public int Value { get; }
    public override string ToString() => $"source+0x{Value:X}";
}

/// <summary>
/// Stateful PS3 decoded-zone link stream. It owns the sequential source tape
/// and the seven-block allocation plan.
/// </summary>
internal sealed class ZoneLinkStream
{
    private readonly EmissionPlan _plan;
    private readonly XSourceWriter _source = new();
    private bool _completed;

    internal ZoneLinkStream(EmissionPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public SourceTapeOffset SourcePosition => new(_source.Position);
    public IReadOnlyList<int> HighWater => _plan.HighWater;

    public SourceTapeOffset Reserve(int byteCount)
    {
        EnsureOpen();
        if (byteCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        SourceTapeOffset start = SourcePosition;
        _source.Reserve(byteCount);
        return start;
    }

    public SourceTapeOffset Append(ReadOnlySpan<byte> bytes)
    {
        EnsureOpen();
        if (bytes.Length == 0)
            throw new ArgumentException("A source range cannot be empty.", nameof(bytes));

        SourceTapeOffset start = SourcePosition;
        _source.WriteBytes(bytes);
        return start;
    }

    public SourceTapeOffset WriteInt32(int value)
    {
        var writer = new XSourceWriter();
        writer.WriteInt32(value);
        return Append(writer.ToArray());
    }

    /// <summary>
    /// Appends an already planned legacy body in native source order. Every
    /// segment must map to a distinct compatible allocation occurrence.
    /// Non-TEMP materialized ranges may never overlap. Native-scoped TEMP
    /// reuse is accepted only when separate allocation occurrences prove it.
    /// </summary>
    public void AppendLegacyBodies(
        IEnumerable<KeyValuePair<ZoneAssetKey, AssetBodyEmission>> bodies)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(bodies);

        IReadOnlyList<EmissionAllocation> allocations = _plan.Allocations;
        var rangesByAllocation = new Dictionary<int, List<(int Start, int End)>>();
        var persistentRanges = new List<(XFileBlockType Block, int Start, int End, ZoneAssetKey Owner)>();
        int lastAllocationIndex = -1;

        foreach (KeyValuePair<ZoneAssetKey, AssetBodyEmission> pair in bodies)
        {
            ZoneAssetKey key = pair.Key;
            foreach (EmissionBlockSegment segment in pair.Value.SourceSegments)
            {
                int start = segment.Address.Offset;
                int end = checked(start + segment.Bytes.Length);
                if (segment.Bytes.Length == 0 || start < 0)
                {
                    throw new InvalidDataException(
                        $"Legacy source segment for '{key}' has an invalid destination range: " +
                        $"{segment.Address} length=0x{segment.Bytes.Length:X}.");
                }

                int allocationIndex = FindAllocation(
                    allocations,
                    rangesByAllocation,
                    segment.Address.Block,
                    start,
                    end,
                    lastAllocationIndex);
                if (allocationIndex < 0)
                {
                    int earlierIndex = -1;
                    for (int index = 0; index < Math.Max(0, lastAllocationIndex); index++)
                    {
                        EmissionAllocation allocation = allocations[index];
                        if (allocation.Block == segment.Address.Block &&
                            allocation.Offset <= start &&
                            checked(allocation.Offset + allocation.Size) >= end)
                        {
                            earlierIndex = index;
                            break;
                        }
                    }
                    bool belongsToEarlierAllocation = earlierIndex >= 0;
                    string earlierContext = earlierIndex >= 0
                        ? $" Allocation #{earlierIndex} '{allocations[earlierIndex].Owner}' covers " +
                          $"0x{allocations[earlierIndex].Offset:X}-0x{checked(allocations[earlierIndex].Offset + allocations[earlierIndex].Size):X}, " +
                          $"after source traversal already advanced to allocation #{lastAllocationIndex}. " +
                          "Nearby allocations: " +
                          string.Join(
                              ", ",
                              allocations
                                  .Select((allocation, index) => (allocation, index))
                                  .Skip(Math.Max(0, earlierIndex - 2))
                                  .Take(checked(lastAllocationIndex - Math.Max(0, earlierIndex - 2) + 2))
                                  .Select(value =>
                                      $"#{value.index} {value.allocation.Block}:" +
                                      $"0x{value.allocation.Offset:X}-" +
                                      $"0x{checked(value.allocation.Offset + value.allocation.Size):X}" +
                                      (value.allocation.Owner is null ? string.Empty : $" '{value.allocation.Owner}'")))
                          + "."
                        : string.Empty;
                    throw new InvalidDataException(
                        belongsToEarlierAllocation
                            ? $"Legacy source segment for '{key}' appears in contradictory source order at " +
                              $"{segment.Address.Block}:0x{start:X}-0x{end:X}.{earlierContext}"
                            : $"Legacy source segment for '{key}' targets unallocated {segment.Address.Block} " +
                              $"range 0x{start:X}-0x{end:X}.");
                }

                if (segment.Address.Block != XFileBlockType.TEMP ||
                    _plan.TempAllocationMode == TempAllocationMode.LegacyMonotonic)
                {
                    foreach ((XFileBlockType block, int otherStart, int otherEnd, ZoneAssetKey otherOwner) in persistentRanges)
                    {
                        if (block == segment.Address.Block && start < otherEnd && otherStart < end)
                        {
                            throw new InvalidDataException(
                                $"Legacy source segments for '{otherOwner}' and '{key}' overlap in {block}: " +
                                $"0x{otherStart:X}-0x{otherEnd:X} versus 0x{start:X}-0x{end:X}.");
                        }
                    }
                    persistentRanges.Add((segment.Address.Block, start, end, key));
                }

                if (!rangesByAllocation.TryGetValue(allocationIndex, out List<(int Start, int End)>? allocationRanges))
                {
                    allocationRanges = [];
                    rangesByAllocation.Add(allocationIndex, allocationRanges);
                }
                if (allocationRanges.Any(range => start < range.End && range.Start < end))
                {
                    throw new InvalidDataException(
                        $"Legacy source segment for '{key}' overlaps another segment in one allocation at " +
                        $"{segment.Address.Block}:0x{start:X}-0x{end:X}.");
                }
                allocationRanges.Add((start, end));
                lastAllocationIndex = allocationIndex;
                Append(segment.Bytes.Span);
            }
        }
    }

    public byte[] Complete()
    {
        EnsureOpen();
        _plan.EnsureBalanced();
        _completed = true;
        return _source.ToArray();
    }

    private static int FindAllocation(
        IReadOnlyList<EmissionAllocation> allocations,
        IReadOnlyDictionary<int, List<(int Start, int End)>> rangesByAllocation,
        XFileBlockType block,
        int start,
        int end,
        int lastAllocationIndex)
    {
        int first = Math.Max(0, lastAllocationIndex);
        for (int index = first; index < allocations.Count; index++)
        {
            EmissionAllocation allocation = allocations[index];
            if (allocation.Block != block ||
                allocation.Offset > start ||
                checked(allocation.Offset + allocation.Size) < end)
            {
                continue;
            }

            if (rangesByAllocation.TryGetValue(index, out List<(int Start, int End)>? ranges) &&
                ranges.Any(range => start < range.End && range.Start < end))
            {
                continue;
            }
            return index;
        }
        return -1;
    }

    private void EnsureOpen()
    {
        if (_completed)
            throw new InvalidOperationException("The PS3 zone link stream is already complete.");
    }
}
