using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicGestures
{
    private enum Gesture { None, Pan, Transform, Marquee, Brush, Terrain, Sculpt, Clip }
    private readonly Control _viewport;
    private readonly OrthographicProjection _projection;
    private readonly OrthographicTransform _transform;
    private readonly OrthographicTerrainStroke _stroke = new();
    private IPointer? _pointer;
    private MouseButton _button;
    private Gesture _gesture;
    private MapDocument? _gestureDocument, _clipDocument;
    private object[] _gestureItems = [], _selectionBefore = [], _marqueeCandidates = [];
    private EditorTool _gestureTool;
    private Point _startScreen, _lastScreen, _cursorScreen;
    private Vector2 _startWorld, _currentWorld;
    private bool _editStarted, _changed, _pointerInside, _changingSelection, _toggle;

    internal OrthographicGestures(Control viewport, OrthographicProjection projection)
    {
        _viewport = viewport;
        _projection = projection;
        _transform = new(projection);
    }

    internal EditorSession? Session { get; set; }
    internal event Action<string>? CursorStatusChanged;
    internal event Action? ClipStarted;
    internal bool IsActive => _gesture != Gesture.None;
    internal bool IsCreating => _gesture is Gesture.Brush or Gesture.Terrain;
    internal bool IsCreatingTerrain => _gesture == Gesture.Terrain;
    internal bool PointerInside => _pointerInside;
    internal Point CursorScreen => _cursorScreen;
    internal Vector2 StartWorld => _startWorld;
    internal Vector2 CurrentWorld => _currentWorld;
    internal Rect? MarqueeBounds => _gesture == Gesture.Marquee && Dragged ? OrthographicGeometry.Rectangle(_startScreen, _cursorScreen) : null;
    internal bool HasClipPreview => _clipDocument is not null;
    internal Vector2 ClipStart { get; private set; }
    internal Vector2 ClipEnd { get; private set; }
    private bool Dragged => OrthographicGeometry.Distance(_cursorScreen, _startScreen) >= 3;

    internal void SessionChanged()
    {
        if (Session is { } session)
        {
            if (_clipDocument is not null && (!ReferenceEquals(_clipDocument, session.Document) || session.Tool != EditorTool.Clip))
                _clipDocument = null;
            if (IsActive && !_changingSelection)
            {
                if (!ReferenceEquals(_gestureDocument, session.Document)) EndGesture(cancel: false, completeEdit: false);
                else if (_gestureTool != session.Tool || _gestureItems.Length != session.Selection.Count ||
                         _gestureItems.Any(item => !session.Selection.Contains(item))) EndGesture(cancel: true);
            }
        }
        _viewport.InvalidateVisual();
    }

    internal void PointerPressed(PointerPressedEventArgs e)
    {
        try { Press(e); }
        catch (Exception exception) when (IsInputError(exception)) { Fail(exception); e.Handled = true; }
    }

    private void Press(PointerPressedEventArgs e)
    {
        if (Session is not { } session || IsActive) return;
        PointerPointProperties properties = e.GetCurrentPoint(_viewport).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed && !properties.IsRightButtonPressed) return;
        _viewport.Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _startScreen = _lastScreen = _cursorScreen = e.GetPosition(_viewport);
        _startWorld = _currentWorld = Snap(_projection.ToWorld(_startScreen));
        _editStarted = _changed = false;
        _selectionBefore = session.Selection.Items.ToArray();
        _toggle = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        _button = properties.IsMiddleButtonPressed ? MouseButton.Middle : properties.IsRightButtonPressed ? MouseButton.Right : MouseButton.Left;
        if (_button != MouseButton.Left) _gesture = Gesture.Pan;
        else if (session.Tool is EditorTool.Brush or EditorTool.Terrain)
        {
            if (string.IsNullOrWhiteSpace(session.Material))
            {
                CursorStatusChanged?.Invoke("Browse an asset folder and choose a material before creating geometry.");
                e.Handled = true;
                return;
            }
            if (session.Tool == EditorTool.Terrain && !RequireTopView(e)) return;
            _gesture = session.Tool == EditorTool.Brush ? Gesture.Brush : Gesture.Terrain;
        }
        else if (session.Tool == EditorTool.Sculpt)
        {
            if (!RequireTopView(e)) return;
            if (!session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>().Any())
                Select(OrthographicGeometry.HitTest(session, _projection, _startScreen, terrainsOnly: true));
            if (!session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>().Any()) return;
            _gesture = Gesture.Sculpt;
        }
        else if (session.Tool == EditorTool.Clip)
        {
            ClipStarted?.Invoke();
            _gesture = Gesture.Clip;
            _clipDocument = session.Document;
            ClipStart = ClipEnd = _startWorld;
        }
        else BeginSelection(session);
        e.Handled = true;
        if (!IsActive) return;
        _gestureDocument = session.Document;
        _gestureTool = session.Tool;
        _gestureItems = session.Selection.Items.ToArray();
        _pointer = e.Pointer;
        e.Pointer.Capture(_viewport);
        _viewport.Cursor = new Cursor(_gesture == Gesture.Pan ? StandardCursorType.SizeAll : StandardCursorType.Cross);
        if (_gesture == Gesture.Sculpt)
            _changed |= _stroke.Begin(session, _projection.ToWorld(_startScreen), e.KeyModifiers.HasFlag(KeyModifiers.Shift), StartEdit);
        _viewport.InvalidateVisual();
    }

    private bool RequireTopView(PointerPressedEventArgs e)
    {
        if (_projection.Plane == OrthoPlane.Top) return true;
        CursorStatusChanged?.Invoke("Create and sculpt terrain in the Top view.");
        e.Handled = true;
        return false;
    }

    private void BeginSelection(EditorSession session)
    {
        if (!_toggle && _transform.TryBegin(session, _startScreen)) { _gesture = Gesture.Transform; return; }
        object? hit = OrthographicSelection.HitTest(session, _projection, _startScreen);
        if (hit is null && session.Tool == EditorTool.Vertex)
        {
            object? owner = OrthographicGeometry.HitTest(session, _projection, _startScreen);
            if (owner is MapBrush or MapTerrain)
            {
                Select(owner, _toggle, _toggle);
                return;
            }
        }
        if (hit is null)
        {
            _marqueeCandidates = OrthographicSelection.MarqueeCandidates(session);
            _gesture = Gesture.Marquee;
            return;
        }
        if (_toggle || !session.Selection.Contains(hit)) Select(hit, _toggle, _toggle);
        if (!_toggle && session.CanTransformSelection && session.TransformMode == TransformMode.Move &&
            session.Tool is EditorTool.Select or EditorTool.Vertex)
        {
            _transform.Begin(session, _startScreen);
            _gesture = Gesture.Transform;
        }
    }

    internal void PointerMoved(PointerEventArgs e)
    {
        try { Move(e); }
        catch (Exception exception) when (IsInputError(exception)) { Fail(exception); e.Handled = true; }
    }

    private void Move(PointerEventArgs e)
    {
        _cursorScreen = e.GetPosition(_viewport);
        _pointerInside = new Rect(_viewport.Bounds.Size).Contains(_cursorScreen);
        Vector2 world = _projection.ToWorld(_cursorScreen);
        var labels = _projection.Plane switch { OrthoPlane.Top => ("X", "Y"), OrthoPlane.Front => ("X", "Z"), _ => ("Y", "Z") };
        CursorStatusChanged?.Invoke(FormattableString.Invariant($"{labels.Item1}: {world.X:0.##}   {labels.Item2}: {world.Y:0.##}"));
        if (Session is not { } session || !ReferenceEquals(_pointer, e.Pointer))
        {
            if (Session?.Tool == EditorTool.Sculpt) _viewport.InvalidateVisual();
            return;
        }
        if (_gesture == Gesture.Pan) _projection.Pan(_cursorScreen - _lastScreen);
        else if (_gesture == Gesture.Sculpt)
            _changed |= _stroke.Move(session, world, e.KeyModifiers.HasFlag(KeyModifiers.Shift), StartEdit);
        else if (Dragged || _editStarted)
        {
            _currentWorld = Snap(world);
            if (_gesture == Gesture.Transform) _changed = _transform.Apply(session, _cursorScreen, StartEdit);
            else if (_gesture == Gesture.Clip) ClipEnd = _currentWorld;
        }
        _lastScreen = _cursorScreen;
        _viewport.InvalidateVisual();
        e.Handled = true;
    }

    internal void PointerReleased(PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer) || e.InitialPressMouseButton != _button) return;
        try
        {
            // Apply the release position even when the platform coalesces the last move event.
            Move(e);
            if (Session is { } session)
            {
                if (_gesture is Gesture.Brush or Gesture.Terrain && Dragged)
                {
                    Vector2 min = Vector2.Min(_startWorld, _currentWorld), max = Vector2.Max(_startWorld, _currentWorld);
                    if (max.X - min.X >= session.GridSize && max.Y - min.Y >= session.GridSize)
                    {
                        StartEdit();
                        Select(OrthographicCreation.Create(session, _projection, min, max, _gesture == Gesture.Terrain));
                        _changed = true;
                    }
                }
                else if (_gesture == Gesture.Marquee)
                {
                    object[] hits = MarqueeBounds is { } rectangle ?
                        OrthographicSelection.InRectangle(_marqueeCandidates, _projection, rectangle).ToArray() : [];
                    _changingSelection = true;
                    try
                    {
                        session.Selection.SetRange(_toggle ? _selectionBefore : []);
                        foreach (object hit in hits) session.Selection.Set(hit, additive: true, toggle: _toggle);
                        session.Refresh();
                    }
                    finally { _changingSelection = false; }
                }
                else if (_gesture == Gesture.Clip && ClipStart == ClipEnd) _clipDocument = null;
            }
            EndGesture(cancel: false);
        }
        catch (Exception exception) when (IsInputError(exception)) { Fail(exception); }
        e.Handled = true;
    }

    internal bool CommitClip()
    {
        if (!HasClipPreview || Session is not { } session || IsActive) return false;
        try
        {
            Vector2 edge = ClipEnd - ClipStart;
            if (edge.LengthSquared() < 0.0001f) return false;
            Vector3 normal = Vector3.Normalize(_projection.Unproject(new Vector2(-edge.Y, edge.X), 0));
            var plane = new Plane(normal, -Vector3.Dot(normal, _projection.Unproject(ClipStart, 0)));
            int count = SelectionClipping.Apply(session, plane);
            _clipDocument = null;
            CursorStatusChanged?.Invoke($"Clipped {count} brush(es).");
            _viewport.InvalidateVisual();
        }
        catch (Exception exception) when (IsInputError(exception)) { Fail(exception); }
        return true;
    }

    internal void CancelGesture()
    {
        _clipDocument = null;
        EndGesture(cancel: true);
    }

    internal void EndGesture(bool cancel, bool completeEdit = true)
    {
        IPointer? pointer = _pointer;
        bool editing = _editStarted, changed = _changed;
        if (cancel && _gesture == Gesture.Clip) _clipDocument = null;
        _pointer = null;
        _gesture = Gesture.None;
        _gestureDocument = null;
        _gestureItems = _selectionBefore = _marqueeCandidates = [];
        _editStarted = _changed = false;
        _viewport.Cursor = null;
        if (editing && completeEdit && Session is { } session)
        {
            if (cancel) session.CancelEdit();
            else session.CompleteEdit(changed);
        }
        if (ReferenceEquals(pointer?.Captured, _viewport)) pointer.Capture(null);
        _viewport.InvalidateVisual();
    }

    private void Select(object? item, bool additive = false, bool toggle = false)
    {
        _changingSelection = true;
        try { Session?.Select(item, additive, toggle); }
        finally { _changingSelection = false; }
        _gestureItems = Session?.Selection.Items.ToArray() ?? [];
    }

    private void StartEdit()
    {
        if (_editStarted || Session is null) return;
        Session.BeginEdit();
        _editStarted = true;
    }

    private void Fail(Exception exception)
    {
        CancelGesture();
        CursorStatusChanged?.Invoke(exception.Message);
    }

    private static bool IsInputError(Exception exception) => exception is ArgumentException or InvalidOperationException or
        NotSupportedException or FormatException or OverflowException;

    internal void PointerExited()
    {
        _pointerInside = false;
        _viewport.InvalidateVisual();
    }

    private Vector2 Snap(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            throw new ArgumentException("The pointer position exceeds the supported map coordinates.");
        if (Session is not { } session) return point;
        if (!float.IsFinite(session.GridSize) || session.GridSize <= 0)
            throw new ArgumentException("Grid size must be positive and finite.");
        return new(session.Snap(point.X), session.Snap(point.Y));
    }
}
