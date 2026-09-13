using System.Globalization;
using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraTransformGesture
{
    private readonly EditorSession _session;
    private readonly MapDocument _document;
    private readonly object[] _selection;
    private readonly CameraNavigation _camera;
    private readonly TransformMode _mode;
    private readonly int _axis;
    private readonly Vector3 _direction, _pivot, _planeNormal, _startWorld;
    private readonly Point _startPoint;
    private readonly float _size;
    private Vector3 _lastRotationDirection;
    private float _rotation;
    private Matrix4x4 _applied = Matrix4x4.Identity;

    internal CameraTransformGesture(EditorSession session, CameraNavigation camera, int axis, Point point, Size size)
    {
        if (session.SelectionBounds is not { } bounds || !session.CanTransformSelection || axis is < 1 or > 4 ||
            axis == 4 && session.TransformMode != TransformMode.Scale)
            throw new ArgumentException("Select editable geometry before dragging a transform handle.");
        _session = session;
        _document = session.Document;
        _selection = session.Selection.Items.ToArray();
        _camera = camera;
        _mode = session.TransformMode;
        _axis = axis;
        _direction = axis == 1 ? Vector3.UnitX : axis == 2 ? Vector3.UnitY : Vector3.UnitZ;
        _pivot = bounds.Min + (bounds.Max - bounds.Min) / 2;
        _size = TransformGizmoGeometry.GetSize(bounds);
        _startPoint = point;
        if (axis != 4)
        {
            Vector3 forward = camera.PickRay(0, 0, (float)(size.Width / size.Height)).Direction;
            Vector3 planeNormal = _mode == TransformMode.Rotate ? _direction :
                forward - _direction * Vector3.Dot(forward, _direction);
            if (planeNormal.LengthSquared() < 0.000001f)
                throw new ArgumentException("Orbit the camera to view this transform axis from the side.");
            _planeNormal = Vector3.Normalize(planeNormal);
            _startWorld = Intersect(point, size);
            _lastRotationDirection = Vector3.Normalize(_startWorld - _pivot);
            if (_mode == TransformMode.Rotate && !BrushGeometry.IsFinite(_lastRotationDirection))
                throw new ArgumentException("Drag a point on the rotation ring.");
        }
        session.BeginEdit();
    }

    internal bool HasChanges => _applied != Matrix4x4.Identity;
    internal bool OwnsDocument => ReferenceEquals(_document, _session.Document);
    internal bool IsCurrent => OwnsDocument && _selection.SequenceEqual(_session.Selection.Items);

    internal string? Update(Point point, Size size)
    {
        if (!IsCurrent)
            throw new InvalidOperationException("The selection changed during the transform.");
        Matrix4x4 target;
        float amount;
        string units;
        if (_mode == TransformMode.Rotate)
        {
            Vector3 direction = Vector3.Normalize(Intersect(point, size) - _pivot);
            if (!BrushGeometry.IsFinite(direction)) return null;
            _rotation += MathF.Atan2(Vector3.Dot(_direction, Vector3.Cross(_lastRotationDirection, direction)),
                Math.Clamp(Vector3.Dot(_lastRotationDirection, direction), -1, 1));
            _lastRotationDirection = direction;
            amount = Snap(_rotation * (180 / MathF.PI), _session.AngleSnap);
            target = AroundPivot(Matrix4x4.CreateFromAxisAngle(_direction, amount * (MathF.PI / 180)));
            units = "°";
        }
        else
        {
            float distance = _axis == 4 ? (float)(_startPoint.Y - point.Y) / 100 :
                Vector3.Dot(Intersect(point, size) - _startWorld, _direction);
            if (_mode == TransformMode.Move)
            {
                if (!float.IsFinite(_session.GridSize) || _session.GridSize <= 0)
                    throw new ArgumentException("Grid size must be positive and finite.");
                amount = _session.Snap(distance);
                target = Matrix4x4.CreateTranslation(_direction * amount);
                units = " units";
            }
            else
            {
                amount = Math.Max(Math.Min(1, _session.ScaleSnap), 1 + Snap(_axis == 4 ? distance : distance / _size, _session.ScaleSnap));
                Vector3 scale = _axis == 4 ? new Vector3(amount) : Vector3.One + _direction * (amount - 1);
                target = AroundPivot(Matrix4x4.CreateScale(scale));
                units = "×";
            }
        }
        if (!float.IsFinite(amount))
            throw new ArgumentException("The transform exceeds the supported numeric range.");
        if (target == _applied) return null;
        if (!Matrix4x4.Invert(_applied, out Matrix4x4 inverse))
            throw new ArgumentException("The transform would collapse the selection.");
        SelectionTransforms.Apply(_session, inverse * target);
        _applied = target;
        _session.Refresh();
        string axisName = _axis == 1 ? "X" : _axis == 2 ? "Y" : _axis == 3 ? "Z" : "uniform";
        return $"{_mode} {axisName}: {amount.ToString("0.###", CultureInfo.InvariantCulture)}{units}";
    }

    internal void Complete(bool cancel)
    {
        if (!OwnsDocument) return;
        if (cancel) _session.CancelEdit();
        else _session.CompleteEdit(HasChanges);
    }

    private Vector3 Intersect(Point point, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new ArgumentException("The camera viewport is too small for a transform.");
        var ray = _camera.PickRay((float)(point.X / size.Width * 2 - 1), (float)(1 - point.Y / size.Height * 2),
            (float)(size.Width / size.Height));
        float denominator = Vector3.Dot(ray.Direction, _planeNormal);
        if (MathF.Abs(denominator) < 0.000001f)
            throw new ArgumentException("Orbit the camera to view this transform handle more clearly.");
        float distance = Vector3.Dot(_pivot - ray.Origin, _planeNormal) / denominator;
        Vector3 result = ray.Origin + ray.Direction * distance;
        if (distance <= 0 || !BrushGeometry.IsFinite(result))
            throw new ArgumentException("The transform handle is behind the camera.");
        return result;
    }

    private Matrix4x4 AroundPivot(Matrix4x4 transform) =>
        Matrix4x4.CreateTranslation(-_pivot) * transform * Matrix4x4.CreateTranslation(_pivot);

    private static float Snap(float value, float increment)
    {
        if (!float.IsFinite(increment) || increment <= 0)
            throw new ArgumentException("Transform snapping must be positive and finite.");
        return MathF.Round(value / increment, MidpointRounding.AwayFromZero) * increment;
    }
}
