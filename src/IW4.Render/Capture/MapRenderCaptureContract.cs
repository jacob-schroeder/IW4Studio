namespace IW4.Render.Capture;

/// <summary>
/// Optional renderer capability for capturing one exact completed frame.
/// The returned value task permits deferred GPU readback without exposing
/// backend command or file-I/O concerns through the shared interface.
/// </summary>
public interface IMapRenderCaptureProvider
{
    ValueTask<MapRenderCaptureResult> CaptureAsync(
        MapRenderCaptureRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies the source, presentation-frame revision, and exact canonical
/// extent of one requested capture.
/// </summary>
public readonly record struct MapRenderCaptureRequest
{
    public MapRenderCaptureRequest(
        MapRenderScreenshotSource source,
        long frameRevision,
        MapRenderPixelExtent expectedExtent)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (expectedExtent.Width <= 0 || expectedExtent.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedExtent));

        Source = source;
        FrameRevision = frameRevision;
        ExpectedExtent = expectedExtent;
    }

    public MapRenderScreenshotSource Source { get; }

    public long FrameRevision { get; }

    public MapRenderPixelExtent ExpectedExtent { get; }

    public bool IsValid =>
        Enum.IsDefined(Source) &&
        FrameRevision >= 0 &&
        ExpectedExtent.Width > 0 &&
        ExpectedExtent.Height > 0;

    public static MapRenderCaptureRequest ForFrame(
        MapRenderScreenshotSource source,
        long frameRevision,
        MapRenderSurfaceExtents surfaceExtents)
    {
        if (!surfaceExtents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(surfaceExtents));

        MapRenderPixelExtent extent = source switch
        {
            MapRenderScreenshotSource.ResolvedScene =>
                surfaceExtents.SceneTarget,
            MapRenderScreenshotSource.HostBackBuffer =>
                surfaceExtents.HostFramebuffer,
            _ => throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                null),
        };
        return new MapRenderCaptureRequest(source, frameRevision, extent);
    }
}

/// <summary>
/// Canonical capture bytes. Rows are tightly packed in top-down order and
/// each pixel is four bytes in R, G, B, A order. Construction takes an owned
/// copy of caller bytes.
/// </summary>
public sealed class MapRenderCaptureImage
{
    public const string PixelFormat = "RGBA8";
    public const string RowOrder = "top-down";
    public const int BytesPerPixel = 4;

    private readonly byte[] _topDownRgba8;

    public MapRenderCaptureImage(
        MapRenderPixelExtent extent,
        int rowStrideBytes,
        ReadOnlySpan<byte> topDownRgba8)
        : this(
            extent,
            rowStrideBytes,
            topDownRgba8.ToArray(),
            takeOwnership: true)
    {
    }

    private MapRenderCaptureImage(
        MapRenderPixelExtent extent,
        int rowStrideBytes,
        byte[] topDownRgba8,
        bool takeOwnership)
    {
        if (extent.Width <= 0 || extent.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(extent));

        int canonicalRowStride = checked(extent.Width * BytesPerPixel);
        if (rowStrideBytes != canonicalRowStride)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowStrideBytes),
                "A canonical RGBA8 capture must have one exact tightly packed row stride.");
        }

        ArgumentNullException.ThrowIfNull(topDownRgba8);
        int expectedByteCount = checked(rowStrideBytes * extent.Height);
        if (topDownRgba8.Length != expectedByteCount)
        {
            throw new ArgumentException(
                "Top-down RGBA8 byte count does not match the exact extent and row stride.",
                nameof(topDownRgba8));
        }

        Extent = extent;
        RowStrideBytes = rowStrideBytes;
        _topDownRgba8 = takeOwnership
            ? topDownRgba8
            : topDownRgba8.ToArray();
    }

    public MapRenderPixelExtent Extent { get; }

    public int Width => Extent.Width;

    public int Height => Extent.Height;

    public int RowStrideBytes { get; }

    public int ByteCount => _topDownRgba8.Length;

    internal byte[] SharedTopDownRgba8 => _topDownRgba8;

    internal static MapRenderCaptureImage TakeOwnership(
        MapRenderPixelExtent extent,
        int rowStrideBytes,
        byte[] topDownRgba8) =>
        new(
            extent,
            rowStrideBytes,
            topDownRgba8,
            takeOwnership: true);
}

