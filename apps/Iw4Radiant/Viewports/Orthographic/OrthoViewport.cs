using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Viewports.Orthographic;

public sealed class OrthoViewport : Control
{
    private readonly OrthographicProjection _projection = new();
    private readonly OrthographicDrawing _drawing;
    private readonly OrthographicGestures _gestures;
    private ContextMenu? _objectMenu;

    public OrthoViewport()
    {
        _drawing = new OrthographicDrawing(_projection);
        _gestures = new OrthographicGestures(this, _projection);
        _gestures.ContextMenuRequested += OpenObjectMenu;
        _gestures.CursorStatusChanged += message => CursorStatusChanged?.Invoke(message);
        Focusable = true;
        ClipToBounds = true;
        PointerCaptureLost += (_, _) => { if (_gestures.IsActive) _gestures.CancelGesture(); };
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnModelDragOver);
        DragDrop.AddDropHandler(this, OnModelDrop);
    }

    internal EditorSession? Session
    {
        get => _gestures.Session;
        set
        {
            if (ReferenceEquals(Session, value)) return;
            _gestures.CancelGesture();
            _objectMenu?.Close();
            if (Session is { } previous) previous.Changed -= SessionChanged;
            _gestures.Session = value;
            if (Session is { } current) current.Changed += SessionChanged;
            InvalidateVisual();
        }
    }

    public OrthoPlane Plane
    {
        get => _projection.Plane;
        set
        {
            if (Plane == value) return;
            _gestures.CancelGesture();
            _projection.SetPlane(value);
            InvalidateVisual();
        }
    }

    internal event Action<string>? CursorStatusChanged;

    internal event Action? ClipStarted
    {
        add => _gestures.ClipStarted += value;
        remove => _gestures.ClipStarted -= value;
    }

    internal event Action? ClipPreviewChanged
    {
        add => _gestures.ClipPreviewChanged += value;
        remove => _gestures.ClipPreviewChanged -= value;
    }

    internal event Action<BrushKind>? BrushKindRequested;
    internal event Action<MapEntity>? EntityInspectorRequested;
    internal event Action? ModelsRequested;
    internal event Action? PrefabsRequested;
    internal event Action? OrganizationRequested;
    internal Func<bool>? CanAcceptModelDrop { get; set; }

    internal bool HasClipPreview => _gestures.CanCommitClip;
    internal bool HasActiveGesture => _gestures.IsActive || _gestures.HasClipPreview;
    internal void CompleteGesture() => _gestures.EndGesture(cancel: false);
    internal void CancelGesture() => _gestures.CancelGesture();
    internal bool CommitClip() => _gestures.CommitClip();

    public void FrameAll()
    {
        if (Session is not { } session) return;
        _gestures.CancelGesture();
        if (session.Scene.VisibleBounds is not { } bounds)
        {
            _projection.Reset();
            InvalidateVisual();
            return;
        }
        Frame(bounds.Min, bounds.Max);
    }

    public void FrameSelection()
    {
        _gestures.CancelGesture();
        if (Session?.SelectionBounds is { } bounds) Frame(bounds.Min, bounds.Max);
        else FrameAll();
    }

    private void Frame(Vector3 minimum, Vector3 maximum)
    {
        _projection.Frame(minimum, maximum);
        InvalidateVisual();
    }

    private void SessionChanged(object? sender, EventArgs e)
    {
        _objectMenu?.Close();
        _gestures.SessionChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            _projection.Size = Bounds.Size;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _drawing.Draw(context, Session, _gestures, IsFocused);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _gestures.PointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _gestures.PointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _gestures.PointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_gestures.IsActive) return;
        _projection.ZoomAt(e.GetPosition(this), e.Delta.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _gestures.PointerExited();
    }

    private void OnModelDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanAcceptModelDrop?.Invoke() != false && XModelDrag.TryRead(e.DataTransfer, out _, out _)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnModelDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (CanAcceptModelDrop?.Invoke() == false || Session is not { } session ||
            !XModelDrag.TryRead(e.DataTransfer, out string name, out _) ||
            session.Scene.ResolveModel?.Invoke(name) is not { } model) return;
        try
        {
            _gestures.CancelGesture();
            if (session.HasPlacement) session.CancelPlacement();
            Vector2 point = _projection.ToWorld(e.GetPosition(this));
            point = new Vector2(session.Snap(point.X), session.Snap(point.Y));
            Vector3 position = _projection.Unproject(point, session.Snap(session.BrushBottom));
            XModelEditing.Place(session, model, position);
            CursorStatusChanged?.Invoke($"Placed {name} on the {Plane.ToString().ToLowerInvariant()} grid.");
            e.DragEffects = DragDropEffects.Copy;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or IOException)
        { CursorStatusChanged?.Invoke(exception.Message); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (HasActiveGesture) _gestures.CancelGesture();
            else if (Session?.HasPlacement == true) Session.CancelPlacement();
            else Session?.Select(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _gestures.HasClipPreview)
        {
            e.Handled = CommitClip();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gestures.CancelGesture();
        _objectMenu?.Close();
        base.OnDetachedFromVisualTree(e);
    }

    private void OpenObjectMenu(Point point, Vector3 position)
    {
        if (Session is not { } session) return;
        _objectMenu?.Close();
        _objectMenu = OrthographicObjectMenu.Open(this, session,
            OrthographicGeometry.HitTest(session, _projection, point), position,
            kind => BrushKindRequested?.Invoke(kind),
            entity => EntityInspectorRequested?.Invoke(entity),
            () => ModelsRequested?.Invoke(),
            () => PrefabsRequested?.Invoke(),
            () => OrganizationRequested?.Invoke(),
            message => CursorStatusChanged?.Invoke(message));
    }
}
