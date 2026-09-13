using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Viewports.Orthographic;

public sealed class OrthoViewport : Control
{
    private readonly OrthographicProjection _projection = new();
    private readonly OrthographicDrawing _drawing;
    private readonly OrthographicGestures _gestures;

    public OrthoViewport()
    {
        _drawing = new OrthographicDrawing(_projection);
        _gestures = new OrthographicGestures(this, _projection);
        Focusable = true;
        ClipToBounds = true;
        PointerCaptureLost += (_, _) => _gestures.EndGesture(cancel: true);
    }

    internal EditorSession? Session
    {
        get => _gestures.Session;
        set
        {
            if (ReferenceEquals(Session, value)) return;
            _gestures.EndGesture(cancel: true);
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
            _gestures.EndGesture(cancel: true);
            _projection.SetPlane(value);
            InvalidateVisual();
        }
    }

    internal event Action<string>? CursorStatusChanged
    {
        add => _gestures.CursorStatusChanged += value;
        remove => _gestures.CursorStatusChanged -= value;
    }

    internal void CompleteGesture() => _gestures.EndGesture(cancel: false);

    public void FrameAll()
    {
        if (Session is not { } session) return;
        _gestures.EndGesture(cancel: true);
        var bounds = session.Document.Brushes.Select(brush => brush.GetBounds())
            .Concat(session.Document.Terrains.Where(terrain => terrain.Vertices.Length > 0)
                .Select(terrain => terrain.GetBounds()))
            .Concat(OrthographicGeometry.PointEntities(session.Document).Select(entity =>
                (EditorSession.EntityOrigin(entity) - new Vector3(8),
                 EditorSession.EntityOrigin(entity) + new Vector3(8)))).ToArray();
        if (bounds.Length == 0)
        {
            _projection.Reset();
            InvalidateVisual();
            return;
        }
        Frame(bounds.Select(bound => bound.Item1).Aggregate(Vector3.Min),
            bounds.Select(bound => bound.Item2).Aggregate(Vector3.Max));
    }

    public void FrameSelection()
    {
        _gestures.EndGesture(cancel: true);
        if (Session?.SelectionBounds is { } bounds) Frame(bounds.Min, bounds.Max);
        else FrameAll();
    }

    private void Frame(Vector3 minimum, Vector3 maximum)
    {
        _projection.Frame(minimum, maximum);
        InvalidateVisual();
    }

    private void SessionChanged(object? sender, EventArgs e) => _gestures.SessionChanged();

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (_gestures.IsActive) _gestures.EndGesture(cancel: true);
            else Session?.Select(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None)
        {
            FrameSelection();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gestures.EndGesture(cancel: true);
        base.OnDetachedFromVisualTree(e);
    }
}
