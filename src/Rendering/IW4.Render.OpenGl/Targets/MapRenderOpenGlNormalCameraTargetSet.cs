using IW4.Render.Scheduling.Lifecycle;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Complete context-local normal-camera color and depth/stencil resources for
/// one exact display extent. The depth cache borrows its color frame, so this
/// owner always disposes depth/combined FBOs before color objects.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraTargetSet : IDisposable
{
    private readonly int _ownerThreadId;
    private bool _disposed;

    private MapRenderOpenGlNormalCameraTargetSet(
        MapRenderOpenGlNormalCameraColorTargetResourceCache colorCache,
        MapRenderOpenGlNormalCameraColorTargetResourceFrame colorFrame,
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache depthCache,
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame depthFrame)
    {
        ArgumentNullException.ThrowIfNull(colorCache);
        ArgumentNullException.ThrowIfNull(colorFrame);
        ArgumentNullException.ThrowIfNull(depthCache);
        ArgumentNullException.ThrowIfNull(depthFrame);
        if (!ReferenceEquals(depthCache.ColorFrame, colorFrame) ||
            !ReferenceEquals(depthFrame.ColorFrame, colorFrame) ||
            !string.Equals(colorFrame.ContextIdentity,
                depthFrame.ContextIdentity, StringComparison.Ordinal) ||
            colorFrame.Plan.DisplayWidth != depthFrame.Plan.DisplayWidth ||
            colorFrame.Plan.DisplayHeight != depthFrame.Plan.DisplayHeight)
        {
            throw new ArgumentException(
                "Normal-camera target resources do not share one context, extent, and borrowed color frame.");
        }

        ColorCache = colorCache;
        ColorFrame = colorFrame;
        DepthCache = depthCache;
        DepthFrame = depthFrame;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string ContextIdentity
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return ColorFrame.ContextIdentity;
        }
    }

    public int DisplayWidth
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return ColorFrame.Plan.DisplayWidth;
        }
    }

    public int DisplayHeight
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return ColorFrame.Plan.DisplayHeight;
        }
    }

    public MapRenderOpenGlNormalCameraColorTargetResourceCache ColorCache
        { get; }

    public MapRenderOpenGlNormalCameraColorTargetResourceFrame ColorFrame
        { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache
        DepthCache { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame
        DepthFrame { get; }

    public static MapRenderOpenGlNormalCameraTargetSet Create(
        IMapRenderOpenGlNormalCameraColorTargetResourceAllocator colorAllocator,
        IMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator
            depthAllocator,
        int displayWidth,
        int displayHeight)
    {
        ArgumentNullException.ThrowIfNull(colorAllocator);
        ArgumentNullException.ThrowIfNull(depthAllocator);
        if (!string.Equals(colorAllocator.ContextIdentity,
                depthAllocator.ContextIdentity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Normal-camera target allocators belong to different OpenGL contexts.");
        }

        MapRenderOpenGlNormalCameraColorTargetResourceCache? colorCache = null;
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache? depthCache =
            null;
        try
        {
            MapRenderOpenGlNormalCameraColorFramePlan colorPlan =
                MapRenderOpenGlNormalCameraColorFramePlanner.CreatePs3(
                    displayWidth,
                    displayHeight);
            colorCache =
                new MapRenderOpenGlNormalCameraColorTargetResourceCache(
                    colorAllocator);
            MapRenderOpenGlNormalCameraColorTargetPrewarmResult colorResult =
                MapRenderOpenGlNormalCameraColorTargetPrewarmer.TryPrewarm(
                    colorCache,
                    colorPlan);
            MapRenderOpenGlNormalCameraColorTargetResourceFrame colorFrame =
                colorResult.Frame ?? throw new InvalidOperationException(
                    "Normal-camera color target prewarm failed: " +
                    string.Join(';', colorResult.Failures.Select(failure =>
                        $"{failure.Kind}:{failure.CanonicalTarget}:{failure.Detail}")));

            MapRenderOpenGlNormalCameraDepthStencilFramePlan depthPlan =
                MapRenderOpenGlNormalCameraDepthStencilFramePlanner.CreatePs3(
                    colorPlan);
            depthCache =
                new MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache(
                    depthAllocator,
                    colorFrame);
            MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult
                depthResult =
                    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmer
                        .TryPrewarm(depthCache, depthPlan);
            MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame
                depthFrame = depthResult.Frame ??
                    throw new InvalidOperationException(
                        "Normal-camera depth/stencil target prewarm failed: " +
                        string.Join(';', depthResult.Failures.Select(failure =>
                            $"{failure.Kind}:{failure.Target}:{failure.Detail}")));

            return new MapRenderOpenGlNormalCameraTargetSet(
                colorCache,
                colorFrame,
                depthCache,
                depthFrame);
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            TryDispose(depthCache, failures);
            TryDispose(colorCache, failures);
            throw new AggregateException(
                "Normal-camera target-set creation failed; every partial owner was disposed when possible.",
                failures);
        }
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;

        _disposed = true;
        var failures = new List<Exception>();
        TryDispose(DepthCache, failures);
        TryDispose(ColorCache, failures);
        if (failures.Count != 0)
        {
            throw new AggregateException(
                "Normal-camera target-set disposal failed.",
                failures);
        }
    }

    private static void TryDispose(
        IDisposable? owner,
        ICollection<Exception> failures)
    {
        if (owner is null)
            return;
        try
        {
            owner.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private void EnsureUsableOnOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Normal-camera target resources may only be used and disposed on their owning render thread.");
        }
    }
}