/// <summary>
/// Submission/completion frame indices for capture readback. A deferred
/// backend records the later frame that made mapped bytes host-readable.
/// </summary>
public readonly record struct MapRenderCaptureTelemetry
{
    public MapRenderCaptureTelemetry(
        long submissionFrameIndex,
        long completionFrameIndex,
        long transferredByteCount)
    {
        if (submissionFrameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(submissionFrameIndex));
        }
        if (completionFrameIndex < submissionFrameIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completionFrameIndex));
        }
        if (transferredByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transferredByteCount));
        }

        SubmissionFrameIndex = submissionFrameIndex;
        CompletionFrameIndex = completionFrameIndex;
        TransferredByteCount = transferredByteCount;
    }

    public long SubmissionFrameIndex { get; }

    public long CompletionFrameIndex { get; }

    public long CompletionDelayFrames => checked(
        CompletionFrameIndex - SubmissionFrameIndex);

    public long TransferredByteCount { get; }

    public bool CompletedSynchronously => CompletionDelayFrames == 0;
}

public enum MapRenderCaptureStatus
{
    Completed,
    RendererNotLoaded,
    FrameInProgress,
    FrameUnavailable,
    FrameRevisionMismatch,
    ExtentMismatch,
    BackendReadbackFailed
}

/// <summary>
/// Immutable capture outcome. Completed outcomes contain one owned image and
/// exact completion telemetry; typed failures contain neither image bytes nor
/// fabricated completion telemetry.
/// </summary>
public sealed class MapRenderCaptureResult
{
    private MapRenderCaptureResult(
        MapRenderCaptureRequest request,
        MapRenderCaptureStatus status,
        MapRenderCaptureImage? image,
        MapRenderCaptureTelemetry? telemetry,
        string? failureDetail)
    {
        if (!request.IsValid)
            throw new ArgumentException(
                "Capture request is not valid.",
                nameof(request));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        bool completed = status == MapRenderCaptureStatus.Completed;
        if (completed)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Extent != request.ExpectedExtent)
            {
                throw new ArgumentException(
                    "Completed capture extent does not match the exact request extent.",
                    nameof(image));
            }
            if (telemetry is not { } completedTelemetry ||
                completedTelemetry.TransferredByteCount != image.ByteCount)
            {
                throw new ArgumentException(
                    "Completed capture telemetry must report the exact owned RGBA8 byte count.",
                    nameof(telemetry));
            }
            if (completedTelemetry.SubmissionFrameIndex !=
                request.FrameRevision)
            {
                throw new ArgumentException(
                    "Capture submission telemetry must identify the exact requested frame revision.",
                    nameof(telemetry));
            }
            if (failureDetail is not null)
            {
                throw new ArgumentException(
                    "A completed capture cannot contain failure detail.",
                    nameof(failureDetail));
            }
        }
        else
        {
            if (image is not null || telemetry is not null)
            {
                throw new ArgumentException(
                    "A failed capture cannot publish image or completion telemetry.");
            }
            if (string.IsNullOrWhiteSpace(failureDetail))
            {
                throw new ArgumentException(
                    "A failed capture requires non-empty failure detail.",
                    nameof(failureDetail));
            }
        }

        Request = request;
        Status = status;
        Image = image;
        Telemetry = telemetry;
        FailureDetail = failureDetail;
    }

    public MapRenderCaptureRequest Request { get; }

    public MapRenderCaptureStatus Status { get; }

    public bool IsCompleted => Status == MapRenderCaptureStatus.Completed;

    public MapRenderCaptureImage? Image { get; }

    public MapRenderCaptureTelemetry? Telemetry { get; }

    public string? FailureDetail { get; }

    public static MapRenderCaptureResult Completed(
        MapRenderCaptureRequest request,
        MapRenderCaptureImage image,
        MapRenderCaptureTelemetry telemetry) =>
        new(
            request,
            MapRenderCaptureStatus.Completed,
            image,
            telemetry,
            failureDetail: null);

    public static MapRenderCaptureResult Failed(
        MapRenderCaptureRequest request,
        MapRenderCaptureStatus status,
        string failureDetail)
    {
        if (status == MapRenderCaptureStatus.Completed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "A failed capture cannot use Completed status.");
        }

        return new MapRenderCaptureResult(
            request,
            status,
            image: null,
            telemetry: null,
            failureDetail);
    }
}
