using System.Runtime.Versioning;

using IW4.Render.Diagnostics;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Telemetry;

/// <summary>
/// Sparsely samples native Metal render-stage and draw-boundary timestamps.
/// Draw-boundary devices sample every GPU phase in turn. Stage-boundary-only
/// devices sample only phases that already own a render pass, avoiding target
/// store/load breaks whose cost would contaminate normal rendering.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalGpuPassTimer : IDisposable
{
    private static readonly MapRenderGpuPhase[] DrawBoundaryPhases =
        Enum.GetValues<MapRenderGpuPhase>();
    private static readonly MapRenderGpuPhase[] StageBoundaryPhases =
    [
        MapRenderGpuPhase.SunShadow,
        MapRenderGpuPhase.SceneTarget,
        MapRenderGpuPhase.ProcessedFloatZ,
        MapRenderGpuPhase.Presentation
    ];

    private const int FramesPerPhaseSample = 2;
    private const ulong SampleCount = 128;
    private const int MaximumRangesPerFrame = 64;
    private const ulong CounterErrorValue = ulong.MaxValue;
    private const ulong MaximumCredibleDurationNanoseconds = 1_000_000_000;

    private readonly MTLCounterSampleBuffer[] _sampleBuffers =
        new MTLCounterSampleBuffer[MetalCommandBufferRing.SlotCount];
    private readonly MapRenderGpuPhase?[] _scheduledPhases =
        new MapRenderGpuPhase?[MetalCommandBufferRing.SlotCount];
    private readonly long[] _frameIndices =
        new long[MetalCommandBufferRing.SlotCount];
    private readonly ulong[] _nextSampleIndices =
        new ulong[MetalCommandBufferRing.SlotCount];
    private readonly int[] _rangeCounts =
        new int[MetalCommandBufferRing.SlotCount];
    private readonly MetalGpuSampleRange[] _ranges =
        new MetalGpuSampleRange[
            MetalCommandBufferRing.SlotCount * MaximumRangesPerFrame];
    private readonly long[] _activeTokens =
        new long[MetalCommandBufferRing.SlotCount];
    private readonly MapRenderGpuPhase[] _samplingPhases;
    private readonly int _samplingCycleFrameCount;
    private readonly bool _supportsStageBoundary;
    private readonly bool _supportsDrawBoundary;
    private bool _enabled;
    private int _currentSlotIndex = -1;
    private long _nextToken;
    private bool _disposed;

    internal MetalGpuPassTimer(MTLDevice device)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));

        Array.Fill(_frameIndices, -1);
        _supportsStageBoundary = device.SupportsCounterSampling(
            MTLCounterSamplingPoint.AtStageBoundary);
        _supportsDrawBoundary = device.SupportsCounterSampling(
            MTLCounterSamplingPoint.AtDrawBoundary);
        _samplingPhases = _supportsDrawBoundary
            ? DrawBoundaryPhases
            : StageBoundaryPhases;
        _samplingCycleFrameCount = checked(
            _samplingPhases.Length * FramesPerPhaseSample);
        if (!_supportsStageBoundary && !_supportsDrawBoundary)
            return;

        MTLCounterSet timestampSet = FindTimestampCounterSet(device);
        if (timestampSet.NativePtr == 0)
            return;

        try
        {
            for (int slot = 0; slot < _sampleBuffers.Length; slot++)
            {
                using var descriptor = new MTLCounterSampleBufferDescriptor
                {
                    CounterSet = timestampSet,
                    SampleCount = SampleCount,
                    StorageMode = MTLStorageMode.Shared
                };
                NSError error = default;
                MTLCounterSampleBuffer sampleBuffer =
                    device.NewCounterSampleBuffer(descriptor, ref error);
                if (sampleBuffer.NativePtr == 0)
                    return;
                _sampleBuffers[slot] = sampleBuffer;
            }
            _enabled = true;
        }
        finally
        {
            if (!_enabled)
                DeleteSampleBuffers();
        }
    }

    internal void BeginFrame(int slotIndex, long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slotIndex);
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        _frameIndices[slotIndex] = frameIndex;
        _currentSlotIndex = slotIndex;
        _nextSampleIndices[slotIndex] = 0;
        _rangeCounts[slotIndex] = 0;
        _activeTokens[slotIndex] = 0;
        if (!_enabled)
        {
            _scheduledPhases[slotIndex] = null;
            return;
        }

        int cycleFrame = checked(
            (int)(frameIndex % _samplingCycleFrameCount));
        _scheduledPhases[slotIndex] =
            cycleFrame % FramesPerPhaseSample == 0
                ? _samplingPhases[cycleFrame / FramesPerPhaseSample]
                : null;
    }

    internal void AttachPass(
        MTLRenderPassDescriptor descriptor,
        int slotIndex,
        MapRenderGpuPhase phase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (descriptor.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal render-pass descriptor is required.",
                nameof(descriptor));
        }
        ValidateSlot(slotIndex);
        if (!_supportsStageBoundary ||
            _scheduledPhases[slotIndex] != phase)
            return;
        if (!TryReserveSamples(slotIndex, 4, out ulong firstSampleIndex))
            return;

        MTLRenderPassSampleBufferAttachmentDescriptor attachment =
            descriptor.SampleBufferAttachments.Object(0);
        attachment.SampleBuffer = _sampleBuffers[slotIndex];
        attachment.StartOfVertexSampleIndex = firstSampleIndex;
        attachment.EndOfVertexSampleIndex = firstSampleIndex + 1;
        attachment.StartOfFragmentSampleIndex = firstSampleIndex + 2;
        attachment.EndOfFragmentSampleIndex = firstSampleIndex + 3;
        AddRange(
            slotIndex,
            new MetalGpuSampleRange(
                MetalGpuSampleRangeKind.StageBoundary,
                firstSampleIndex,
                firstSampleIndex + 1,
                firstSampleIndex + 2,
                firstSampleIndex + 3));
    }

    internal void AttachPass(
        MTLRenderPassDescriptor descriptor,
        MapRenderGpuPhase phase)
    {
        if (_currentSlotIndex < 0)
            return;
        AttachPass(descriptor, _currentSlotIndex, phase);
    }

    internal MetalGpuPhaseScope BeginPhase(
        MTLRenderCommandEncoder encoder,
        MapRenderGpuPhase phase)
    {
        if (_currentSlotIndex < 0)
            return default;
        return BeginPhase(encoder, _currentSlotIndex, phase);
    }

    internal MetalGpuPhaseScope BeginPhase(
        MTLRenderCommandEncoder encoder,
        int slotIndex,
        MapRenderGpuPhase phase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoder.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal render encoder is required.",
                nameof(encoder));
        }
        ValidateSlot(slotIndex);
        if (!_supportsDrawBoundary ||
            _scheduledPhases[slotIndex] != phase)
        {
            return default;
        }
        if (_activeTokens[slotIndex] != 0)
        {
            throw new InvalidOperationException(
                "Metal GPU timing intervals cannot overlap.");
        }
        if (!TryReserveSamples(slotIndex, 2, out ulong firstSampleIndex))
            return default;

        long token = ++_nextToken;
        _activeTokens[slotIndex] = token;
        encoder.SampleCountersInBuffer(
            _sampleBuffers[slotIndex],
            firstSampleIndex,
            true);
        return new MetalGpuPhaseScope(
            this,
            encoder,
            slotIndex,
            _frameIndices[slotIndex],
            token,
            firstSampleIndex,
            firstSampleIndex + 1);
    }

    internal void Abandon(int slotIndex, long frameIndex)
    {
        if (slotIndex < 0)
            return;
        ValidateSlot(slotIndex);
        if (_frameIndices[slotIndex] != frameIndex)
            return;
        ClearSlot(slotIndex);
        if (_currentSlotIndex == slotIndex)
            _currentSlotIndex = -1;
    }

    internal bool TryCollect(
        int slotIndex,
        long frameIndex,
        int readbackDelayFrames,
        out MapRenderOpenGlGpuPhaseTiming timing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSlot(slotIndex);
        if (readbackDelayFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(readbackDelayFrames));

        MapRenderGpuPhase? phase = _scheduledPhases[slotIndex];
        int rangeCount = _rangeCounts[slotIndex];
        ulong sampleCount = _nextSampleIndices[slotIndex];
        long scheduledFrameIndex = _frameIndices[slotIndex];
        timing = default;
        if (!_enabled || rangeCount == 0 || phase is null)
        {
            ClearSlot(slotIndex);
            return false;
        }
        if (scheduledFrameIndex != frameIndex)
        {
            ClearSlot(slotIndex);
            throw new InvalidOperationException(
                "The completed Metal GPU sample does not match its frame slot.");
        }

        NSData resolved = _sampleBuffers[slotIndex].ResolveCounterRange(
            new NSRange { location = 0, length = sampleCount });
        if (resolved.NativePtr == 0 ||
            resolved.MutableBytes == 0 ||
            resolved.Length < sampleCount * sizeof(ulong))
        {
            ClearSlot(slotIndex);
            return false;
        }

        ulong* samples = (ulong*)resolved.MutableBytes;
        ulong elapsedNanoseconds = 0;
        int rangeOffset = slotIndex * MaximumRangesPerFrame;
        for (int rangeIndex = 0; rangeIndex < rangeCount; rangeIndex++)
        {
            MetalGpuSampleRange range = _ranges[rangeOffset + rangeIndex];
            ulong start;
            ulong end;
            if (range.Kind == MetalGpuSampleRangeKind.StageBoundary)
            {
                ulong vertexStart = samples[range.StartIndex];
                ulong vertexEnd = samples[range.EndIndex];
                ulong fragmentStart = samples[range.FragmentStartIndex];
                ulong fragmentEnd = samples[range.FragmentEndIndex];
                if (vertexStart == CounterErrorValue ||
                    vertexEnd == CounterErrorValue ||
                    fragmentStart == CounterErrorValue ||
                    fragmentEnd == CounterErrorValue)
                {
                    ClearSlot(slotIndex);
                    return false;
                }
                start = Math.Min(vertexStart, fragmentStart);
                end = Math.Max(vertexEnd, fragmentEnd);
            }
            else
            {
                start = samples[range.StartIndex];
                end = samples[range.EndIndex];
                if (start == CounterErrorValue || end == CounterErrorValue)
                {
                    ClearSlot(slotIndex);
                    return false;
                }
            }
            if (end < start)
            {
                ClearSlot(slotIndex);
                return false;
            }
            ulong duration = end - start;
            if (duration > MaximumCredibleDurationNanoseconds ||
                elapsedNanoseconds >
                    MaximumCredibleDurationNanoseconds - duration)
            {
                ClearSlot(slotIndex);
                return false;
            }
            elapsedNanoseconds += duration;
        }

        timing = new MapRenderOpenGlGpuPhaseTiming(
            phase.Value,
            frameIndex,
            elapsedNanoseconds,
            readbackDelayFrames);
        ClearSlot(slotIndex);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _currentSlotIndex = -1;
        DeleteSampleBuffers();
        for (int slot = 0; slot < _frameIndices.Length; slot++)
            ClearSlot(slot);
    }

    private static MTLCounterSet FindTimestampCounterSet(MTLDevice device)
    {
        NSArray sets = device.CounterSets;
        for (ulong setIndex = 0; setIndex < sets.Count; setIndex++)
        {
            var set = new MTLCounterSet(sets.Object(setIndex));
            if (!string.Equals(
                    set.Name.ToString(),
                    "timestamp",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            NSArray counters = set.Counters;
            for (ulong counterIndex = 0;
                 counterIndex < counters.Count;
                 counterIndex++)
            {
                var counter = new MTLCounter(counters.Object(counterIndex));
                if (string.Equals(
                        counter.Name.ToString(),
                        "GPUTimestamp",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return set;
                }
            }
        }
        return default;
    }

    private void DeleteSampleBuffers()
    {
        for (int slot = 0; slot < _sampleBuffers.Length; slot++)
        {
            if (_sampleBuffers[slot].NativePtr == 0)
                continue;
            _sampleBuffers[slot].Dispose();
            _sampleBuffers[slot] = default;
        }
    }

    private void ClearSlot(int slotIndex)
    {
        _scheduledPhases[slotIndex] = null;
        _frameIndices[slotIndex] = -1;
        _nextSampleIndices[slotIndex] = 0;
        _rangeCounts[slotIndex] = 0;
        _activeTokens[slotIndex] = 0;
    }

    private bool TryReserveSamples(
        int slotIndex,
        ulong count,
        out ulong firstSampleIndex)
    {
        firstSampleIndex = _nextSampleIndices[slotIndex];
        if (firstSampleIndex + count > SampleCount ||
            _rangeCounts[slotIndex] >= MaximumRangesPerFrame)
        {
            return false;
        }
        _nextSampleIndices[slotIndex] = firstSampleIndex + count;
        return true;
    }

    private void AddRange(int slotIndex, MetalGpuSampleRange range)
    {
        int rangeIndex = _rangeCounts[slotIndex]++;
        _ranges[slotIndex * MaximumRangesPerFrame + rangeIndex] = range;
    }

    private void EndPhase(
        MTLRenderCommandEncoder encoder,
        int slotIndex,
        long frameIndex,
        long token,
        ulong startSampleIndex,
        ulong endSampleIndex)
    {
        if (_disposed ||
            _frameIndices[slotIndex] != frameIndex ||
            _activeTokens[slotIndex] != token)
        {
            return;
        }
        encoder.SampleCountersInBuffer(
            _sampleBuffers[slotIndex],
            endSampleIndex,
            true);
        AddRange(
            slotIndex,
            new MetalGpuSampleRange(
                MetalGpuSampleRangeKind.DrawBoundary,
                startSampleIndex,
                endSampleIndex,
                0,
                0));
        _activeTokens[slotIndex] = 0;
    }

    private static void ValidateSlot(int slotIndex)
    {
        if ((uint)slotIndex >= MetalCommandBufferRing.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
    }

    internal readonly struct MetalGpuPhaseScope : IDisposable
    {
        private readonly MetalGpuPassTimer? _owner;
        private readonly MTLRenderCommandEncoder _encoder;
        private readonly int _slotIndex;
        private readonly long _frameIndex;
        private readonly long _token;
        private readonly ulong _startSampleIndex;
        private readonly ulong _endSampleIndex;

        internal MetalGpuPhaseScope(
            MetalGpuPassTimer owner,
            MTLRenderCommandEncoder encoder,
            int slotIndex,
            long frameIndex,
            long token,
            ulong startSampleIndex,
            ulong endSampleIndex)
        {
            _owner = owner;
            _encoder = encoder;
            _slotIndex = slotIndex;
            _frameIndex = frameIndex;
            _token = token;
            _startSampleIndex = startSampleIndex;
            _endSampleIndex = endSampleIndex;
        }

        public void Dispose() => _owner?.EndPhase(
            _encoder,
            _slotIndex,
            _frameIndex,
            _token,
            _startSampleIndex,
            _endSampleIndex);
    }

    private readonly record struct MetalGpuSampleRange(
        MetalGpuSampleRangeKind Kind,
        ulong StartIndex,
        ulong EndIndex,
        ulong FragmentStartIndex,
        ulong FragmentEndIndex);

    private enum MetalGpuSampleRangeKind : byte
    {
        DrawBoundary,
        StageBoundary
    }
}
