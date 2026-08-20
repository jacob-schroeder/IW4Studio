using System.Runtime.Versioning;

using IW4.Render.Metal.Native;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Telemetry;

/// <summary>
/// Bounds CPU/GPU overlap to three frames without synchronizing ordinary
/// submissions. A slot is waited only when the CPU catches the GPU, and the
/// completed command buffer supplies allocation-free whole-frame GPU timing.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalCommandBufferRing : IDisposable
{
    internal const int SlotCount = 3;
    private const int CompletionCapacity = SlotCount + 1;

    private readonly MTLCommandQueue _queue;
    private readonly MTLCommandBuffer[] _slots = new MTLCommandBuffer[SlotCount];
    private readonly long[] _frameIndices = new long[SlotCount];
    private readonly long[] _completedFrameIndices =
        new long[CompletionCapacity];
    private readonly double[] _completedMilliseconds =
        new double[CompletionCapacity];
    private readonly bool[] _completedHasFrameTiming =
        new bool[CompletionCapacity];
    private readonly int[] _completedSlotIndices =
        new int[CompletionCapacity];
    private int _nextSlot;
    private int _completionCount;
    private bool _disposed;

    internal MetalCommandBufferRing(MTLCommandQueue queue)
    {
        if (queue.NativePtr == 0)
            throw new ArgumentException("A Metal command queue is required.", nameof(queue));
        _queue = queue;
        Array.Fill(_frameIndices, -1);
    }

    internal MTLCommandBuffer Begin(long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        Retire(_nextSlot, wait: true);
        MTLCommandBuffer commandBuffer = _queue.CommandBuffer();
        if (commandBuffer.NativePtr == 0)
            throw new InvalidOperationException("Metal did not provide a command buffer.");

        // queue.commandBuffer is autoreleased. The ring crosses the frame's
        // autorelease-pool boundary, so take exactly one balancing retain.
        MetalObjectiveC.Retain(commandBuffer.NativePtr);
        _slots[_nextSlot] = commandBuffer;
        _frameIndices[_nextSlot] = frameIndex;
        _nextSlot = (_nextSlot + 1) % SlotCount;
        return commandBuffer;
    }

    internal void Abandon(MTLCommandBuffer commandBuffer)
    {
        if (commandBuffer.NativePtr == 0)
            return;
        for (int index = 0; index < _slots.Length; index++)
        {
            if (_slots[index].NativePtr != commandBuffer.NativePtr)
                continue;
            commandBuffer.Dispose();
            _slots[index] = default;
            _frameIndices[index] = -1;
            return;
        }
    }

    internal void DrainCompleted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int index = 0; index < _slots.Length; index++)
            Retire(index, wait: false);
    }

    internal void WaitForIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int index = 0; index < _slots.Length; index++)
            Retire(index, wait: true);
    }

    internal bool TryDequeueGpuTiming(
        out long frameIndex,
        out double milliseconds,
        out bool hasFrameTiming,
        out int slotIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completionCount == 0)
        {
            frameIndex = -1;
            milliseconds = 0.0;
            hasFrameTiming = false;
            slotIndex = -1;
            return false;
        }

        int earliestIndex = 0;
        for (int index = 1; index < _completionCount; index++)
        {
            if (_completedFrameIndices[index] <
                _completedFrameIndices[earliestIndex])
            {
                earliestIndex = index;
            }
        }
        frameIndex = _completedFrameIndices[earliestIndex];
        milliseconds = _completedMilliseconds[earliestIndex];
        hasFrameTiming = _completedHasFrameTiming[earliestIndex];
        slotIndex = _completedSlotIndices[earliestIndex];
        int lastIndex = --_completionCount;
        if (earliestIndex != lastIndex)
        {
            _completedFrameIndices[earliestIndex] =
                _completedFrameIndices[lastIndex];
            _completedMilliseconds[earliestIndex] =
                _completedMilliseconds[lastIndex];
            _completedHasFrameTiming[earliestIndex] =
                _completedHasFrameTiming[lastIndex];
            _completedSlotIndices[earliestIndex] =
                _completedSlotIndices[lastIndex];
        }
        _completedFrameIndices[lastIndex] = 0;
        _completedMilliseconds[lastIndex] = 0.0;
        _completedHasFrameTiming[lastIndex] = false;
        _completedSlotIndices[lastIndex] = 0;
        return true;
    }

    internal int ResolveSlot(MTLCommandBuffer commandBuffer)
    {
        if (commandBuffer.NativePtr == 0)
            throw new ArgumentException(
                "A Metal command buffer is required.",
                nameof(commandBuffer));
        for (int index = 0; index < _slots.Length; index++)
        {
            if (_slots[index].NativePtr == commandBuffer.NativePtr)
                return index;
        }
        throw new InvalidOperationException(
            "The Metal command buffer is not owned by this frame ring.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        for (int index = 0; index < _slots.Length; index++)
            Retire(index, wait: true, recordTiming: false);
        _completionCount = 0;
        _disposed = true;
    }

    private void Retire(int slot, bool wait, bool recordTiming = true)
    {
        MTLCommandBuffer commandBuffer = _slots[slot];
        if (commandBuffer.NativePtr == 0)
            return;

        MTLCommandBufferStatus status = commandBuffer.Status;
        if (status is not (MTLCommandBufferStatus.Completed or
                           MTLCommandBufferStatus.Error))
        {
            if (!wait)
                return;
            commandBuffer.WaitUntilCompleted();
            status = commandBuffer.Status;
        }

        try
        {
            if (status == MTLCommandBufferStatus.Error)
            {
                string detail = commandBuffer.Error.NativePtr == 0
                    ? "unknown command-buffer error"
                    : commandBuffer.Error.LocalizedDescription.ToString() ??
                      "unknown command-buffer error";
                throw new InvalidOperationException(
                    $"Metal frame {_frameIndices[slot]} failed: {detail}");
            }
            if (status != MTLCommandBufferStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Metal frame {_frameIndices[slot]} stopped in status {status}.");
            }

            double start = MetalObjectiveC.GetGpuStartTime(commandBuffer.NativePtr);
            double end = MetalObjectiveC.GetGpuEndTime(commandBuffer.NativePtr);
            if (recordTiming)
            {
                bool hasFrameTiming =
                    double.IsFinite(start) &&
                    double.IsFinite(end) &&
                    start > 0.0 &&
                    end >= start;
                EnqueueGpuTiming(
                    _frameIndices[slot],
                    hasFrameTiming ? (end - start) * 1000.0 : 0.0,
                    hasFrameTiming,
                    slot);
            }
        }
        finally
        {
            // Balances the explicit retain in Begin.
            commandBuffer.Dispose();
            _slots[slot] = default;
            _frameIndices[slot] = -1;
        }
    }

    private void EnqueueGpuTiming(
        long frameIndex,
        double milliseconds,
        bool hasFrameTiming,
        int slotIndex)
    {
        if (_completionCount == CompletionCapacity)
        {
            throw new InvalidOperationException(
                "Metal GPU timing completions exceeded the bounded frame ring.");
        }
        _completedFrameIndices[_completionCount] = frameIndex;
        _completedMilliseconds[_completionCount] = milliseconds;
        _completedHasFrameTiming[_completionCount] = hasFrameTiming;
        _completedSlotIndices[_completionCount] = slotIndex;
        _completionCount++;
    }
}
