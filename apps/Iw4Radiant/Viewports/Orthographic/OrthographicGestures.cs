using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicGestures
{
    private enum Gesture { None, Pan, Transform, Marquee, PaintSelection, Brush, Terrain, Sculpt, Clip }
    private readonly Control _viewport;
    private readonly OrthographicProjection _projection;
    private readonly OrthographicTransform _transform;
    private readonly OrthographicTerrainStroke _stroke = new();
    private readonly HashSet<object> _paintVisited = [];
    private IPointer? _pointer;
    private MouseButton _button;
    private Gesture _gesture;
    private MapDocument? _gestureDocument, _clipDocument;
    private object[] _gestureItems = [], _selectionBefore = [], _marqueeCandidates = [];
    private EditorTool _gestureTool;
    private Point _startScreen, _lastScreen, _cursorScreen;
    private Vector2 _startWorld, _currentWorld;
    private bool _editStarted, _changed, _pointerInside, _changingSelection, _toggle, _paintSelecting, _panDragged;
    private SelectionVolumeMode _selectionVolumeMode;

    internal OrthographicGestures(Control viewport, OrthographicProjection projection)
    {
        _viewport = viewport;
        _projection = projection;
        _transform = new(projection);
    }

    internal EditorSession? Session { get; set; }
    internal event Action<string>? CursorStatusChanged;
    internal event Action? ClipStarted;
    internal event Action? ClipPreviewChanged;
    internal event Action<Point, Vector3>? ContextMenuRequested;
    internal bool IsActive => _gesture != Gesture.None;
    internal bool IsCreating => _gesture is Gesture.Brush or Gesture.Terrain;
    internal bool IsCreatingTerrain => _gesture == Gesture.Terrain;
    internal bool PointerInside => _pointerInside;
    internal Point CursorScreen => _cursorScreen;
    internal Vector2 StartWorld => _startWorld;
    internal Vector2 CurrentWorld => _currentWorld;
    internal Rect? MarqueeBounds => _gesture == Gesture.Marquee && Dragged ? OrthographicGeometry.Rectangle(_startScreen, _cursorScreen) : null;
    internal bool HasClipPreview => _clipDocument is not null;
    internal bool CanCommitClip => HasClipPreview && !IsActive && (ClipEnd - ClipStart).LengthSquared() >= 0.0001f;
    internal Vector2 ClipStart { get; private set; }
    internal Vector2 ClipEnd { get; private set; }
    private bool Dragged => OrthographicGeometry.Distance(_cursorScreen, _startScreen) >= 3;

    internal void SessionChanged()
    {
        if (Session is { } session)
        {
            if (_clipDocument is not null && (!ReferenceEquals(_clipDocument, session.Document) || session.Tool != EditorTool.Clip))
                ClearClipPreview();
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
        _panDragged = false;
        _selectionBefore = session.Selection.Items.ToArray();
        _toggle = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _button = properties.IsMiddleButtonPressed ? MouseButton.Middle : properties.IsRightButtonPressed ? MouseButton.Right : MouseButton.Left;
        if (_button == MouseButton.Left && session.HasPlacement)
        {
            string? label = session.PlacementLabel;
            session.Place(_projection.Unproject(_startWorld, session.Snap(session.BrushBottom)), null,
                (e.KeyModifiers & KeyModifiers.Shift) != 0);
            CursorStatusChanged?.Invoke($"Placed {label}." + (session.HasPlacement ? " Click to place another; Esc cancels." : ""));
            e.Handled = true;
            return;
        }
        if (_button != MouseButton.Left) _gesture = Gesture.Pan;
        else if (_toggle && session.Tool is EditorTool.Select or EditorTool.Terrain or EditorTool.Clip)
        {
            object? hit = OrthographicGeometry.HitTest(session, _projection, _startScreen);
            _gesture = Gesture.PaintSelection;
            _paintSelecting = hit is null || !session.Selection.Contains(hit);
            if (hit is not null) PaintSelection([hit]);
        }
        else if (session.Tool == EditorTool.Select && session.SelectionVolumeMode != SelectionVolumeMode.None &&
                 e.KeyModifiers == KeyModifiers.None)
        {
            _selectionVolumeMode = session.SelectionVolumeMode;
            _marqueeCandidates = OrthographicSelection.MarqueeCandidates(session);
            _gesture = Gesture.Marquee;
        }
        else if (session.Tool == EditorTool.Terrain || session.Tool == EditorTool.Select &&
                 session.Selection.Count == 0 && e.KeyModifiers == KeyModifiers.None)
        {
            if (string.IsNullOrWhiteSpace(session.Material))
            {
                CursorStatusChanged?.Invoke("Browse an asset folder and choose a material before creating geometry.");
                e.Handled = true;
                return;
            }
            if (session.Tool == EditorTool.Terrain && !RequireTopView(e)) return;
            _gesture = session.Tool == EditorTool.Terrain ? Gesture.Terrain : Gesture.Brush;
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
        if (HasClipPreview) ClipPreviewChanged?.Invoke();
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
        if (session.Tool == EditorTool.Vertex &&
            OrthographicSelection.HitTestVertexHandle(session, _projection, _startScreen) is { } vertexHandle)
        {
            SelectVertices(vertexHandle.Vertices, toggle: _toggle);
            if (!_toggle && session.CanTransformSelection && session.TransformMode == TransformMode.Move)
            {
                _transform.Begin(session, _startScreen);
                _gesture = Gesture.Transform;
            }
            return;
        }
        if (!_toggle && _transform.TryBegin(session, _startScreen)) { _gesture = Gesture.Transform; return; }
        object? hit = OrthographicSelection.HitTest(session, _projection, _startScreen);
        if (!_toggle)
        {
            if (session.Tool == EditorTool.Face && hit is BrushFaceSelection)
            {
                Select(hit);
                return;
            }
            if (hit is not null && session.Selection.Contains(hit) && session.CanTransformSelection &&
                session.TransformMode == TransformMode.Move && session.Tool is EditorTool.Select or EditorTool.Vertex)
            {
                _transform.Begin(session, _startScreen);
                _gesture = Gesture.Transform;
            }
            return;
        }
        if (hit is null && session.Tool == EditorTool.Vertex)
        {
            object? owner = OrthographicGeometry.HitTest(session, _projection, _startScreen);
            if (owner is MapBrush or MapTerrain)
            {
                Select(owner, additive: true, toggle: true);
                return;
            }
        }
        if (hit is null)
        {
            _marqueeCandidates = OrthographicSelection.MarqueeCandidates(session);
            _gesture = Gesture.Marquee;
            return;
        }
        Select(hit, additive: true, toggle: true);
    }

    private void SelectVertices(IReadOnlyList<object> vertices, bool toggle)
    {
        if (Session is not { } session) return;
        _changingSelection = true;
        try
        {
            if (!toggle)
            {
                if (vertices.Any(vertex => !session.Selection.Contains(vertex)))
                    session.Selection.SetRange(vertices);
            }
            else
            {
                bool select = vertices.Any(vertex => !session.Selection.Contains(vertex));
                foreach (object vertex in vertices)
                    if (session.Selection.Contains(vertex) != select)
                        session.Selection.Set(vertex, additive: true, toggle: true);
            }
            session.Refresh();
        }
        finally { _changingSelection = false; }
        _gestureItems = session.Selection.Items.ToArray();
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
        CursorStatusChanged?.Invoke((Session?.HasPlacement == true ? $"Place {Session.PlacementLabel} · click, Shift to repeat, Esc to cancel · " : "") +
            FormattableString.Invariant($"{labels.Item1}: {world.X:0.##}   {labels.Item2}: {world.Y:0.##}"));
        if (Session is not { } session || !ReferenceEquals(_pointer, e.Pointer))
        {
            if (Session?.Tool == EditorTool.Sculpt) _viewport.InvalidateVisual();
            return;
        }
        if (_gesture == Gesture.Pan)
        {
            if (_button != MouseButton.Right || _panDragged)
                _projection.Pan(_cursorScreen - _lastScreen);
            else if (Dragged)
            {
                _panDragged = true;
                _projection.Pan(_cursorScreen - _startScreen);
            }
        }
        else if (_gesture == Gesture.PaintSelection && Dragged)
            PaintSelection(OrthographicGeometry.HitTestSegment(session, _projection, _lastScreen, _cursorScreen).ToArray());
        else if (_gesture == Gesture.Sculpt)
            _changed |= _stroke.Move(session, world, e.KeyModifiers.HasFlag(KeyModifiers.Shift), StartEdit);
        else if (Dragged || _editStarted)
        {
            _currentWorld = Snap(world);
            if (_gesture == Gesture.Transform)
            {
                _changed = _transform.Apply(session, _cursorScreen, StartEdit);
                // A changed transform already invalidates the view through the session.
                // Pointer events inside the same snapped cell need no grid redraw.
                _lastScreen = _cursorScreen;
                e.Handled = true;
                return;
            }
            else if (_gesture == Gesture.Clip) ClipEnd = _currentWorld;
        }
        _lastScreen = _cursorScreen;
        _viewport.InvalidateVisual();
        e.Handled = true;
    }

    internal void PointerReleased(PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer) || e.InitialPressMouseButton != _button) return;
        bool showContextMenu = false;
        Point menuPoint = _startScreen;
        Vector3 menuPosition = Session is { } current
            ? _projection.Unproject(_startWorld, current.Snap(current.BrushBottom))
            : default;
        try
        {
            // Apply the release position even when the platform coalesces the last move event.
            Move(e);
            showContextMenu = _gesture == Gesture.Pan && _button == MouseButton.Right && !_panDragged;
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
                        OrthographicSelection.InRectangle(session, _marqueeCandidates, _projection, rectangle,
                            _selectionVolumeMode == SelectionVolumeMode.None ? SelectionVolumeMode.PartialTall : _selectionVolumeMode).ToArray() : [];
                    _changingSelection = true;
                    try
                    {
                        if (_selectionVolumeMode == SelectionVolumeMode.None)
                        {
                            session.Selection.SetRange(_selectionBefore);
                            foreach (object hit in hits) session.Selection.Set(hit, additive: true, toggle: true);
                        }
                        else session.Selection.SetRange(hits);
                        session.SelectionVolumeMode = SelectionVolumeMode.None;
                        session.Refresh();
                    }
                    finally { _changingSelection = false; }
                }
                else if (_gesture == Gesture.Clip && ClipStart == ClipEnd) ClearClipPreview();
            }
            EndGesture(cancel: false);
            if (showContextMenu && Session is not null) ContextMenuRequested?.Invoke(menuPoint, menuPosition);
        }
        catch (Exception exception) when (IsInputError(exception)) { Fail(exception); }
        e.Handled = true;
    }

    internal bool CommitClip()
    {
        if (!CanCommitClip || Session is not { } session) return false;
        try
        {
            Vector2 edge = ClipEnd - ClipStart;
            Vector3 normal = Vector3.Normalize(_projection.Unproject(new Vector2(-edge.Y, edge.X), 0));
            var plane = new Plane(normal, -Vector3.Dot(normal, _projection.Unproject(ClipStart, 0)));
            int count = SelectionClipping.Apply(session, plane);
            ClearClipPreview();
            CursorStatusChanged?.Invoke($"Clipped {count} brush(es).");
            _viewport.InvalidateVisual();
        }
        catch (Exception exception) when (IsInputError(exception)) { Fail(exception); }
        return true;
    }

    internal void CancelGesture()
    {
        ClearClipPreview();
        EndGesture(cancel: true);
    }

    private void ClearClipPreview()
    {
        if (_clipDocument is null) return;
        _clipDocument = null;
        ClipPreviewChanged?.Invoke();
    }

    internal void EndGesture(bool cancel, bool completeEdit = true)
    {
        IPointer? pointer = _pointer;
        bool transformPreview = _gesture == Gesture.Transform && _editStarted;
        bool editing = _editStarted, changed = _changed;
        bool clearSelectionVolume = _selectionVolumeMode != SelectionVolumeMode.None &&
            Session?.SelectionVolumeMode != SelectionVolumeMode.None;
        object[]? restoreSelection = cancel && completeEdit && _gesture == Gesture.PaintSelection &&
            ReferenceEquals(_gestureDocument, Session?.Document) ? _selectionBefore : null;
        if (cancel && _gesture == Gesture.Clip) ClearClipPreview();
        _pointer = null;
        _gesture = Gesture.None;
        _gestureDocument = null;
        _gestureItems = _selectionBefore = _marqueeCandidates = [];
        _selectionVolumeMode = SelectionVolumeMode.None;
        _paintVisited.Clear();
        _editStarted = _changed = false;
        _viewport.Cursor = null;
        if (transformPreview) Session?.EndTransformPreview();
        if (editing && completeEdit && Session is { } session)
        {
            if (cancel) session.CancelEdit();
            else session.CompleteEdit(changed);
        }
        if (clearSelectionVolume && Session is { } current)
        {
            current.SelectionVolumeMode = SelectionVolumeMode.None;
            current.Refresh();
        }
        if (restoreSelection is not null) Session?.SelectRange(restoreSelection);
        if (ReferenceEquals(pointer?.Captured, _viewport)) pointer.Capture(null);
        if (HasClipPreview) ClipPreviewChanged?.Invoke();
        _viewport.InvalidateVisual();
    }

    private void PaintSelection(IEnumerable<object> hits)
    {
        if (Session is not { } session) return;
        bool changed = false;
        _changingSelection = true;
        try
        {
            foreach (object hit in hits)
            {
                if (!_paintVisited.Add(hit) || session.Selection.Contains(hit) == _paintSelecting) continue;
                session.Selection.Set(hit, additive: true, toggle: !_paintSelecting);
                changed = true;
            }
            if (changed) session.Refresh();
        }
        finally { _changingSelection = false; }
        _gestureItems = session.Selection.Items.ToArray();
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
        if (_gesture == Gesture.Transform) Session.BeginTransformPreview();
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
