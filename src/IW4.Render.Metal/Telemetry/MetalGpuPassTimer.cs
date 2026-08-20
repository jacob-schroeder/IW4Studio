using System.Runtime.Versioning;

using IW4.Render.Diagnostics;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Telemetry;

/// <summary>
/// Sparsely samples native Metal render-stage timestamps. One scene or
/// presentation pass is sampled every sixteen submitted frames, so ordinary
/// frames remain counter-free and no frame carries more than one sample
/// buffer attachment.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MetalGpuPassTimer : IDisposable
{
    private const long SampleCycleLength = 32;
    private const ulong SampleCount = 4;
    private const ulong CounterErrorValue = ulong.MaxValue;
    private const ulong MaximumCredibleDurationNanoseconds = 1_000_000_000;

    private readonly MTLCounterSampleBuffer[] _sampleBuffers =
        new MTLCounterSampleBuffer[MetalCommandBufferRing.SlotCount];
    private readonly MapRenderGpuPhase?[] _scheduledPhases =
        new MapRenderGpuPhase?[MetalCommandBufferRing.SlotCount];
    private readonly long[] _frameIndices =
        new long[MetalCommandBufferRing.SlotCount];
    private readonly bool[] _attached =
        new bool[MetalCommandBufferRing.SlotCount];
    private readonly bool _enabled;
    private bool _disposed;

    internal MetalGpuPassTimer(MTLDevice device)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));

        Array.Fill(_frameIndices, -1);
        if (!device.SupportsCounterSampling(
                MTLCounterSamplingPoint.AtStageBoundary))
        {
            return;
        }

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
        _attached[slotIndex] = false;
        if (!_enabled)
        {
            _scheduledPhases[slotIndex] = null;
            return;
        }

        _scheduledPhases[slotIndex] =
            (frameIndex % SampleCycleLength) switch
        {
            0 => (MapRenderGpuPhase?)MapRenderGpuPhase.SceneTarget,
            SampleCycleLength / 2 => MapRenderGpuPhase.Presentation,
            _ => null
        };
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
        if (_scheduledPhases[slotIndex] != phase)
            return;
        if (_attached[slotIndex])
        {
            throw new InvalidOperationException(
                $"Metal GPU phase {phase} was attached more than once in frame " +
                $"{_frameIndices[slotIndex]}.");
        }

        MTLRenderPassSampleBufferAttachmentDescriptor attachment =
            descriptor.SampleBufferAttachments.Object(0);
        attachment.SampleBuffer = _sampleBuffers[slotIndex];
        attachment.StartOfVertexSampleIndex = 0;
        attachment.EndOfVertexSampleIndex = 1;
        attachment.StartOfFragmentSampleIndex = 2;
        attachment.EndOfFragmentSampleIndex = 3;
        _attached[slotIndex] = true;
    }

    internal void Abandon(int slotIndex, long frameIndex)
    {
        if (slotIndex < 0)
            return;
        ValidateSlot(slotIndex);
        if (_frameIndices[slotIndex] != frameIndex)
            return;
        ClearSlot(slotIndex);
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
        bool attached = _attached[slotIndex];
        long scheduledFrameIndex = _frameIndices[slotIndex];
        ClearSlot(slotIndex);
        timing = default;
        if (!_enabled || !attached || phase is null)
            return false;
        if (scheduledFrameIndex != frameIndex)
        {
            throw new InvalidOperationException(
                "The completed Metal GPU sample does not match its frame slot.");
        }

        NSData resolved = _sampleBuffers[slotIndex].ResolveCounterRange(
            new NSRange { location = 0, length = SampleCount });
        if (resolved.NativePtr == 0 ||
            resolved.MutableBytes == 0 ||
            resolved.Length < SampleCount * sizeof(ulong))
        {
            return false;
        }

        ulong* samples = (ulong*)resolved.MutableBytes;
        ulong vertexStart = samples[0];
        ulong vertexEnd = samples[1];
        ulong fragmentStart = samples[2];
        ulong fragmentEnd = samples[3];
        if (vertexStart == CounterErrorValue ||
            vertexEnd == CounterErrorValue ||
            fragmentStart == CounterErrorValue ||
            fragmentEnd == CounterErrorValue)
        {
            return false;
        }

        ulong start = Math.Min(vertexStart, fragmentStart);
        ulong end = Math.Max(vertexEnd, fragmentEnd);
        if (end < start)
            return false;
        ulong elapsedNanoseconds = end - start;
        if (elapsedNanoseconds > MaximumCredibleDurationNanoseconds)
            return false;

        timing = new MapRenderOpenGlGpuPhaseTiming(
            phase.Value,
            frameIndex,
            elapsedNanoseconds,
            readbackDelayFrames);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
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
        _attached[slotIndex] = false;
    }

    private static void ValidateSlot(int slotIndex)
    {
        if ((uint)slotIndex >= MetalCommandBufferRing.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
    }
}
