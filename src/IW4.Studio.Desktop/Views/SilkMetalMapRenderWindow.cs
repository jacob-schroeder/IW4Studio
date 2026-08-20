using System.Runtime.Versioning;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.Diagnostics;
using IW4.Render.Metal;
using IW4.Render.Metal.Native;
using IW4.Render.Resources;
using IW4.Studio.Desktop.Rendering;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Native Silk.NET host for the macOS Metal map renderer. Metal owns command
/// submission and drawable presentation; the Silk window supplies only the
/// Cocoa window, event pump, and input devices.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class SilkMetalMapRenderWindow : INativeMapRenderWindow
{
    private readonly MapRenderScene _scene;
    private readonly RenderSceneSnapshot _sceneSnapshot;
    private readonly NativeMapRenderInteraction _interaction;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.Zero
    };
    private IWindow? _window;
    private MetalLayerHost? _layerHost;
    private MetalMapRenderer? _renderer;
    private int _startupRenderedFrameCount;
    private int _startupSettledFrameCount;
    private bool _startupWorkingSetReclaimed;
    private bool _disposed;
    private bool _closeRequested;
    private long _nextTelemetryTimestamp;

    internal SilkMetalMapRenderWindow(
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

        WindowOptions options = WindowOptions.Default;
        options.Title = $"Live Preview - {_scene.Name} - IW4 Studio";
        options.Size = new Vector2D<int>(1280, 720);
        options.API = GraphicsAPI.None;
        options.VSync = false;
        options.ShouldSwapAutomatically = false;

        IWindow window = Window.Create(options);
        window.Load += Window_Load;
        window.Resize += Window_Resize;
        window.FramebufferResize += Window_FramebufferResize;
        window.Update += Window_Update;
        window.Render += Window_Render;
        window.Closing += Window_Closing;
        _window = window;
        try
        {
            window.Initialize();
            _timer.Start();
        }
        catch
        {
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

            window.DoRender();
            _renderer?.RecordPresentedFrame();
            PublishTelemetry(window);
            ReclaimSettledStartupWorkingSet();
        }
        catch (Exception exception)
        {
            Failed?.Invoke(this, exception);
            Dispose();
        }
    }

    private bool ShouldStop(IWindow window) =>
        _closeRequested || window.IsClosing;

    private void PublishTelemetry(IWindow window)
    {
        if (_renderer is not { } renderer)
            return;

        long now = Stopwatch.GetTimestamp();
        if (now < _nextTelemetryTimestamp)
            return;
        _nextTelemetryTimestamp = checked(now + Stopwatch.Frequency);

        MapRenderFrameTelemetrySnapshot snapshot = renderer.FrameTelemetry;
        long draws = LatestCounter(
            snapshot,
            MapRenderFrameCounter.DrawCalls);
        long triangles = LatestCounter(
            snapshot,
            MapRenderFrameCounter.Triangles);
        long snapshotPasses = checked(
            LatestCounter(
                snapshot,
                MapRenderFrameCounter.NormalCameraSnapshotBasePasses) +
            LatestCounter(
                snapshot,
                MapRenderFrameCounter.NormalCameraSnapshotReceiverPasses));
        long authorizedPasses = checked(
            LatestCounter(
                snapshot,
                MapRenderFrameCounter.NormalCameraAuthorizedBasePasses) +
            LatestCounter(
                snapshot,
                MapRenderFrameCounter.NormalCameraAuthorizedReceiverPasses));
        long blockedPasses = LatestCounter(
            snapshot,
            MapRenderFrameCounter.NormalCameraBlockedPasses);
        double drawableWait = LatestCpuPhase(
            snapshot,
            MapRenderCpuPhase.SwapOrPresent);
        double encodeMilliseconds = Math.Max(
            0.0,
            snapshot.CpuFrameMilliseconds.Latest - drawableWait);
        double sceneGpuMilliseconds = LatestGpuPhase(
            snapshot,
            MapRenderGpuPhase.SceneTarget);
        window.Title = string.Format(
            CultureInfo.InvariantCulture,
            "Live Preview - {0} - Metal | {1:0.0} FPS | ENCODE {2:0.00} ms | " +
            "WAIT {3:0.00} ms | SCENE GPU {4:0.00} ms | CMD {5:0.00} ms | " +
            "{6:N0} draws | {7:N0} tris | ADMIT {8:N0}/{9:N0} | BLOCKED {10:N0}",
            _scene.Name,
            snapshot.PresentedFramesPerSecond,
            encodeMilliseconds,
            drawableWait,
            sceneGpuMilliseconds,
            snapshot.GpuFrameMilliseconds.Latest,
            draws,
            triangles,
            authorizedPasses,
            snapshotPasses,
            blockedPasses);
    }

    private static long LatestCounter(
        MapRenderFrameTelemetrySnapshot snapshot,
        MapRenderFrameCounter counter)
    {
        foreach (MapRenderFrameCounterTelemetrySnapshot value in
                 snapshot.Counters)
        {
            if (value.Counter == counter)
                return value.Latest;
        }
        return 0;
    }

    private static double LatestCpuPhase(
        MapRenderFrameTelemetrySnapshot snapshot,
        MapRenderCpuPhase phase)
    {
        foreach (MapRenderCpuPhaseTelemetrySnapshot value in
                 snapshot.CpuPhases)
        {
            if (value.Phase == phase)
                return value.Milliseconds.Latest;
        }
        return 0.0;
    }

    private static double LatestGpuPhase(
        MapRenderFrameTelemetrySnapshot snapshot,
        MapRenderGpuPhase phase)
    {
        foreach (MapRenderGpuPhaseTelemetrySnapshot value in
                 snapshot.GpuPhases)
        {
            if (value.Phase == phase)
                return value.Milliseconds.Latest;
        }
        return 0.0;
    }

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

        RenderBuildMemoryReclaimer.ReclaimCompletedBuildWorkspace();
        _startupWorkingSetReclaimed = true;
    }

    private void Window_Load()
    {
        IWindow window = _window ?? throw new InvalidOperationException(
            "The Silk Metal map window was not created.");
        nint cocoaWindow = window.Native?.Cocoa ?? 0;
        if (cocoaWindow == 0)
        {
            throw new InvalidOperationException(
                "Silk.NET did not expose the native Cocoa window required " +
                "by the Metal renderer.");
        }

        Vector2D<int> framebufferSize = window.FramebufferSize;
        _layerHost = new MetalLayerHost(
            cocoaWindow,
            Math.Max(1, framebufferSize.X),
            Math.Max(1, framebufferSize.Y));
        _renderer = new MetalMapRenderer(_layerHost)
        {
            EditorPreviewFogRenderingEnabled = true,
            ShowTexturedGeometry = true
        };
        // Establish the logical Scene and physical drawable extents before
        // scene resources are created. Retina hosts otherwise build a full
        // physical-size scene target only to replace it immediately after
        // Load.
        ResizeRendererSurfaces();
        Vector2D<int> initialSize = window.Size;
        _renderer.Load(
            _scene,
            _sceneSnapshot,
            _interaction.Camera,
            Math.Max(1, initialSize.X) /
            (float)Math.Max(1, initialSize.Y));
        RenderBuildMemoryReclaimer.ReclaimCompletedBuildWorkspace();
        _interaction.Initialize(window);
    }

    private void Window_Resize(Vector2D<int> size) =>
        ResizeRendererSurfaces();

    private void Window_FramebufferResize(Vector2D<int> size) =>
        ResizeRendererSurfaces();

    private void ResizeRendererSurfaces()
    {
        if (_window is not { } window || _layerHost is not { } layerHost)
            return;

        Vector2D<int> logicalSize = window.Size;
        Vector2D<int> framebufferSize = window.FramebufferSize;
        int framebufferWidth = Math.Max(1, framebufferSize.X);
        int framebufferHeight = Math.Max(1, framebufferSize.Y);
        layerHost.Resize(framebufferWidth, framebufferHeight);
        _renderer?.Resize(new MapRenderSurfaceExtents(
            new MapRenderPixelExtent(
                Math.Max(1, logicalSize.X),
                Math.Max(1, logicalSize.Y)),
            new MapRenderPixelExtent(
                framebufferWidth,
                framebufferHeight)));
    }

    private void Window_Update(double elapsedSeconds)
    {
        if (_window is { } window &&
            _interaction.Update(window, elapsedSeconds))
        {
            _closeRequested = true;
        }
    }

    private void Window_Render(double elapsedSeconds) =>
        _renderer?.Render(_interaction.Camera);

    private void Window_Closing() => _closeRequested = true;

    public void Dispose()
    {
        if (_disposed)
            return;

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
            try
            {
                _renderer?.Dispose();
            }
            finally
            {
                _renderer = null;
                try
                {
                    _layerHost?.Dispose();
                }
                finally
                {
                    _layerHost = null;
                    _interaction.Dispose();
                }
            }
        }
        finally
        {
            window?.Reset();
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(SilkMetalMapRenderWindow));
        }
    }
}
