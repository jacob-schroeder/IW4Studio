using System.Runtime.Versioning;

using IW4.Render.Capture;
using IW4.Render.Metal.Resources;

using SharpMetal.Metal;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalMapRenderer : IMapRenderCaptureProvider
{
    private readonly object _captureGate = new();
    private readonly MetalCaptureReadbackRing _captureReadbacks;
    private MTLTexture _captureHostOutput;
    private MTLCommandBuffer _captureProducerSubmission;
    private MapRenderSurfaceExtents _captureSurfaceExtents;
    private long _captureFrameRevision = -1;

    public ValueTask<MapRenderCaptureResult> CaptureAsync(
        MapRenderCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.IsValid)
        {
            throw new ArgumentException(
                "Capture request is not valid.",
                nameof(request));
        }

        Task<MapRenderCaptureResult> readback;
        lock (_captureGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_loaded)
            {
                return ValueTask.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.RendererNotLoaded,
                    "Metal capture requires loaded renderer resources."));
            }
            if (_telemetry.IsCpuFrameActive)
            {
                return ValueTask.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.FrameInProgress,
                    "Metal capture cannot interrupt an active render frame."));
            }
            if (_captureFrameRevision < 0)
            {
                return ValueTask.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.FrameUnavailable,
                    "Metal capture requires a completed Live Preview presentation frame."));
            }
            if (request.FrameRevision != _captureFrameRevision)
            {
                return ValueTask.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.FrameRevisionMismatch,
                    $"Requested frame revision {request.FrameRevision} does not match the available Metal frame revision {_captureFrameRevision}."));
            }

            MapRenderPixelExtent availableExtent = request.Source switch
            {
                MapRenderScreenshotSource.ResolvedScene =>
                    _captureSurfaceExtents.SceneTarget,
                MapRenderScreenshotSource.HostBackBuffer =>
                    _captureSurfaceExtents.HostFramebuffer,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Source,
                    null),
            };
            if (request.ExpectedExtent != availableExtent)
            {
                return ValueTask.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.ExtentMismatch,
                    $"Requested capture extent {request.ExpectedExtent} does not match the available {request.Source} extent {availableExtent}."));
            }
            if (_captureProducerSubmission.NativePtr == 0)
            {
                return ValueTask.FromResult(MapRenderCaptureResult.Failed(
                    request,
                    MapRenderCaptureStatus.FrameUnavailable,
                    "The Metal submission for the requested frame is unavailable."));
            }

            MTLTexture source;
            bool sourceIsBgra;
            switch (request.Source)
            {
                case MapRenderScreenshotSource.ResolvedScene:
                    source = _targets.ResolvedColor;
                    sourceIsBgra = false;
                    if (source.PixelFormat != MTLPixelFormat.RGBA8Unorm)
                    {
                        return ValueTask.FromResult(
                            MapRenderCaptureResult.Failed(
                                request,
                                MapRenderCaptureStatus.BackendReadbackFailed,
                                $"Metal resolved-scene capture requires RGBA8Unorm, but the current target is {source.PixelFormat}."));
                    }
                    break;
                case MapRenderScreenshotSource.HostBackBuffer:
                    source = _captureHostOutput;
                    sourceIsBgra = true;
                    if (source.NativePtr == 0)
                    {
                        return ValueTask.FromResult(
                            MapRenderCaptureResult.Failed(
                                request,
                                MapRenderCaptureStatus.FrameUnavailable,
                                "The exact Metal host output for the requested frame is unavailable."));
                    }
                    if (source.PixelFormat != MTLPixelFormat.BGRA8Unorm)
                    {
                        return ValueTask.FromResult(
                            MapRenderCaptureResult.Failed(
                                request,
                                MapRenderCaptureStatus.BackendReadbackFailed,
                                $"Metal host capture requires BGRA8Unorm, but the current output is {source.PixelFormat}."));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        request.Source,
                        null);
            }

            // Submission occurs while the revision gate is held. A next-frame
            // invalidation therefore happens either before this request (and
            // rejects it) or after this same-queue copy is committed, ahead of
            // every write to the retained host/scene targets for that frame.
            readback = _captureReadbacks.Enqueue(
                request,
                source,
                sourceIsBgra,
                _captureProducerSubmission,
                ResolveCaptureCompletionFrameIndex);
        }

        return WrapReadback(readback, cancellationToken);
    }

    private static ValueTask<MapRenderCaptureResult> WrapReadback(
        Task<MapRenderCaptureResult> readback,
        CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? new ValueTask<MapRenderCaptureResult>(
                AwaitReadbackAsync(readback, cancellationToken))
            : new ValueTask<MapRenderCaptureResult>(readback);

    private static async Task<MapRenderCaptureResult> AwaitReadbackAsync(
        Task<MapRenderCaptureResult> readback,
        CancellationToken cancellationToken) =>
        await readback.WaitAsync(cancellationToken).ConfigureAwait(false);

    private long ResolveCaptureCompletionFrameIndex() =>
        Math.Max(0, Interlocked.Read(ref _lastCompletedCpuFrameIndex));

    private MTLTexture CaptureHostOutput =>
        _captureHostOutput.NativePtr != 0
            ? _captureHostOutput
            : throw new InvalidOperationException(
                "The Metal host presentation target is unavailable.");

    private void ResizeCaptureHostOutput(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (_captureHostOutput.NativePtr != 0 &&
            _captureHostOutput.Width == checked((ulong)width) &&
            _captureHostOutput.Height == checked((ulong)height))
        {
            return;
        }

        using var descriptor = new MTLTextureDescriptor
        {
            TextureType = MTLTextureType.Type2D,
            PixelFormat = MTLPixelFormat.BGRA8Unorm,
            Width = checked((ulong)width),
            Height = checked((ulong)height),
            Depth = 1,
            ArrayLength = 1,
            MipmapLevelCount = 1,
            SampleCount = 1,
            StorageMode = MTLStorageMode.Private,
            Usage = MTLTextureUsage.RenderTarget
        };
        MTLTexture replacement = _surface.Device.NewTexture(descriptor);
        if (replacement.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal could not allocate the {width}x{height} retained host presentation target.");
        }

        MTLTexture previous;
        lock (_captureGate)
        {
            previous = _captureHostOutput;
            _captureHostOutput = replacement;
        }
        if (previous.NativePtr != 0)
            previous.Dispose();
    }

    private void EncodeCaptureHostHandoff(
        MTLCommandBuffer commandBuffer,
        MTLTexture drawableTexture)
    {
        if (commandBuffer.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal command buffer is required.",
                nameof(commandBuffer));
        }
        if (drawableTexture.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal drawable texture is required.",
                nameof(drawableTexture));
        }

        MTLTexture source = CaptureHostOutput;
        if (source.PixelFormat != MTLPixelFormat.BGRA8Unorm ||
            drawableTexture.PixelFormat != source.PixelFormat ||
            source.Width != drawableTexture.Width ||
            source.Height != drawableTexture.Height)
        {
            throw new InvalidOperationException(
                "The retained Metal host target must exactly match the BGRA8 drawable.");
        }

        MTLBlitCommandEncoder blit = commandBuffer.BlitCommandEncoder();
        if (blit.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal could not begin the host presentation copy.");
        }
        try
        {
            var origin = new MTLOrigin();
            blit.CopyFromTexture(
                source,
                sourceSlice: 0,
                sourceLevel: 0,
                origin,
                new MTLSize
                {
                    width = source.Width,
                    height = source.Height,
                    depth = 1
                },
                drawableTexture,
                destinationSlice: 0,
                destinationLevel: 0,
                origin);
        }
        finally
        {
            blit.EndEncoding();
        }
    }

    private void InvalidateCaptureFrame()
    {
        lock (_captureGate)
        {
            _captureSurfaceExtents = default;
            _captureProducerSubmission = default;
            _captureFrameRevision = -1;
        }
    }

    private void PublishCaptureFrame(
        long frameRevision,
        MTLCommandBuffer producerSubmission)
    {
        // This runs after command-buffer commit. Keep publication limited to
        // non-owning value assignments so it cannot fail after GPU ownership
        // has transferred or expose a revision without a submitted frame.
        lock (_captureGate)
        {
            _captureSurfaceExtents = _surfaceExtents;
            _captureProducerSubmission = producerSubmission;
            _captureFrameRevision = frameRevision;
        }
    }

    private void WaitForCaptureReadbacks() =>
        _captureReadbacks.WaitForIdle();

    private void DisposeCaptureResources()
    {
        InvalidateCaptureFrame();
        _captureReadbacks.Dispose();
        if (_captureHostOutput.NativePtr != 0)
        {
            _captureHostOutput.Dispose();
            _captureHostOutput = default;
        }
    }
}
