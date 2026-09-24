using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;
using Vector = Avalonia.Vector;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicTransform
{
    internal const double AxisLength = 44;
    internal const double RotationRadius = 38;
    private readonly OrthographicProjection _projection;
    private Vector3 _pivot;
    private Point _start;
    private (Vector3 Min, Vector3 Max) _bounds;
    private Matrix4x4 _applied = Matrix4x4.Identity;
    private TransformMode _mode;
    private int _axis, _resizeCorner = -1;

    internal OrthographicTransform(OrthographicProjection projection) => _projection = projection;

    internal bool TryBegin(EditorSession session, Point point)
    {
        if (!session.CanTransformSelection || session.SelectionBounds is not { } bounds ||
            session.Tool is not (EditorTool.Select or EditorTool.Vertex)) return false;
        Rect rect = _projection.ScreenBounds(bounds.Min, bounds.Max);
        Point center = rect.Center;
        if (session.Tool == EditorTool.Select && session.TransformMode == TransformMode.Move && session.Selection.Count == 1 && session.Selection.Active is MapBrush)
        {
            Point[] corners = OrthographicGeometry.Corners(rect);
            for (int i = 0; i < corners.Length; i++)
                if (OrthographicGeometry.Distance(point, corners[i]) <= 7)
                {
                    Begin(session, point, 0);
                    _resizeCorner = i;
                    return true;
                }
        }
        if (session.TransformMode == TransformMode.Rotate)
        {
            if (Math.Abs(OrthographicGeometry.Distance(point, center) - RotationRadius) > 6) return false;
            Begin(session, point, 0);
            return true;
        }
        int axis = OrthographicGeometry.Distance(point, center) <= 7 ? 0 :
            OrthographicGeometry.DistanceToSegment(point, center, center + new Vector(AxisLength, 0)) <= 6 ? 1 :
            OrthographicGeometry.DistanceToSegment(point, center, center - new Vector(0, AxisLength)) <= 6 ? 2 : -1;
        if (axis < 0) return false;
        Begin(session, point, axis);
        return true;
    }

    internal void Begin(EditorSession session, Point point, int axis = 0)
    {
        _bounds = session.SelectionBounds ?? throw new InvalidOperationException("The selection has no editable bounds.");
        _pivot = (_bounds.Min + _bounds.Max) / 2;
        _start = point;
        _mode = session.TransformMode;
        _axis = axis;
        _resizeCorner = -1;
        _applied = Matrix4x4.Identity;
    }

    internal bool Apply(EditorSession session, Point point, Action beginEdit)
    {
        if (_resizeCorner >= 0) return Resize(session, point, beginEdit);
        Matrix4x4 transform = GetTransform(session, point);
        if (transform == _applied) return _applied != Matrix4x4.Identity;
        if (!Matrix4x4.Invert(_applied, out Matrix4x4 inverse))
            throw new InvalidOperationException("The previous transform cannot be inverted.");
        beginEdit();
        SelectionTransforms.Apply(session, inverse * transform);
        _applied = transform;
        bool pointEntityMove = _mode == TransformMode.Move && session.Selection.Items.All(item =>
            item is MapEntity entity && PointEntityGeometry.IsPointEntity(entity));
        if (!pointEntityMove || !session.RefreshPointEntityPreview()) session.Refresh();
        return transform != Matrix4x4.Identity;
    }

    private Matrix4x4 GetTransform(EditorSession session, Point point)
    {
        if (_mode == TransformMode.Move)
        {
            Vector2 displacement = _projection.ToWorld(point) - _projection.ToWorld(_start);
            displacement = new(session.Snap(displacement.X), session.Snap(displacement.Y));
            if (_axis == 1) displacement.Y = 0;
            if (_axis == 2) displacement.X = 0;
            Vector3 translation = SelectionTransforms.ApplyAxisLocks(session, _projection.Unproject(displacement, 0));
            return Matrix4x4.CreateTranslation(translation);
        }
        Matrix4x4 linear;
        if (_mode == TransformMode.Rotate)
        {
            Vector2 center = _projection.Project(_pivot);
            Vector2 start = _projection.ToWorld(_start) - center, current = _projection.ToWorld(point) - center;
            if (current.LengthSquared() < 0.0001f) return _applied;
            float angle = MathF.Atan2(current.Y, current.X) - MathF.Atan2(start.Y, start.X);
            float degrees = angle * 180 / MathF.PI;
            if (!float.IsFinite(session.AngleSnap) || session.AngleSnap <= 0)
                throw new ArgumentException("Angle snapping must be positive and finite.");
            degrees = MathF.Round(degrees / session.AngleSnap, MidpointRounding.AwayFromZero) * session.AngleSnap;
            Vector3 axis = Vector3.Cross(_projection.Unproject(Vector2.UnitX, 0), _projection.Unproject(Vector2.UnitY, 0));
            linear = Matrix4x4.CreateFromAxisAngle(axis, degrees * MathF.PI / 180);
        }
        else
        {
            Vector delta = point - _start;
            float factor = 1 + (float)(_axis == 1 ? delta.X : _axis == 2 ? -delta.Y : delta.X - delta.Y) / (float)AxisLength;
            float snap = session.ScaleSnap;
            if (!float.IsFinite(snap) || snap <= 0)
                throw new ArgumentException("Scale snapping must be positive and finite.");
            factor = Math.Max(Math.Min(1, snap), 1 + MathF.Round((factor - 1) / snap, MidpointRounding.AwayFromZero) * snap);
            Vector3 scale = _axis == 0 ? new Vector3(factor) : Vector3.One +
                _projection.Unproject(_axis == 1 ? Vector2.UnitX : Vector2.UnitY, 0) * (factor - 1);
            linear = Matrix4x4.CreateScale(scale);
        }
        return Matrix4x4.CreateTranslation(-_pivot) * linear * Matrix4x4.CreateTranslation(_pivot);
    }

    private bool Resize(EditorSession session, Point point, Action beginEdit)
    {
        if (session.Selection.Active is not MapBrush brush) return false;
        Vector2 cursor = _projection.ToWorld(point);
        cursor = new(session.Snap(cursor.X), session.Snap(cursor.Y));
        Vector2 min = _projection.Project(_bounds.Min), max = _projection.Project(_bounds.Max);
        if (_resizeCorner is 0 or 3) min.X = Math.Min(cursor.X, max.X - session.GridSize);
        else max.X = Math.Max(cursor.X, min.X + session.GridSize);
        if (_resizeCorner is 0 or 1) max.Y = Math.Max(cursor.Y, min.Y + session.GridSize);
        else min.Y = Math.Min(cursor.Y, max.Y - session.GridSize);
        Vector3 minimum = _projection.Unproject(min, _projection.MissingAxis(_bounds.Min));
        Vector3 maximum = _projection.Unproject(max, _projection.MissingAxis(_bounds.Max));
        var current = brush.GetBounds();
        if (Vector3.DistanceSquared(minimum, current.Min) > 0.00001f || Vector3.DistanceSquared(maximum, current.Max) > 0.00001f)
        {
            beginEdit();
            brush.Resize(minimum, maximum, session.TextureLock);
            session.Refresh();
        }
        return Vector3.DistanceSquared(minimum, _bounds.Min) > 0.00001f || Vector3.DistanceSquared(maximum, _bounds.Max) > 0.00001f;
    }
}
