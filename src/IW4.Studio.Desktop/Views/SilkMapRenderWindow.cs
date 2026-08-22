using System.Diagnostics;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.Diagnostics;
using IW4.Render.OpenGl;
using IW4.Render.Resources;
using IW4.Studio.Desktop.Rendering;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Native Silk.NET host for a full-map OpenGL render. It deliberately owns
/// its own framebuffer and context; the map renderer's presentation path
/// targets a native Silk back buffer rather than an Avalonia composition FBO.
/// </summary>
internal sealed class SilkMapRenderWindow : INativeMapRenderWindow
{
    private static readonly long TelemetryRefreshTicks =
        Math.Max(1, Stopwatch.Frequency);
    private const double FrameTimingAverageWeight = 1.0 / 32.0;

    private readonly MapRenderScene _scene;
    private readonly RenderSceneSnapshot _sceneSnapshot;
    private readonly NativeMapRenderInteraction _interaction;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render)
    {
        // A zero dispatcher interval immediately requeues the next native
        // pump after the current callback yields. A fixed delay is paid on
        // top of every frame and becomes an artificial FPS ceiling once the
        // renderer itself is below the display interval.
        Interval = TimeSpan.Zero
    };
    private IWindow? _window;
    private SilkMapRenderOpenGlShareGroup.Lease? _shareGroupLease;
    private SilkOpenGlMapRenderer? _renderer;
    private SilkMapRenderFpsOverlay? _fpsOverlay;
    private MapRenderFrameTelemetrySnapshot? _telemetrySnapshot;
    private long _nextTelemetryRefreshTimestamp;
    private double _hostRenderMilliseconds;
    private double _hostRenderAverageMilliseconds;
    private bool _hasHostRenderSample;
    private double _swapMilliseconds;
    private double _swapAverageMilliseconds;
    private bool _hasSwapSample;
    private int _startupRenderedFrameCount;
    private int _startupSettledFrameCount;
    private bool _startupWorkingSetReclaimed;
    private bool _disposed;
    private bool _closeRequested;

    public SilkMapRenderWindow(
        MapRenderScene scene,
        RenderSceneSnapshot sceneSnapshot,
        string? mapEntityString = null,
        Func<string, Task>? copyTextAsync = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _sceneSnapshot = sceneSnapshot ??
            throw new ArgumentNullException(nameof(sceneSnapshot));
        _interaction = new NativeMapRenderInteraction(
            scene,
            NativeMapRenderCamera.CreateInitial(
                scene.CameraBounds,
                mapEntityString),
            copyTextAsync);
        _timer.Tick += Timer_Tick;
    }

    public event EventHandler<Exception>? Failed;

    public event EventHandler? Stopped;

    public void Show()
    {
        ThrowIfDisposed();
        if (_window is not null)
            return;

        LivePreviewDebugDump.Write(
            $"OpenGL Live Preview host Show started; scene={_scene.Name}");

        WindowOptions options = WindowOptions.Default;
        options.Title = $"Live Preview - {_scene.Name} - IW4 Studio";
        options.Size = new Vector2D<int>(1280, 720);
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3));
        options.PreferredDepthBufferBits = 24;
        options.PreferredStencilBufferBits = 8;
        // DoRender is pumped by Avalonia's UI dispatcher. A blocking
        // swap-interval wait here stalls the whole Studio UI and quantizes a
        // frame that narrowly misses 60 Hz down to 30 Hz. Keep the native
        // editor viewport unsynchronized; its telemetry and interaction then
        // reflect actual renderer throughput instead of double-buffer pacing.
        options.VSync = false;
        options.ShouldSwapAutomatically = false;

        LivePreviewDebugDump.Write(
            "OpenGL share-group acquisition started");
        _shareGroupLease = SilkMapRenderOpenGlShareGroup.Acquire();
        LivePreviewDebugDump.Write(
            "OpenGL share-group acquisition completed");
        options.SharedContext = _shareGroupLease.SharedContext;
        IWindow window;
        try
        {
            LivePreviewDebugDump.Write(
                "OpenGL native window creation started");
            window = Window.Create(options);
            LivePreviewDebugDump.Write(
                "OpenGL native window creation completed");
        }
        catch (Exception exception)
        {
            LivePreviewDebugDump.Write(
                $"OpenGL native window creation failed: {exception}");
            _shareGroupLease.Dispose();
            _shareGroupLease = null;
            throw;
        }
        window.Load += Window_Load;
        window.Resize += Window_Resize;
        window.FramebufferResize += Window_FramebufferResize;
        window.Update += Window_Update;
        window.Render += Window_Render;
        window.Closing += Window_Closing;
        _window = window;
        try
        {
            LivePreviewDebugDump.Write(
                "OpenGL native window initialization started");
            window.Initialize();
            LivePreviewDebugDump.Write(
                "OpenGL native window initialization completed");
            _timer.Start();
            LivePreviewDebugDump.Write(
                "OpenGL render timer started");
        }
        catch (Exception exception)
        {
            LivePreviewDebugDump.Write(
                $"OpenGL native window initialization failed: {exception}");
            Dispose();
            throw;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        IWindow? window = _window;
        if (window is null || _disposed)
            return;

        try
        {
            window.DoEvents();
            if (ShouldStop(window))
            {
                Dispose();
                return;
            }

            window.DoUpdate();
            if (ShouldStop(window))
            {
                Dispose();
                return;
            }

            bool firstFrame = _startupRenderedFrameCount == 0;
            if (firstFrame)
            {
                LivePreviewDebugDump.Write(
                    "OpenGL first frame render started");
            }
            long renderStartTimestamp = Stopwatch.GetTimestamp();
            window.DoRender();
            long swapStartTimestamp = Stopwatch.GetTimestamp();
            if (window.GLContext is not { } context)
            {
                throw new InvalidOperationException(
                    "The initialized Live Preview window has no OpenGL context.");
            }
            context.SwapBuffers();
            long renderEndTimestamp = Stopwatch.GetTimestamp();
            _swapMilliseconds =
                (renderEndTimestamp - swapStartTimestamp) * 1000.0 /
                Stopwatch.Frequency;
            if (_hasSwapSample)
            {
                _swapAverageMilliseconds +=
                    (_swapMilliseconds - _swapAverageMilliseconds) *
                    FrameTimingAverageWeight;
            }
            else
            {
                _swapAverageMilliseconds = _swapMilliseconds;
                _hasSwapSample = true;
            }

            _hostRenderMilliseconds =
                (renderEndTimestamp - renderStartTimestamp) * 1000.0 /
                Stopwatch.Frequency;
            if (_hasHostRenderSample)
            {
                _hostRenderAverageMilliseconds +=
                    (_hostRenderMilliseconds -
                     _hostRenderAverageMilliseconds) *
                    FrameTimingAverageWeight;
            }
            else
            {
                _hostRenderAverageMilliseconds = _hostRenderMilliseconds;
                _hasHostRenderSample = true;
            }

            _renderer?.RecordPresentedFrame();
            if (firstFrame)
            {
                LivePreviewDebugDump.Write(
                    $"OpenGL first frame presented; " +
                    $"renderAndSwap={Stopwatch.GetElapsedTime(renderStartTimestamp, renderEndTimestamp).TotalMilliseconds:0}ms; " +
                    $"swap={Stopwatch.GetElapsedTime(swapStartTimestamp, renderEndTimestamp).TotalMilliseconds:0}ms");
            }
            ReclaimSettledStartupWorkingSet();
            RefreshTelemetrySnapshot(renderEndTimestamp);
        }
        catch (Exception exception)
        {
            LivePreviewDebugDump.Write(
                $"OpenGL render loop failed: {exception}");
            Failed?.Invoke(this, exception);
            Dispose();
        }
    }

    private bool ShouldStop(IWindow window) =>
        _closeRequested || window.IsClosing;

    private void ReclaimSettledStartupWorkingSet()
    {
        if (_startupWorkingSetReclaimed || _renderer is not { } renderer)
            return;

        _startupRenderedFrameCount = checked(
            _startupRenderedFrameCount + 1);
        if (renderer.IsStartupWorkingSetSettled(_interaction.Camera))
        {
            _startupSettledFrameCount = checked(
                _startupSettledFrameCount + 1);
        }
        else
        {
            _startupSettledFrameCount = 0;
        }

        const int requiredSettledFrameCount = 2;
        const int maximumStartupFrameCount = 120;
        if (_startupSettledFrameCount < requiredSettledFrameCount &&
            _startupRenderedFrameCount < maximumStartupFrameCount)
        {
            return;
        }

        long reclaimStarted = Stopwatch.GetTimestamp();
        LivePreviewDebugDump.Write(
            "OpenGL settled-startup memory reclaim started");
        RenderBuildMemoryReclaimer.ReclaimCompletedBuildWorkspace();
        LivePreviewDebugDump.Write(
            $"OpenGL settled-startup memory reclaim completed; " +
            $"elapsed={Stopwatch.GetElapsedTime(reclaimStarted).TotalMilliseconds:0}ms");
        _startupWorkingSetReclaimed = true;
    }

    private void RefreshTelemetrySnapshot(long timestamp)
    {
        if (_renderer is not { } renderer ||
            (_telemetrySnapshot is not null &&
             timestamp < _nextTelemetryRefreshTimestamp))
        {
            return;
        }

        _telemetrySnapshot = renderer.FrameTelemetry;
        _nextTelemetryRefreshTimestamp = timestamp + TelemetryRefreshTicks;
    }

    private void Window_Load()
    {
        LivePreviewDebugDump.Write(
            "OpenGL Window_Load entered");
        IWindow window = _window ?? throw new InvalidOperationException(
            "The Silk map render window was not created.");
        SilkMapRenderOpenGlShareGroup.Lease shareGroupLease =
            _shareGroupLease ?? throw new InvalidOperationException(
                "The Silk map render window has no OpenGL share-group lease.");
        GL gl = GL.GetApi(window);
        LivePreviewDebugDump.Write(
            $"OpenGL context: {DescribeOpenGlContext(gl)}");
        long successfulLinksBefore =
            shareGroupLease.ProgramCache.SuccessfulLinkCount;
        long linkReusesBefore =
            shareGroupLease.ProgramCache.LinkReuseCount;
        LivePreviewDebugDump.Write(
            "OpenGL map renderer construction started");
        _renderer = new SilkOpenGlMapRenderer(
            gl,
            shareGroupLease.ProgramCache);
        _renderer.EditorPreviewFogRenderingEnabled = true;
        _renderer.ShowTexturedGeometry = true;
        _renderer.SetHostFramebuffer(0);
        LivePreviewDebugDump.Write(
            "OpenGL map renderer construction completed");
        Vector2D<int> initialSize = window.Size;
        float initialAspectRatio =
            Math.Max(1, initialSize.X) /
            (float)Math.Max(1, initialSize.Y);
        long rendererLoadStarted = Stopwatch.GetTimestamp();
        LivePreviewDebugDump.Write(
            $"OpenGL map renderer Load started; " +
            $"window={initialSize.X}x{initialSize.Y}; " +
            $"framebuffer={window.FramebufferSize.X}x{window.FramebufferSize.Y}");
        _renderer.Load(
            _scene,
            _sceneSnapshot,
            _interaction.Camera,
            initialAspectRatio,
            LivePreviewDebugDump.Write);
        LivePreviewDebugDump.Write(
            $"OpenGL map renderer Load completed; " +
            $"elapsed={Stopwatch.GetElapsedTime(rendererLoadStarted).TotalMilliseconds:0}ms");
        long reclaimStarted = Stopwatch.GetTimestamp();
        LivePreviewDebugDump.Write(
            "OpenGL post-load memory reclaim started");
        RenderBuildMemoryReclaimer.ReclaimCompletedBuildWorkspace();
        LivePreviewDebugDump.Write(
            $"OpenGL post-load memory reclaim completed; " +
            $"elapsed={Stopwatch.GetElapsedTime(reclaimStarted).TotalMilliseconds:0}ms");
        string programReuse =
            $"OpenGL program reuse for '{_scene.Name}': " +
            $"newLinks={shareGroupLease.ProgramCache.SuccessfulLinkCount - successfulLinksBefore}, " +
            $"reusedLinks={shareGroupLease.ProgramCache.LinkReuseCount - linkReusesBefore}, " +
            $"cached={shareGroupLease.ProgramCache.CachedProgramCount}/" +
            $"{shareGroupLease.ProgramCache.MaximumEntryCount}, " +
            $"capacityBypass={shareGroupLease.ProgramCache.CapacityBypassCount}.";
        Console.WriteLine(programReuse);
        LivePreviewDebugDump.Write(programReuse);
        LivePreviewDebugDump.Write(
            "OpenGL FPS overlay construction started");
        _fpsOverlay = new SilkMapRenderFpsOverlay(gl);
        LivePreviewDebugDump.Write(
            "OpenGL FPS overlay construction completed");
        LivePreviewDebugDump.Write(
            "OpenGL initial surface resize started");
        ResizeRendererSurfaces();
        LivePreviewDebugDump.Write(
            "OpenGL initial surface resize completed");
        LivePreviewDebugDump.Write(
            "OpenGL interaction initialization started");
        _interaction.Initialize(window);
        LivePreviewDebugDump.Write(
            "OpenGL interaction initialization completed");
        LivePreviewDebugDump.Write(
            "OpenGL Window_Load completed");
    }

    private static string DescribeOpenGlContext(GL gl)
    {
        try
        {
            return
                $"vendor={gl.GetStringS(StringName.Vendor)}; " +
                $"renderer={gl.GetStringS(StringName.Renderer)}; " +
                $"version={gl.GetStringS(StringName.Version)}; " +
                $"glsl={gl.GetStringS(StringName.ShadingLanguageVersion)}";
        }
        catch (Exception exception)
        {
            return $"identity unavailable ({exception.Message})";
        }
    }

    private void Window_Resize(Vector2D<int> size) => ResizeRendererSurfaces();

    private void Window_FramebufferResize(Vector2D<int> size) =>
        ResizeRendererSurfaces();

    private void ResizeRendererSurfaces()
    {
        if (_window is not { } window || _renderer is not { } renderer)
            return;

        Vector2D<int> logicalSize = window.Size;
        Vector2D<int> framebufferSize = window.FramebufferSize;
        renderer.SetHostFramebuffer(0);
        renderer.Resize(new MapRenderSurfaceExtents(
            new MapRenderPixelExtent(
                Math.Max(1, logicalSize.X),
                Math.Max(1, logicalSize.Y)),
            new MapRenderPixelExtent(
                Math.Max(1, framebufferSize.X),
                Math.Max(1, framebufferSize.Y))));
    }

    private void Window_Update(double elapsedSeconds)
    {
        if (_window is not { } window)
            return;

        if (_interaction.Update(window, elapsedSeconds))
            _closeRequested = true;
    }

    private void Window_Render(double elapsedSeconds)
    {
        SilkOpenGlMapRenderer? renderer = _renderer;
        renderer?.Render(_interaction.Camera);
        if (renderer is null || _fpsOverlay is not { } fpsOverlay ||
            _telemetrySnapshot is not { } telemetrySnapshot ||
            _window is not { } window)
        {
            return;
        }

        Vector2D<int> logicalSize = window.Size;
        Vector2D<int> framebufferSize = window.FramebufferSize;
        float renderScaling = Math.Max(
            framebufferSize.X / (float)Math.Max(1, logicalSize.X),
            framebufferSize.Y / (float)Math.Max(1, logicalSize.Y));
        try
        {
            fpsOverlay.Render(
                telemetrySnapshot,
                _hostRenderMilliseconds,
                _hostRenderAverageMilliseconds,
                _swapMilliseconds,
                _swapAverageMilliseconds,
                Math.Max(1, framebufferSize.X),
                Math.Max(1, framebufferSize.Y),
                renderScaling);
        }
        finally
        {
            // The HUD changes only program/VAO/buffer and fixed draw state.
            // Its texture bindings stay untouched, so retain the renderer's
            // known texture state instead of discarding it wholesale.
            renderer.AdoptStateAfterExternalOpenGlOverlay();
        }
    }

    private void Window_Closing() => _closeRequested = true;

    public void Dispose()
    {
        if (_disposed)
            return;

        LivePreviewDebugDump.Write(
            "OpenGL Live Preview host disposal started");
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        IWindow? window = _window;
        _window = null;
        if (window is not null)
        {
            window.Load -= Window_Load;
            window.Resize -= Window_Resize;
            window.FramebufferResize -= Window_FramebufferResize;
            window.Update -= Window_Update;
            window.Render -= Window_Render;
            window.Closing -= Window_Closing;
        }

        try
        {
            if (window?.GLContext is { } context &&
                !context.IsCurrent)
            {
                context.MakeCurrent();
            }
            try
            {
                _fpsOverlay?.Dispose();
            }
            finally
            {
                _fpsOverlay = null;
                _telemetrySnapshot = null;
                try
                {
                    _renderer?.Dispose();
                }
                finally
                {
                    _renderer = null;
                    _interaction.Dispose();
                }
            }
        }
        finally
        {
            window?.Reset();
            _shareGroupLease?.Dispose();
            _shareGroupLease = null;
            LivePreviewDebugDump.Write(
                "OpenGL Live Preview host disposal completed");
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SilkMapRenderWindow));
    }
}
