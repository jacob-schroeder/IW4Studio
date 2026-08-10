using System.Buffers.Binary;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Model;

namespace IW4.Linker;

/// <summary>
/// Loader-side recorder for one decoded zone. It accepts physical coordinates
/// while loading, then freezes them into an occurrence-based object graph.
/// </summary>
public sealed partial class ZoneObjectCapture
{
    private const long RootTempEpoch = 1;

    private readonly byte[] _tape;
    private readonly XFile _layout;
    private readonly List<CapturedAllocation> _allocations = [];
    private readonly Dictionary<CaptureOccurrence, CapturedAllocation> _allocationByOccurrence = [];
    private readonly AllocationIndex _allocationIndex = new();
    private readonly List<CapturedPointer> _pointers = [];
    private readonly Dictionary<CaptureOccurrence, CapturedPointer> _pointerByOccurrence = [];
    private readonly List<CapturedProvider> _providers = [];
    private readonly List<CapturedXString> _strings = [];
    private readonly List<CapturedBoundary> _boundaries = [];
    private readonly Dictionary<CaptureOccurrence, PendingInlineBinding> _inlineBindingByPointer = [];
    private readonly Dictionary<PhysicalKey, List<PendingInlineBinding>> _unboundInlineBindingsByTarget = [];
    private readonly LinkedList<PendingInlineBinding> _unclaimedInsertCells = [];
    private readonly List<TempLifetimeRecord> _tempLifetimes = [];
    private readonly Dictionary<long, TempLifetimeRecord> _tempLifetimeByEpoch = [];
    private readonly Stack<long> _activeTempEpochs = new([RootTempEpoch]);
    private long _nextOccurrence;
    private long _nextTempEpoch = RootTempEpoch;
    private bool _frozen;

    public ZoneObjectCapture(ReadOnlySpan<byte> decodedTape, XFile declaredLayout)
    {
        _tape = decodedTape.ToArray();
        _layout = new XFile(declaredLayout.Size, declaredLayout.ExternalSize, declaredLayout.BlockSizes);
        var rootLifetime = new TempLifetimeRecord(RootTempEpoch, 0, null);
        _tempLifetimes.Add(rootLifetime);
        _tempLifetimeByEpoch.Add(RootTempEpoch, rootLifetime);
    }

    public long RootEpoch => RootTempEpoch;

    public long EnterTempEpoch()
    {
        ThrowIfFrozen();
        long epoch = checked(++_nextTempEpoch);
        long parentEpoch = _activeTempEpochs.Peek();
        _activeTempEpochs.Push(epoch);
        var lifetime = new TempLifetimeRecord(epoch, NextSequence, parentEpoch);
        _tempLifetimes.Add(lifetime);
        if (!_tempLifetimeByEpoch.TryAdd(epoch, lifetime))
            throw new InvalidDataException($"TEMP lifetime {epoch} was recorded more than once.");
        return epoch;
    }

    public void RetireTempEpoch(long epoch)
    {
        ThrowIfFrozen();
        if (epoch == RootTempEpoch || _activeTempEpochs.Count <= 1 || _activeTempEpochs.Peek() != epoch)
            throw new InvalidDataException("TEMP lifetime stack is unbalanced.");

        _activeTempEpochs.Pop();
        TempLifetimeRecord lifetime = LifetimeFor(epoch);
        lifetime.EndSequence = NextSequence;
    }
}
