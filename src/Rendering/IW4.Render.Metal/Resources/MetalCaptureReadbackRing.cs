using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using IW4.Render.Capture;
using IW4.Render.Metal.Native;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// Bounded reusable staging storage for deferred Metal texture readback.
/// Copies are submitted to the renderer's command queue and completed on a
/// worker so capture never waits the normal render thread for the GPU.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalCaptureReadbackRing : IDisposable
{
    private const int SlotCount = 3;

    private readonly object _gate = new();
    private readonly MTLDevice _device;
    private readonly MTLCommandQueue _queue;
    private readonly bool _hasUnifiedMemory;
    private readonly Slot[] _slots = new Slot[SlotCount];
    private bool _disposed;

    internal MetalCaptureReadbackRing(
        MTLDevice device,
        MTLCommandQueue queue)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        if (queue.NativePtr == 0)
            throw new ArgumentException("A Metal command queue is required.", nameof(queue));

        _device = device;
        _queue = queue;
        _hasUnifiedMemory = device.HasUnifiedMemory;
        for (int index = 0; index < _slots.Length; index++)
            _slots[index] = new Slot();
    }

    internal Task<MapRenderCaptureResult> Enqueue(
        MapRenderCaptureRequest request,
        MTLTexture source,
        bool sourceIsBgra,
        MTLCommandBuffer producerSubmission,
        Func<long> resolveCompletionFrameIndex)
    {
        ArgumentNullException.ThrowIfNull(resolveCompletionFrameIndex);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Slot? slot = null;
            for (int index = 0; index < _slots.Length; index++)
            {
                Slot candidate = _slots[index];
                if (candidate.ActiveTask is { IsCompleted: false })
                    continue;

                candidate.ActiveTask = null;
                slot = candidate;
                break;
            }

            if (slot is null)
            {
                return Task.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    "Metal capture has three GPU readbacks in flight; retry after one completes."));
            }

            Task<MapRenderCaptureResult> task;
            try
            {
                task = Submit(
                    slot,
                    request,
                    source,
                    sourceIsBgra,
                    producerSubmission,
                    resolveCompletionFrameIndex);
            }
            catch (Exception error)
            {
                return Task.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    $"Metal RGBA8 readback submission failed: {error.Message}"));
            }

            slot.ActiveTask = task;
            _ = task.ContinueWith(
                completed => ReleaseCompletedSlot(slot, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    internal void WaitForIdle()
    {
        Task[] active;
        lock (_gate)
        {
            active = _slots
                .Select(slot => slot.ActiveTask)
                .Where(task => task is not null && !task.IsCompleted)
                .Cast<Task>()
                .ToArray();
        }

        if (active.Length != 0)
            Task.WaitAll(active);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        WaitForIdle();
        lock (_gate)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Buffer.NativePtr != 0)
                    slot.Buffer.Dispose();
                slot.Buffer = default;
                slot.BufferCapacity = 0;
                slot.ActiveTask = null;
            }
        }
    }

    private Task<MapRenderCaptureResult> Submit(
        Slot slot,
        MapRenderCaptureRequest request,
        MTLTexture source,
        bool sourceIsBgra,
        MTLCommandBuffer producerSubmission,
        Func<long> resolveCompletionFrameIndex)
    {
        if (source.NativePtr == 0)
            throw new ArgumentException("A Metal source texture is required.", nameof(source));
        if (producerSubmission.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal producer command buffer is required.",
                nameof(producerSubmission));
        }
        if (source.SampleCount != 1 || source.Depth != 1 ||
            source.ArrayLength != 1)
        {
            throw new InvalidOperationException(
                "Metal capture requires one single-sample 2D texture slice.");
        }

        ulong width = checked((ulong)request.ExpectedExtent.Width);
        ulong height = checked((ulong)request.ExpectedExtent.Height);
        if (source.Width != width || source.Height != height)
        {
            throw new InvalidOperationException(
                $"Metal capture source {source.Width}x{source.Height} does not match requested extent {request.ExpectedExtent}.");
        }

        ulong tightRowBytes = checked(width * MapRenderCaptureImage.BytesPerPixel);
        // 256 bytes satisfies the strict macOS texture-buffer row pitch;
        // newer Apple-family devices may report a smaller pixel-format
        // alignment, which remains valid for the buffer offset itself.
        ulong rowAlignment = Math.Max(
            256,
            _device.MinimumLinearTextureAlignmentForPixelFormat(
                source.PixelFormat));
        ulong stagingRowBytes = AlignUp(tightRowBytes, rowAlignment);
        ulong byteCount = checked(stagingRowBytes * height);
        EnsureBuffer(slot, byteCount);

        MTLCommandBuffer commandBuffer = default;
        bool retainedCommandBuffer = false;
        bool retainedProducerSubmission = false;
        try
        {
            using var pool = new NSAutoreleasePool();
            // The frame ring owns the producer wrapper only until its normal
            // retirement. Keep an independent reference through asynchronous
            // completion so a successful copy can never mask a failed source
            // frame or be reported as the requested revision.
            MetalObjectiveC.Retain(producerSubmission.NativePtr);
            retainedProducerSubmission = true;
            commandBuffer = _queue.CommandBuffer();
            if (commandBuffer.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal did not provide a capture command buffer.");
            }
            MetalObjectiveC.Retain(commandBuffer.NativePtr);
            retainedCommandBuffer = true;

            MTLBlitCommandEncoder blit = commandBuffer.BlitCommandEncoder();
            if (blit.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal did not provide a capture blit encoder.");
            }
            try
            {
                blit.CopyFromTexture(
                    source,
                    sourceSlice: 0,
                    sourceLevel: 0,
                    new MTLOrigin(),
                    new MTLSize
                    {
                        width = width,
                        height = height,
                        depth = 1
                    },
                    slot.Buffer,
                    destinationOffset: 0,
                    destinationBytesPerRow: stagingRowBytes,
                    destinationBytesPerImage: byteCount);
                if (!_hasUnifiedMemory)
                {
                    blit.SynchronizeResource(
                        new MTLResource(slot.Buffer.NativePtr));
                }
            }
            finally
            {
                blit.EndEncoding();
            }
            commandBuffer.Commit();

            MTLCommandBuffer ownedCommandBuffer = commandBuffer;
            Task<MapRenderCaptureResult> completion = Task.Run(() => Complete(
                request,
                producerSubmission,
                ownedCommandBuffer,
                slot.Buffer,
                checked((int)stagingRowBytes),
                sourceIsBgra,
                resolveCompletionFrameIndex));
            retainedCommandBuffer = false;
            retainedProducerSubmission = false;
            return completion;
        }
        catch
        {
            if (retainedCommandBuffer)
                commandBuffer.Dispose();
            if (retainedProducerSubmission)
                producerSubmission.Dispose();
            throw;
        }
    }

    private static MapRenderCaptureResult Complete(
        MapRenderCaptureRequest request,
        MTLCommandBuffer producerSubmission,
        MTLCommandBuffer commandBuffer,
        MTLBuffer stagingBuffer,
        int stagingRowBytes,
        bool sourceIsBgra,
        Func<long> resolveCompletionFrameIndex)
    {
        using var pool = new NSAutoreleasePool();
        try
        {
            commandBuffer.WaitUntilCompleted();
            MTLCommandBufferStatus producerStatus =
                producerSubmission.Status;
            if (producerStatus == MTLCommandBufferStatus.Error)
            {
                string detail = producerSubmission.Error.NativePtr == 0
                    ? "unknown command-buffer error"
                    : producerSubmission.Error.LocalizedDescription.ToString() ??
                      "unknown command-buffer error";
                return MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    $"Metal source frame {request.FrameRevision} failed: {detail}");
            }
            if (producerStatus != MTLCommandBufferStatus.Completed)
            {
                return MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    $"Metal source frame {request.FrameRevision} stopped in status {producerStatus}.");
            }

            MTLCommandBufferStatus status = commandBuffer.Status;
            if (status == MTLCommandBufferStatus.Error)
            {
                string detail = commandBuffer.Error.NativePtr == 0
                    ? "unknown command-buffer error"
                    : commandBuffer.Error.LocalizedDescription.ToString() ??
                      "unknown command-buffer error";
                return MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    $"Metal RGBA8 readback failed: {detail}");
            }
            if (status != MTLCommandBufferStatus.Completed)
            {
                return MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    $"Metal RGBA8 readback stopped in status {status}.");
            }
            if (stagingBuffer.Contents == 0)
            {
                return MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.BackendReadbackFailed,
                    "Metal capture staging storage is not CPU accessible.");
            }

            int tightRowBytes = checked(
                request.ExpectedExtent.Width *
                MapRenderCaptureImage.BytesPerPixel);
            byte[] topDownRgba8 = new byte[checked(
                tightRowBytes * request.ExpectedExtent.Height)];
            for (int y = 0; y < request.ExpectedExtent.Height; y++)
            {
                nint sourceRow = checked(
                    stagingBuffer.Contents + y * stagingRowBytes);
                Marshal.Copy(
                    sourceRow,
                    topDownRgba8,
                    y * tightRowBytes,
                    tightRowBytes);
            }

            if (sourceIsBgra)
            {
                for (int offset = 0;
                     offset < topDownRgba8.Length;
                     offset += MapRenderCaptureImage.BytesPerPixel)
                {
                    (topDownRgba8[offset], topDownRgba8[offset + 2]) =
                        (topDownRgba8[offset + 2], topDownRgba8[offset]);
                }
            }

            MapRenderCaptureImage image =
                MapRenderCaptureImage.TakeOwnership(
                    request.ExpectedExtent,
                    tightRowBytes,
                    topDownRgba8);
            long completionFrameIndex = Math.Max(
                request.FrameRevision,
                resolveCompletionFrameIndex());
            return MapRenderCaptureResult.Completed(
                request,
                image,
                new MapRenderCaptureTelemetry(
                    request.FrameRevision,
                    completionFrameIndex,
                    image.ByteCount));
        }
        catch (Exception error)
        {
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.BackendReadbackFailed,
                $"Metal RGBA8 readback failed: {error.Message}");
        }
        finally
        {
            // Balances the explicit retain at submission.
            commandBuffer.Dispose();
            producerSubmission.Dispose();
        }
    }

    private void EnsureBuffer(Slot slot, ulong requiredByteCount)
    {
        if (slot.Buffer.NativePtr != 0 &&
            slot.BufferCapacity >= requiredByteCount)
        {
            return;
        }

        MTLBuffer replacement = _device.NewBuffer(
            requiredByteCount,
            _hasUnifiedMemory
                ? MTLResourceOptions.ResourceStorageModeShared
                : MTLResourceOptions.ResourceStorageModeManaged);
        if (replacement.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal could not allocate {requiredByteCount} capture staging bytes.");
        }

        MTLBuffer previous = slot.Buffer;
        slot.Buffer = replacement;
        slot.BufferCapacity = requiredByteCount;
        if (previous.NativePtr != 0)
            previous.Dispose();
    }

    private void ReleaseCompletedSlot(
        Slot slot,
        Task<MapRenderCaptureResult> completed)
    {
        lock (_gate)
        {
            if (ReferenceEquals(slot.ActiveTask, completed))
                slot.ActiveTask = null;
        }
    }

    private static ulong AlignUp(ulong value, ulong alignment)
    {
        ulong remainder = value % alignment;
        return remainder == 0
            ? value
            : checked(value + alignment - remainder);
    }

    private sealed class Slot
    {
        internal MTLBuffer Buffer;
        internal ulong BufferCapacity;
        internal Task<MapRenderCaptureResult>? ActiveTask;
    }
}
