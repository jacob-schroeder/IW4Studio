using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed class CameraViewport : OpenGlControlBase
{
    private MapDocument _document = MapDocument.Create();
    private object? _selection;
    private readonly CameraNavigation _navigation = new();
    private readonly SceneRenderer _renderer = new();
    private IPointer? _dragPointer;
    private Point _lastPointer;
    private bool _panning;

    public CameraViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        _renderer.StatusChanged += (_, _) => RendererStatusChanged?.Invoke(this, EventArgs.Empty);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _dragPointer = null;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.F)
                return;
            FrameSelection();
            e.Handled = true;
        };
        SizeChanged += (_, _) => RequestNextFrameRendering();
    }

    internal MapDocument Document
    {
        get => _document;
        set
        {
            _document = value;
            _selection = null;
            RefreshScene();
        }
    }

    internal object? Selection
    {
        get => _selection;
        set
        {
            if (ReferenceEquals(_selection, value))
                return;
            _selection = value;
            RefreshScene();
        }
    }

    internal event Action<object?>? SelectionChanged;
    internal Func<string, string?>? ResolveTexturePath { get; set; }
    internal string? RendererError => _renderer.Error;
    internal event EventHandler? RendererStatusChanged;

    internal void RefreshScene()
    {
        _renderer.RefreshScene();
        RequestNextFrameRendering();
    }

    internal void ReloadTextures()
    {
        _renderer.ReloadTextures();
        RequestNextFrameRendering();
    }

    internal void FrameAll()
    {
        _navigation.FrameAll(Document, (float)(Bounds.Width / Math.Max(1, Bounds.Height)));
        RequestNextFrameRendering();
    }

    internal void FrameSelection()
    {
        _navigation.FrameSelection(Document, Selection, (float)(Bounds.Width / Math.Max(1, Bounds.Height)));
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl) => _renderer.Initialize(gl);

    protected override void OnOpenGlDeinit(GlInterface gl) => _renderer.ReleaseResources();

    protected override void OnOpenGlLost() => _renderer.ContextLost();

    protected override void OnOpenGlRender(GlInterface glInterface, int framebuffer)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
        _renderer.Render(size, framebuffer, _navigation.ViewProjection((float)size.Width / size.Height),
            Document, Selection, ResolveTexturePath);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
        {
            _dragPointer = e.Pointer;
            _lastPointer = e.GetPosition(this);
            _panning = properties.IsMiddleButtonPressed;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed)
        {
            Selection = CameraPicking.Pick(Document, _navigation, e.GetPosition(this), Bounds.Size);
            SelectionChanged?.Invoke(Selection);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;
        Point position = e.GetPosition(this);
        Avalonia.Vector delta = position - _lastPointer;
        _lastPointer = position;
        if (_panning)
            _navigation.Pan((float)delta.X, (float)delta.Y, (float)Math.Max(1, Bounds.Height));
        else
            _navigation.Orbit((float)delta.X, (float)delta.Y);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer))
            return;
        _dragPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(delta))
            return;
        _navigation.Zoom((float)delta);
        RequestNextFrameRendering();
        e.Handled = true;
    }
}
