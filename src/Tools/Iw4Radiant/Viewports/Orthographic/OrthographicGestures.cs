using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Vector = Avalonia.Vector;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicGestures
{
    private enum Gesture { None, Pan, Move, Resize, Brush, Terrain, Sculpt }

    private readonly Control _viewport;
    private readonly OrthographicProjection _projection;
    private IPointer? _pointer;
    private MouseButton _button;
    private Gesture _gesture;
    private MapDocument? _gestureDocument;
    private object? _gestureSelection;
    private Point _startScreen, _lastScreen, _cursorScreen;
    private Vector2 _startWorld, _currentWorld, _lastSculptWorld;
    private Vector3 _translation;
    private (Vector3 Min, Vector3 Max) _originalBounds;
    private int _resizeCorner, _moveAxis;
    private bool _editStarted, _changed, _pointerInside;

    internal OrthographicGestures(Control viewport, OrthographicProjection projection)
    {
        _viewport = viewport;
        _projection = projection;
    }

    internal EditorSession? Session { get; set; }
    internal event Action<string>? CursorStatusChanged;
    internal bool IsActive => _gesture != Gesture.None;
    internal bool IsCreating => _gesture is Gesture.Brush or Gesture.Terrain;
    internal bool IsCreatingTerrain => _gesture == Gesture.Terrain;
    internal bool PointerInside => _pointerInside;
    internal Point CursorScreen => _cursorScreen;
    internal Vector2 StartWorld => _startWorld;
    internal Vector2 CurrentWorld => _currentWorld;

    internal void SessionChanged()
    {
        // Opening/undoing from another view invalidates the objects held by a gesture.
        if (_gesture != Gesture.None && Session is { } session)
        {
            if (!ReferenceEquals(_gestureDocument, session.Document))
                EndGesture(cancel: false, completeEdit: false);
            else if (_gestureSelection is not null && !ReferenceEquals(_gestureSelection, session.Selection))
                EndGesture(cancel: true);
        }
        _viewport.InvalidateVisual();
    }

    internal void PointerPressed(PointerPressedEventArgs e)
    {
        if (Session is not { } session || _gesture != Gesture.None) return;
        PointerPointProperties properties = e.GetCurrentPoint(_viewport).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed && !properties.IsRightButtonPressed) return;
        _viewport.Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _startScreen = _lastScreen = _cursorScreen = e.GetPosition(_viewport);
        _startWorld = _currentWorld = Snap(_projection.ToWorld(_startScreen));
        _translation = Vector3.Zero;
        _editStarted = _changed = false;
        _moveAxis = 0;
        _button = properties.IsMiddleButtonPressed ? MouseButton.Middle :
            properties.IsRightButtonPressed ? MouseButton.Right : MouseButton.Left;
        if (_button == MouseButton.Left && session.Tool is EditorTool.Brush or EditorTool.Terrain &&
            string.IsNullOrWhiteSpace(session.Material))
        {
            CursorStatusChanged?.Invoke("Browse an asset folder and choose a material before creating geometry.");
            e.Handled = true;
            return;
        }
        if (_button != MouseButton.Left)
            _gesture = Gesture.Pan;
        else if (session.Tool == EditorTool.Brush)
            _gesture = Gesture.Brush;
        else if (session.Tool == EditorTool.Terrain)
        {
            if (_projection.Plane != OrthoPlane.Top)
            {
                CursorStatusChanged?.Invoke("Create terrain in the Top view by dragging an XY rectangle.");
                e.Handled = true;
                return;
            }
            _gesture = Gesture.Terrain;
        }
        else if (session.Tool == EditorTool.Sculpt)
        {
            if (_projection.Plane != OrthoPlane.Top)
            {
                CursorStatusChanged?.Invoke("Sculpt terrain in the Top view; Shift lowers the surface.");
                e.Handled = true;
                return;
            }
            if (session.Selection is not MapTerrain)
                session.Select(OrthographicGeometry.HitTest(session, _projection, _startScreen, terrainsOnly: true));
            if (session.Selection is not MapTerrain) return;
            _gesture = Gesture.Sculpt;
            _lastSculptWorld = _projection.ToWorld(_startScreen);
        }
        else
            BeginSelectionGesture(_startScreen);
        if (_gesture == Gesture.None) return;
        _gestureDocument = session.Document;
        _gestureSelection = session.Selection;
        _pointer = e.Pointer;
        e.Pointer.Capture(_viewport);
        _viewport.Cursor = new Cursor(_gesture == Gesture.Pan ? StandardCursorType.SizeAll : StandardCursorType.Cross);
        if (_gesture == Gesture.Sculpt) SculptAt(_lastSculptWorld, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _viewport.InvalidateVisual();
        e.Handled = true;
    }

    private void BeginSelectionGesture(Point point)
    {
        if (Session is not { } session) return;
        if (session.CanTransformSelection && session.SelectionBounds is { } bounds)
        {
            Rect rect = _projection.ScreenBounds(bounds.Min, bounds.Max);
            if (session.Selection is MapBrush)
            {
                Point[] corners = OrthographicGeometry.Corners(rect);
                for (int i = 0; i < corners.Length; i++)
                    if (OrthographicGeometry.Distance(point, corners[i]) <= 7)
                    {
                        _resizeCorner = i;
                        _originalBounds = bounds;
                        _gesture = Gesture.Resize;
                        return;
                    }
            }
            Point center = rect.Center;
            if (OrthographicGeometry.Distance(point, center) <= 7)
            {
                _gesture = Gesture.Move;
                return;
            }
            if (OrthographicGeometry.DistanceToSegment(point, center, center + new Vector(44, 0)) <= 6)
                _moveAxis = 1;
            else if (OrthographicGeometry.DistanceToSegment(point, center, center - new Vector(0, 44)) <= 6)
                _moveAxis = 2;
            if (_moveAxis != 0)
            {
                _gesture = Gesture.Move;
                return;
            }
        }
        session.Select(OrthographicGeometry.HitTest(session, _projection, point));
        if (session.CanTransformSelection) _gesture = Gesture.Move;
    }

    internal void PointerMoved(PointerEventArgs e)
    {
        _cursorScreen = e.GetPosition(_viewport);
        _pointerInside = new Rect(_viewport.Bounds.Size).Contains(_cursorScreen);
        Vector2 world = _projection.ToWorld(_cursorScreen);
        var labels = _projection.Plane switch
        {
            OrthoPlane.Top => ("X", "Y"), OrthoPlane.Front => ("X", "Z"), _ => ("Y", "Z")
        };
        CursorStatusChanged?.Invoke(FormattableString.Invariant($"{labels.Item1}: {world.X:0.##}   {labels.Item2}: {world.Y:0.##}"));
        if (Session is not { } session || !ReferenceEquals(_pointer, e.Pointer))
        {
            if (Session?.Tool == EditorTool.Sculpt) _viewport.InvalidateVisual();
            return;
        }
        if (_gesture == Gesture.Pan)
        {
            _projection.Pan(_cursorScreen - _lastScreen);
        }
        else if (_gesture == Gesture.Sculpt)
        {
            float spacing = Math.Max(1, session.SculptRadius * 0.15f);
            float distance = Vector2.Distance(_lastSculptWorld, world);
            if (distance >= spacing)
            {
                Vector2 direction = Vector2.Normalize(world - _lastSculptWorld);
                int stamps = Math.Min(256, (int)(distance / spacing));
                for (int i = 0; i < stamps; i++)
                {
                    _lastSculptWorld += direction * spacing;
                    SculptAt(_lastSculptWorld, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }
            }
        }
        else if (OrthographicGeometry.Distance(_cursorScreen, _startScreen) >= 3 || _editStarted)
        {
            _currentWorld = Snap(world);
            if (_gesture == Gesture.Move)
            {
                Vector2 displacement = Snap(world - _projection.ToWorld(_startScreen));
                if (_moveAxis == 1) displacement.Y = 0;
                if (_moveAxis == 2) displacement.X = 0;
                Vector3 translation = _projection.Unproject(displacement, 0);
                Vector3 delta = translation - _translation;
                if (delta != Vector3.Zero)
                {
                    StartEdit();
                    session.TranslateSelection(delta);
                    _translation = translation;
                    _changed = translation != Vector3.Zero;
                    session.Refresh();
                }
            }
            else if (_gesture == Gesture.Resize) ResizeSelection();
        }
        _lastScreen = _cursorScreen;
        _viewport.InvalidateVisual();
        e.Handled = true;
    }

    private void ResizeSelection()
    {
        if (Session?.Selection is not MapBrush brush) return;
        Vector2 min = _projection.Project(_originalBounds.Min), max = _projection.Project(_originalBounds.Max);
        float grid = Session.GridSize;
        if (_resizeCorner is 0 or 3) min.X = Math.Min(_currentWorld.X, max.X - grid);
        else max.X = Math.Max(_currentWorld.X, min.X + grid);
        if (_resizeCorner is 0 or 1) max.Y = Math.Max(_currentWorld.Y, min.Y + grid);
        else min.Y = Math.Min(_currentWorld.Y, max.Y - grid);
        Vector3 minimum = _projection.Unproject(min, _projection.MissingAxis(_originalBounds.Min));
        Vector3 maximum = _projection.Unproject(max, _projection.MissingAxis(_originalBounds.Max));
        var current = brush.GetBounds();
        if (Vector3.DistanceSquared(minimum, current.Min) < 0.00001f &&
            Vector3.DistanceSquared(maximum, current.Max) < 0.00001f) return;
        StartEdit();
        brush.Resize(minimum, maximum);
        _changed = Vector3.DistanceSquared(minimum, _originalBounds.Min) > 0.00001f ||
            Vector3.DistanceSquared(maximum, _originalBounds.Max) > 0.00001f;
        Session.Refresh();
    }

    private void SculptAt(Vector2 world, bool lower)
    {
        if (Session?.Selection is not MapTerrain terrain || Session.SculptRadius <= 0) return;
        bool changed = false;
        for (int i = 0; i < terrain.Vertices.Length; i++)
        {
            Vector3 vertex = terrain.Vertices[i];
            float distance = Vector2.Distance(new Vector2(vertex.X, vertex.Y), world);
            if (distance >= Session.SculptRadius) continue;
            float falloff = 1 - distance / Session.SculptRadius;
            float height = vertex.Z + Session.SculptStrength * falloff * falloff * (lower ? -1 : 1);
            if (!float.IsFinite(height) || height == vertex.Z) continue;
            StartEdit();
            terrain.Vertices[i].Z = height;
            changed = _changed = true;
        }
        if (changed) Session.Refresh();
    }

    private void StartEdit()
    {
        if (_editStarted || Session is null) return;
        Session.BeginEdit();
        _editStarted = true;
    }

    internal void PointerReleased(PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer) || e.InitialPressMouseButton != _button) return;
        if (_gesture is Gesture.Brush or Gesture.Terrain && Session is { } session &&
            OrthographicGeometry.Distance(e.GetPosition(_viewport), _startScreen) >= 3)
        {
            _currentWorld = Snap(_projection.ToWorld(e.GetPosition(_viewport)));
            Vector2 min = Vector2.Min(_startWorld, _currentWorld), max = Vector2.Max(_startWorld, _currentWorld);
            if (max.X - min.X >= session.GridSize && max.Y - min.Y >= session.GridSize)
            {
                StartEdit();
                if (_gesture == Gesture.Brush)
                {
                    float bottom = session.Snap(session.BrushBottom);
                    float top = Math.Max(bottom + session.GridSize, session.Snap(bottom + session.BrushHeight));
                    var brush = MapBrush.CreateBox(_projection.Unproject(min, bottom), _projection.Unproject(max, top), session.Material);
                    session.Document.World.Brushes.Add(brush);
                    _gestureSelection = brush;
                    session.Select(brush);
                }
                else
                {
                    int count = Math.Clamp(session.TerrainVertices, 2, 16);
                    float spacing = (max.X - min.X) / (count - 1);
                    var terrain = MapTerrain.Create(new Vector3(min.X, min.Y, session.BrushBottom), spacing, count, session.Material);
                    for (int x = 0; x < count; x++)
                    for (int y = 0; y < count; y++)
                    {
                        int index = x * count + y;
                        float offset = y * (max.Y - min.Y) / (count - 1);
                        terrain.Vertices[index].Y = min.Y + offset;
                        terrain.TextureCoordinates[index].Y = offset / 128;
                    }
                    session.Document.World.Terrains.Add(terrain);
                    _gestureSelection = terrain;
                    session.Select(terrain);
                }
                _changed = true;
            }
        }
        EndGesture(cancel: false);
        e.Handled = true;
    }

    internal void EndGesture(bool cancel, bool completeEdit = true)
    {
        IPointer? pointer = _pointer;
        bool editing = _editStarted;
        bool changed = _changed;
        _pointer = null;
        _gesture = Gesture.None;
        _gestureDocument = null;
        _gestureSelection = null;
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

    internal void PointerExited()
    {
        _pointerInside = false;
        _viewport.InvalidateVisual();
    }

    private Vector2 Snap(Vector2 point) => Session is { } session ? new(session.Snap(point.X), session.Snap(point.Y)) : point;
}
