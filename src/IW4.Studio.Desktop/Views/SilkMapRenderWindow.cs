using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Avalonia.Threading;
using IW4.Render;
using IW4.Render.Diagnostics;
using IW4.Render.OpenGl;
using IW4.Render.Picking;
using IW4.Render.Resources;
using IW4.Render.Transforms;
using IW4.Studio.Desktop.Rendering;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Native Silk.NET host for a full-map OpenGL render. It deliberately owns
/// its own framebuffer and context; the map renderer's presentation path
/// targets a native Silk back buffer rather than an Avalonia composition FBO.
/// </summary>
internal sealed class SilkMapRenderWindow : IDisposable
{
    private static readonly long TelemetryRefreshTicks =
        Math.Max(1, Stopwatch.Frequency);
    private const double FrameTimingAverageWeight = 1.0 / 32.0;

    private readonly MapRenderScene _scene;
    private readonly RenderSceneSnapshot _sceneSnapshot;
    private readonly Func<string, Task>? _copyTextAsync;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render)
    {
        // A zero dispatcher interval immediately requeues the next native
        // pump after the current callback yields. A fixed delay is paid on
        // top of every frame and becomes an artificial FPS ceiling once the
        // renderer itself is below the display interval.
        Interval = TimeSpan.Zero
    };
    private IWindow? _window;
    private IInputContext? _input;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
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
    private RenderCamera _camera;
    private Vector2 _lastMousePosition;
    private bool _wasDragging;
    private bool _wasPickKeyPressed;
    private MapRenderPickHit? _selectedPick;
    private IReadOnlyList<MapRenderPickCandidate> _selectedPickCandidates = [];
    private IReadOnlyList<MapRenderPickCandidate> _selectedNeighborCandidates = [];
    private bool _lastPickMiss;
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
        _copyTextAsync = copyTextAsync;
        _camera = CreateInitialCamera(scene.CameraBounds, mapEntityString);
        _timer.Tick += Timer_Tick;
    }

    public event EventHandler<Exception>? Failed;

    public event EventHandler? Stopped;

    private static RenderCamera CreateInitialCamera(
        RenderBounds bounds,
        string? mapEntityString)
    {
        return TryCreateGlobalIntermissionCamera(
                bounds,
                mapEntityString,
                out RenderCamera camera)
            ? camera
            : CreateBoundsCamera(bounds);
    }

    private static RenderCamera CreateBoundsCamera(RenderBounds bounds)
    {
        const float previewNearPlane = 4f;
        float radius = bounds.Radius;
        float targetHeight = bounds.IsValid
            ? bounds.Center.Y - radius * 0.001f
            : 0f;
        Vector3 target = new(
            radius * 0.04f,
            targetHeight,
            -radius * 0.074f);
        Vector3 position = target + new Vector3(
            -radius * 0.14f,
            radius * 0.02f,
            radius * 0.14f);
        Vector3 direction = Vector3.Normalize(target - position);
        return new RenderCamera(
            position,
            MathF.Atan2(direction.X, -direction.Z),
            MathF.Asin(direction.Y),
            55f * MathF.PI / 180f,
            previewNearPlane,
            MathF.Max(250000f, position.Y + radius * 4f));
    }

    private static bool TryCreateGlobalIntermissionCamera(
        RenderBounds bounds,
        string? mapEntityString,
        out RenderCamera camera)
    {
        camera = default;
        if (string.IsNullOrWhiteSpace(mapEntityString))
            return false;

        ReadOnlySpan<char> source = mapEntityString.AsSpan();
        int sourceOffset = 0;
        while (TryReadEntity(source, ref sourceOffset, out ReadOnlySpan<char> entity))
        {
            if (!TryReadGlobalIntermission(
                    entity,
                    out Vector3 gameOrigin,
                    out Vector3 gameAngles))
            {
                continue;
            }

            const float previewNearPlane = 4f;
            const float degreesToRadians = MathF.PI / 180f;
            Vector3 position =
                RenderCoordinateConverter.GameToRenderPosition(gameOrigin);
            float yaw = NormalizeDegrees(90f - gameAngles.Y) *
                degreesToRadians;
            float pitch = -NormalizeDegrees(gameAngles.X) *
                degreesToRadians;
            camera = new RenderCamera(
                position,
                yaw,
                pitch,
                55f * degreesToRadians,
                previewNearPlane,
                MathF.Max(250000f, position.Y + bounds.Radius * 4f));
            return true;
        }

        return false;
    }

    private static bool TryReadEntity(
        ReadOnlySpan<char> source,
        ref int sourceOffset,
        out ReadOnlySpan<char> entity)
    {
        entity = default;
        int relativeStart = source[sourceOffset..].IndexOf('{');
        if (relativeStart < 0)
            return false;

        int entityStart = sourceOffset + relativeStart + 1;
        bool quoted = false;
        bool escaped = false;
        for (int index = entityStart; index < source.Length; index++)
        {
            char current = source[index];
            if (quoted)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == '"')
                    quoted = false;
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == '}')
            {
                entity = source[entityStart..index];
                sourceOffset = index + 1;
                return true;
            }
        }

        sourceOffset = source.Length;
        return false;
    }

    private static bool TryReadGlobalIntermission(
        ReadOnlySpan<char> entity,
        out Vector3 origin,
        out Vector3 angles)
    {
        origin = default;
        angles = default;
        bool isGlobalIntermission = false;
        bool hasOrigin = false;
        bool hasAngles = false;
        int offset = 0;
        while (offset < entity.Length)
        {
            SkipWhitespace(entity, ref offset);
            if (offset == entity.Length)
                break;
            if (!TryReadQuotedToken(entity, ref offset, out ReadOnlySpan<char> key))
                return false;

            SkipWhitespace(entity, ref offset);
            if (!TryReadQuotedToken(entity, ref offset, out ReadOnlySpan<char> value))
                return false;

            if (key.Equals("classname", StringComparison.OrdinalIgnoreCase))
            {
                isGlobalIntermission = value.Equals(
                    "mp_global_intermission",
                    StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("origin", StringComparison.OrdinalIgnoreCase))
                hasOrigin = TryParseVector3(value, out origin);
            else if (key.Equals("angles", StringComparison.OrdinalIgnoreCase))
                hasAngles = TryParseVector3(value, out angles);
        }

        return isGlobalIntermission && hasOrigin && hasAngles;
    }

    private static bool TryReadQuotedToken(
        ReadOnlySpan<char> source,
        ref int offset,
        out ReadOnlySpan<char> value)
    {
        value = default;
        if (offset >= source.Length || source[offset] != '"')
            return false;

        int valueStart = ++offset;
        bool escaped = false;
        while (offset < source.Length)
        {
            char current = source[offset];
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (current == '"')
            {
                value = source[valueStart..offset];
                offset++;
                return true;
            }
            offset++;
        }

        return false;
    }

    private static bool TryParseVector3(
        ReadOnlySpan<char> source,
        out Vector3 value)
    {
        value = default;
        Span<float> components = stackalloc float[3];
        int offset = 0;
        for (int component = 0; component < components.Length; component++)
        {
            SkipWhitespace(source, ref offset);
            int start = offset;
            while (offset < source.Length && !char.IsWhiteSpace(source[offset]))
                offset++;
            if (start == offset ||
                !float.TryParse(
                    source[start..offset],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out components[component]) ||
                !float.IsFinite(components[component]))
            {
                return false;
            }
        }

        SkipWhitespace(source, ref offset);
        if (offset != source.Length)
            return false;

        value = new Vector3(components[0], components[1], components[2]);
        return true;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> source, ref int offset)
    {
        while (offset < source.Length && char.IsWhiteSpace(source[offset]))
            offset++;
    }

    private static float NormalizeDegrees(float degrees)
    {
        float normalized = degrees % 360f;
        if (normalized > 180f)
            return normalized - 360f;
        if (normalized <= -180f)
            return normalized + 360f;
        return normalized;
    }

    public void Show()
    {
        ThrowIfDisposed();
        if (_window is not null)
            return;

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

        _shareGroupLease = SilkMapRenderOpenGlShareGroup.Acquire();
        options.SharedContext = _shareGroupLease.SharedContext;
        IWindow window;
        try
        {
            window = Window.Create(options);
        }
        catch
        {
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
            ReclaimSettledStartupWorkingSet();
            RefreshTelemetrySnapshot(renderEndTimestamp);
        }
        catch (Exception exception)
        {
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
        if (renderer.IsStartupWorkingSetSettled(_camera))
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
        IWindow window = _window ?? throw new InvalidOperationException(
            "The Silk map render window was not created.");
        SilkMapRenderOpenGlShareGroup.Lease shareGroupLease =
            _shareGroupLease ?? throw new InvalidOperationException(
                "The Silk map render window has no OpenGL share-group lease.");
        GL gl = GL.GetApi(window);
        long successfulLinksBefore =
            shareGroupLease.ProgramCache.SuccessfulLinkCount;
        long linkReusesBefore =
            shareGroupLease.ProgramCache.LinkReuseCount;
        _renderer = new SilkOpenGlMapRenderer(
            gl,
            shareGroupLease.ProgramCache);
        _renderer.EditorPreviewFogRenderingEnabled = true;
        _renderer.ShowTexturedGeometry = true;
        _renderer.SetHostFramebuffer(0);
        Vector2D<int> initialSize = window.Size;
        _renderer.Load(
            _scene,
            _sceneSnapshot,
            _camera,
            Math.Max(1, initialSize.X) /
            (float)Math.Max(1, initialSize.Y));
        RenderBuildMemoryReclaimer.ReclaimCompletedBuildWorkspace();
        Console.WriteLine(
            $"OpenGL program reuse for '{_scene.Name}': " +
            $"newLinks={shareGroupLease.ProgramCache.SuccessfulLinkCount - successfulLinksBefore}, " +
            $"reusedLinks={shareGroupLease.ProgramCache.LinkReuseCount - linkReusesBefore}, " +
            $"cached={shareGroupLease.ProgramCache.CachedProgramCount}/" +
            $"{shareGroupLease.ProgramCache.MaximumEntryCount}, " +
            $"capacityBypass={shareGroupLease.ProgramCache.CapacityBypassCount}.");
        _fpsOverlay = new SilkMapRenderFpsOverlay(gl);
        ResizeRendererSurfaces();

        _input = window.CreateInput();
        _keyboard = _input.Keyboards.FirstOrDefault();
        _mouse = _input.Mice.FirstOrDefault();
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
        if (_keyboard is null)
            return;

        if (_keyboard.IsKeyPressed(Key.Escape))
        {
            _closeRequested = true;
            return;
        }

        UpdateMouseLook();
        ApplyPicking();

        float delta =
            MapRenderCameraUpdateTiming.ClampElapsedSeconds(elapsedSeconds);
        float yaw = _camera.YawRadians;
        if (_keyboard.IsKeyPressed(Key.Left))
            yaw -= 1.6f * delta;
        if (_keyboard.IsKeyPressed(Key.Right))
            yaw += 1.6f * delta;

        Vector3 movement = Vector3.Zero;
        if (_keyboard.IsKeyPressed(Key.W))
            movement += _camera.Forward;
        if (_keyboard.IsKeyPressed(Key.S))
            movement -= _camera.Forward;
        if (_keyboard.IsKeyPressed(Key.D))
            movement += _camera.Right;
        if (_keyboard.IsKeyPressed(Key.A))
            movement -= _camera.Right;
        if (_keyboard.IsKeyPressed(Key.Up))
            movement += Vector3.UnitY;
        if (_keyboard.IsKeyPressed(Key.Down))
            movement -= Vector3.UnitY;

        float moveSpeed = _keyboard.IsKeyPressed(Key.ShiftLeft)
            ? 2200f
            : 700f;
        _camera = _camera with
        {
            YawRadians = yaw,
            Position = movement == Vector3.Zero
                ? _camera.Position
                : _camera.Position +
                  Vector3.Normalize(movement) * moveSpeed * delta
        };
    }

    private void UpdateMouseLook()
    {
        if (_mouse is null)
            return;

        Vector2 position = _mouse.Position;
        if (!_mouse.IsButtonPressed(MouseButton.Left))
        {
            _wasDragging = false;
            _lastMousePosition = position;
            return;
        }

        if (!_wasDragging)
        {
            _wasDragging = true;
            _lastMousePosition = position;
            return;
        }

        Vector2 delta = position - _lastMousePosition;
        _lastMousePosition = position;
        _camera = _camera with
        {
            YawRadians = _camera.YawRadians + delta.X * 0.004f,
            PitchRadians = Math.Clamp(
                _camera.PitchRadians - delta.Y * 0.004f,
                -1.55f,
                1.55f)
        };
    }

    private void ApplyPicking()
    {
        bool isPickKeyPressed = _keyboard?.IsKeyPressed(Key.P) == true;
        if (isPickKeyPressed && !_wasPickKeyPressed)
        {
            PickUnderMouse();
            CopyCurrentPickToClipboard();
        }

        _wasPickKeyPressed = isPickKeyPressed;
    }

    private void PickUnderMouse()
    {
        if (_mouse is null || _window is null)
            return;

        Vector2D<int> size = _window.Size;
        Vector2 viewport = new(Math.Max(1, size.X), Math.Max(1, size.Y));
        _selectedPickCandidates = MapRenderPicker.PickCandidates(
            _scene,
            _camera,
            _mouse.Position,
            viewport,
            includeUntexturedGeometry: false,
            includeCollision: false);
        if (MapRenderPicker.TryPick(
                _scene,
                _camera,
                _mouse.Position,
                viewport,
                includeUntexturedGeometry: false,
                includeCollision: false,
                out MapRenderPickHit hit))
        {
            _selectedPick = hit;
            _selectedNeighborCandidates = MapRenderPicker.FindNearbyCandidates(
                _scene,
                hit.Position,
                includeUntexturedGeometry: false,
                includeCollision: false);
            _lastPickMiss = false;
            return;
        }

        _selectedPick = null;
        _selectedNeighborCandidates = [];
        _lastPickMiss = true;
    }

    private void CopyCurrentPickToClipboard()
    {
        if (_copyTextAsync is null)
            return;

        string text = MapRenderPickClipboardFormatter.Format(
            _scene,
            _camera,
            _selectedPick,
            _selectedPickCandidates,
            _selectedNeighborCandidates,
            _lastPickMiss);
        _ = _copyTextAsync(text);
    }

    private void Window_Render(double elapsedSeconds)
    {
        SilkOpenGlMapRenderer? renderer = _renderer;
        renderer?.Render(_camera);
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
                    _input?.Dispose();
                    _input = null;
                    _keyboard = null;
                    _mouse = null;
                }
            }
        }
        finally
        {
            window?.Reset();
            _shareGroupLease?.Dispose();
            _shareGroupLease = null;
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SilkMapRenderWindow));
    }
}
