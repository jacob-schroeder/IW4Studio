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
        PointerCaptureLost += (_, _) => { if (_gestures.IsActive) _gestures.CancelGesture(); };
    }

    internal EditorSession? Session
    {
        get => _gestures.Session;
        set
        {
            if (ReferenceEquals(Session, value)) return;
            _gestures.CancelGesture();
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

    internal event Action<string>? CursorStatusChanged
    {
        add => _gestures.CursorStatusChanged += value;
        remove => _gestures.CursorStatusChanged -= value;
    }

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

    internal bool HasClipPreview => _gestures.CanCommitClip;
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
            if (Session?.HasPlacement == true) Session.CancelPlacement();
            else if (_gestures.IsActive || _gestures.HasClipPreview) _gestures.CancelGesture();
            else Session?.Select(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _gestures.HasClipPreview)
        {
            e.Handled = CommitClip();
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None)
        {
            FrameSelection();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gestures.CancelGesture();
        base.OnDetachedFromVisualTree(e);
    }
}
