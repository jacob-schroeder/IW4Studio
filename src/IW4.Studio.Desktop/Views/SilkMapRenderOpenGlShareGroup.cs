using IW4.Render.OpenGl.Programs;
using IW4.Studio.Desktop.Persistence;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Keeps one hidden OpenGL context alive for the Studio process so linked
/// programs remain valid across sequential native map-render windows.
/// </summary>
internal sealed class SilkMapRenderOpenGlShareGroup : IDisposable
{
    private static SilkMapRenderOpenGlShareGroup? s_instance;

    private readonly IWindow _rootWindow;
    private readonly IGLContext _rootContext;
    private readonly int _ownerThreadId;
    private int _activeLeaseCount;
    private bool _shutdownRequested;
    private bool _disposed;

    private SilkMapRenderOpenGlShareGroup()
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
        WindowOptions options = WindowOptions.Default;
        options.Title = "IW4 Studio OpenGL Share Group";
        options.Size = new Vector2D<int>(1, 1);
        options.IsVisible = false;
        options.ShouldSwapAutomatically = false;
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3));
        options.PreferredDepthBufferBits = 24;
        options.PreferredStencilBufferBits = 8;
        options.VSync = false;

        IWindow rootWindow = Window.Create(options);
        try
        {
            rootWindow.Initialize();
            _rootWindow = rootWindow;
            _rootContext = rootWindow.GLContext ??
                throw new InvalidOperationException(
                    "Silk.NET did not create the OpenGL share-root context.");
            ProgramCache = new OpenGlSharedProgramCache(
                GL.GetApi(rootWindow),
                programBinaryCacheDirectory:
                    OpenGlProgramBinaryCachePath.GetDirectory());
        }
        catch
        {
            rootWindow.Reset();
            throw;
        }
    }

    internal OpenGlSharedProgramCache ProgramCache { get; }

    internal static Lease Acquire()
    {
        SilkMapRenderOpenGlShareGroup group =
            s_instance ??= new SilkMapRenderOpenGlShareGroup();
        return group.AcquireCore();
    }

    internal static void Shutdown()
    {
        SilkMapRenderOpenGlShareGroup? group = s_instance;
        if (group is null)
            return;

        try
        {
            group.RequestShutdown();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"OpenGL share-group shutdown failed: {exception}");
        }
    }

    private Lease AcquireCore()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shutdownRequested)
        {
            throw new InvalidOperationException(
                "The OpenGL share group is already shutting down.");
        }

        _activeLeaseCount = checked(_activeLeaseCount + 1);
        return new Lease(this, _rootContext, ProgramCache);
    }

    private void RequestShutdown()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;

        _shutdownRequested = true;
        if (_activeLeaseCount == 0)
            Dispose();
    }

    private void Release()
    {
        EnsureOwnerThread();
        if (_activeLeaseCount <= 0)
        {
            throw new InvalidOperationException(
                "The OpenGL share-group lease count underflowed.");
        }

        _activeLeaseCount--;
        if (_shutdownRequested && _activeLeaseCount == 0)
            Dispose();
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return;
        if (_activeLeaseCount != 0)
        {
            throw new InvalidOperationException(
                "The OpenGL share group cannot be disposed while map-render windows still use it.");
        }

        _disposed = true;
        Console.WriteLine(
            $"OpenGL shared program cache: " +
            $"requests={ProgramCache.LinkRequestCount}, " +
            $"uniqueLinkAttempts={ProgramCache.UniqueLinkAttemptCount}, " +
            $"linked={ProgramCache.SuccessfulLinkCount}, " +
            $"binaryEnabled={ProgramCache.ProgramBinaryPersistenceEnabled}, " +
            $"binaryAttempts={ProgramCache.ProgramBinaryLoadAttemptCount}, " +
            $"binaryHits={ProgramCache.ProgramBinaryLoadHitCount}, " +
            $"binaryStores={ProgramCache.ProgramBinaryStoreCount}, " +
            $"reuses={ProgramCache.LinkReuseCount}, " +
            $"failed={ProgramCache.FailedLinkCount}, " +
            $"capacityBypass={ProgramCache.CapacityBypassCount}, " +
            $"cached={ProgramCache.CachedProgramCount}/" +
            $"{ProgramCache.MaximumEntryCount}.");
        try
        {
            if (!_rootContext.IsCurrent)
                _rootContext.MakeCurrent();
            ProgramCache.Dispose();
        }
        finally
        {
            _rootWindow.Reset();
            if (ReferenceEquals(s_instance, this))
                s_instance = null;
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "The OpenGL share group may only be used on its owning UI/render thread.");
        }
    }

    internal sealed class Lease : IDisposable
    {
        private SilkMapRenderOpenGlShareGroup? _owner;

        internal Lease(
            SilkMapRenderOpenGlShareGroup owner,
            IGLContext sharedContext,
            OpenGlSharedProgramCache programCache)
        {
            _owner = owner;
            SharedContext = sharedContext;
            ProgramCache = programCache;
        }

        internal IGLContext SharedContext { get; }

        internal OpenGlSharedProgramCache ProgramCache { get; }

        public void Dispose()
        {
            SilkMapRenderOpenGlShareGroup? owner =
                Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
