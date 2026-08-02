using Silk.NET.OpenGL;
using IW4.Render;
using IW4.Render.OpenGl.Presentation;

using IW4.Render.Capture;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer :
    IMapRenderCaptureProvider
{
    public ValueTask<MapRenderCaptureResult> CaptureAsync(
        MapRenderCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<MapRenderCaptureResult>(Capture(request));
    }

    private MapRenderCaptureResult Capture(
        MapRenderCaptureRequest request)
    {
        if (!request.IsValid)
            throw new ArgumentException(
                "Capture request is not valid.",
                nameof(request));
        if (!_loaded)
        {
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.RendererNotLoaded,
                "OpenGL capture requires loaded renderer resources.");
        }
        if (_frameTelemetry.IsCpuFrameActive)
        {
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.FrameInProgress,
                "OpenGL capture cannot interrupt an active render frame.");
        }

        MapRenderOpenGlNormalCameraDefaultPresentationExecutionResult?
            presentation = LastEditorPreviewPresentationResult;
        if (presentation is null)
        {
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.FrameUnavailable,
                "OpenGL capture requires a completed Live Preview presentation frame.");
        }

        long availableFrameRevision = presentation.Plan.FrameRevision;
        if (request.FrameRevision != availableFrameRevision)
        {
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.FrameRevisionMismatch,
                $"Requested frame revision {request.FrameRevision} does not match the available OpenGL frame revision {availableFrameRevision}.");
        }

        uint readFramebuffer;
        ReadBufferMode readBuffer;
        MapRenderPixelExtent captureExtent;
        switch (request.Source)
        {
            case MapRenderScreenshotSource.ResolvedScene:
                readFramebuffer = presentation.Plan.ResolvedSceneColor
                    .Resource.FramebufferHandle;
                readBuffer = ReadBufferMode.ColorAttachment0;
                captureExtent = presentation.SceneTargetExtent;
                break;
            case MapRenderScreenshotSource.HostBackBuffer:
                readFramebuffer = _hostFramebuffer;
                readBuffer = _hostFramebuffer == 0
                    ? ReadBufferMode.Back
                    : ReadBufferMode.ColorAttachment0;
                captureExtent = presentation.HostFramebufferExtent;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Source,
                    null);
        }

        if (request.ExpectedExtent != captureExtent)
        {
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.ExtentMismatch,
                $"Requested capture extent {request.ExpectedExtent} does not match the available {request.Source} extent {captureExtent}.");
        }

        int rowStrideBytes = checked(
            captureExtent.Width * MapRenderCaptureImage.BytesPerPixel);
        byte[] bottomUp = new byte[checked(
            rowStrideBytes * captureExtent.Height)];
        try
        {
            try
            {
                _state.BindFramebuffer(
                    FramebufferTarget.ReadFramebuffer,
                    readFramebuffer);
                _gl.ReadBuffer(readBuffer);
                _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
                fixed (byte* ptr = bottomUp)
                {
                    _gl.ReadPixels(
                        0,
                        0,
                        checked((uint)captureExtent.Width),
                        checked((uint)captureExtent.Height),
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        ptr);
                }
            }
            finally
            {
                _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
                _state.BindFramebuffer(
                    FramebufferTarget.ReadFramebuffer,
                    _hostFramebuffer);
                _gl.ReadBuffer(_hostFramebuffer == 0
                    ? ReadBufferMode.Back
                    : ReadBufferMode.ColorAttachment0);
            }
        }
        catch (Exception error)
        {
            _state.InvalidateAll();
            return MapRenderCaptureResult.Failed(
                request,
                MapRenderCaptureStatus.BackendReadbackFailed,
                $"OpenGL RGBA8 readback failed: {error.Message}");
        }

        byte[] topDown = new byte[bottomUp.Length];
        for (var y = 0; y < captureExtent.Height; y++)
        {
            System.Buffer.BlockCopy(
                bottomUp,
                y * rowStrideBytes,
                topDown,
                (captureExtent.Height - 1 - y) * rowStrideBytes,
                rowStrideBytes);
        }

        MapRenderCaptureImage image = MapRenderCaptureImage.TakeOwnership(
            captureExtent,
            rowStrideBytes,
            topDown);
        var telemetry = new MapRenderCaptureTelemetry(
            submissionFrameIndex: availableFrameRevision,
            completionFrameIndex: availableFrameRevision,
            transferredByteCount: image.ByteCount);
        return MapRenderCaptureResult.Completed(
            request,
            image,
            telemetry);
    }
}
