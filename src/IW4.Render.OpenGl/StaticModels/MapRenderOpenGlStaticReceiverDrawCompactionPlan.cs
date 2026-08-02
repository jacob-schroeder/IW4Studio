namespace IW4.Render.OpenGl.StaticModels;

/// <summary>
/// Retains one frame's already-sorted, visible source-instance order for an
/// exact static receiver instance buffer. A buffer is compactable only when
/// every observed draw is a compatible single-pass draw and those draws form
/// one contiguous run in the visible queue. That keeps translucent ordering
/// and authored multipass atomicity unchanged.
/// </summary>
internal sealed class MapRenderOpenGlStaticReceiverDrawCompactionPlan
{
    private readonly int _sourceCapacity;
    private int[]? _sourceIndices;
    private int _firstVisibleOrdinal;
    private int _lastVisibleOrdinal;
    private bool _disqualified;

    internal MapRenderOpenGlStaticReceiverDrawCompactionPlan(
        int sourceCapacity)
    {
        if (sourceCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceCapacity));
        _sourceCapacity = sourceCapacity;
        BeginFrame();
    }

    internal int FirstGroupIndex { get; private set; }

    internal int SourceCount { get; private set; }

    internal bool HasObservation { get; private set; }

    internal bool HasAllocatedSourceScratch => _sourceIndices is not null;

    internal bool CanCompact =>
        HasObservation &&
        !_disqualified &&
        SourceCount > 0 &&
        _lastVisibleOrdinal - _firstVisibleOrdinal + 1 == SourceCount;

    internal ReadOnlySpan<int> SourceIndices =>
        _sourceIndices is null
            ? []
            : _sourceIndices.AsSpan(0, SourceCount);

    internal void BeginFrame()
    {
        FirstGroupIndex = -1;
        SourceCount = 0;
        HasObservation = false;
        _firstVisibleOrdinal = -1;
        _lastVisibleOrdinal = -1;
        _disqualified = false;
    }

    internal void ObserveCandidate(
        int groupIndex,
        int visibleOrdinal,
        int sourceIndex)
    {
        if (groupIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(groupIndex));
        if (visibleOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(visibleOrdinal));
        if ((uint)sourceIndex >= (uint)_sourceCapacity)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        if (SourceCount != 0 &&
            (groupIndex <= FirstGroupIndex ||
             visibleOrdinal <= _lastVisibleOrdinal))
        {
            throw new ArgumentException(
                "Receiver draw observations must follow sorted queue order.");
        }
        if (SourceCount == _sourceCapacity)
        {
            throw new InvalidOperationException(
                "The receiver draw plan observed more rows than its source buffer contains.");
        }

        HasObservation = true;
        if (SourceCount == 0)
        {
            FirstGroupIndex = groupIndex;
            _firstVisibleOrdinal = visibleOrdinal;
        }
        _lastVisibleOrdinal = visibleOrdinal;
        (_sourceIndices ??= new int[_sourceCapacity])[SourceCount++] =
            sourceIndex;
    }

    internal void Disqualify()
    {
        HasObservation = true;
        _disqualified = true;
    }
}
