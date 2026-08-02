namespace IW4.Runtime.Assets.Lifecycle.State;

public sealed class GfxImageRuntimeState : IGfxImageRuntimeState
{
    private Dictionary<XAssetRuntimeAllocationKey, GfxImageRuntimeRecord> _records = [];
    private List<GfxImageCardMemoryRange> _allocatedRanges = [];
    private List<GfxImageCardMemoryRange> _freeRanges = [];

    public IReadOnlyList<GfxImageCardMemoryRange> AllocatedRanges =>
        Array.AsReadOnly(_allocatedRanges.ToArray());

    public IReadOnlyList<GfxImageCardMemoryRange> FreeRanges =>
        Array.AsReadOnly(_freeRanges.ToArray());

    public bool IsCardMemoryTableDirty { get; private set; }

    public bool TryGet(
        XAssetRuntimeAllocationKey allocation,
        out GfxImageRuntimeRecord? record) =>
        _records.TryGetValue(allocation, out record);

    public void Set(
        XAssetRuntimeAllocationKey allocation,
        GfxImageRuntimeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.SideRecord);
        _records[allocation] = record;
    }

    public void AddAllocatedRange(GfxImageCardMemoryRange range)
    {
        if (_allocatedRanges.Any(existing => Overlaps(existing, range)))
            throw new InvalidOperationException("GfxImage allocated card-memory ranges cannot overlap.");

        _allocatedRanges.Add(range);
        _allocatedRanges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
    }

    public void AddFreeRange(GfxImageCardMemoryRange range) =>
        InsertAndCoalesceFreeRange(range);

    public bool ReleaseFirstOverlappingRange(uint start, uint length)
    {
        if (length == 0)
            return false;

        uint end = checked(start + length);
        var requested = new GfxImageCardMemoryRange(start, end);
        int index = _allocatedRanges.FindIndex(range => Overlaps(range, requested));
        if (index < 0)
            return false;

        GfxImageCardMemoryRange released = _allocatedRanges[index];
        _allocatedRanges.RemoveAt(index);
        InsertAndCoalesceFreeRange(released);
        IsCardMemoryTableDirty = true;
        return true;
    }

    public IXAssetRuntimeStateSnapshot CaptureSnapshot() =>
        new GfxImageRuntimeSnapshot(
            new Dictionary<XAssetRuntimeAllocationKey, GfxImageRuntimeRecord>(_records),
            _allocatedRanges.ToArray(),
            _freeRanges.ToArray(),
            IsCardMemoryTableDirty);

    public void RestoreSnapshot(IXAssetRuntimeStateSnapshot snapshot)
    {
        if (snapshot is not GfxImageRuntimeSnapshot typed)
            throw new ArgumentException("Snapshot does not belong to GfxImage runtime state.", nameof(snapshot));

        _records = new Dictionary<XAssetRuntimeAllocationKey, GfxImageRuntimeRecord>(typed.Records);
        _allocatedRanges = typed.AllocatedRanges.ToList();
        _freeRanges = typed.FreeRanges.ToList();
        IsCardMemoryTableDirty = typed.IsCardMemoryTableDirty;
    }

    private void InsertAndCoalesceFreeRange(GfxImageCardMemoryRange incoming)
    {
        uint start = incoming.Start;
        uint end = incoming.End;
        int firstMergedIndex = _freeRanges.Count;

        for (int index = 0; index < _freeRanges.Count; index++)
        {
            GfxImageCardMemoryRange existing = _freeRanges[index];
            if (existing.End < start)
                continue;
            if (end < existing.Start)
            {
                firstMergedIndex = index;
                break;
            }

            firstMergedIndex = Math.Min(firstMergedIndex, index);
            start = Math.Min(start, existing.Start);
            end = Math.Max(end, existing.End);
            _freeRanges.RemoveAt(index--);
        }

        if (firstMergedIndex > _freeRanges.Count)
            firstMergedIndex = _freeRanges.Count;
        _freeRanges.Insert(firstMergedIndex, new GfxImageCardMemoryRange(start, end));
    }

    private static bool Overlaps(
        GfxImageCardMemoryRange left,
        GfxImageCardMemoryRange right) =>
        left.Start < right.End && right.Start < left.End;
}
