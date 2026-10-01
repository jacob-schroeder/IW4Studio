using System.Globalization;
using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraSweepGesture
{
    private readonly EditorSession _session;
    private readonly CameraNavigation _camera;
    private readonly MapDocument _document;
    private readonly MapEntity _entity;
    private readonly Vector3 _origin, _aim, _tangent, _planeNormal;
    private readonly bool _rotatePlane;
    private readonly float _maximumArc;
    private readonly float _startArc, _startPlane, _endpointSign;
    private readonly Point _startPoint;
    private float _pressAngle;
    private bool _editing, _changed;

    private CameraSweepGesture(EditorSession session, CameraNavigation camera, MapEntity entity, MapLight light,
        Point startPoint, int endpointIndex, bool rotatePlane)
    {
        _session = session;
        _camera = camera;
        _document = session.Document;
        _entity = entity;
        _startPoint = startPoint;
        _startArc = light.SweepAngle;
        _startPlane = light.SweepPlane;
        _rotatePlane = rotatePlane;
        _endpointSign = endpointIndex == 0 ? -1 : 1;
        _origin = light.Origin;
        _aim = light.Direction;
        _tangent = light.SweepTangent;
        _planeNormal = Vector3.Cross(_aim, _tangent);
        _maximumArc = MathF.Min(120, 180 - light.OuterAngle * (360 / MathF.PI) - 0.01f);
    }

    internal bool IsCurrent => ReferenceEquals(_document, _session.Document) &&
        ReferenceEquals(_session.Selection.Active, _entity);

    internal static bool TryBegin(EditorSession session, CameraNavigation camera, Point point, Size size, bool rotatePlane,
        out CameraSweepGesture? gesture, out string? notice)
    {
        gesture = null;
        notice = null;
        if (session.Selection.Active is not MapEntity { ClassName: "light" } entity ||
            !MapLight.TryCreate(entity, session.Scene.ResolveTargets(entity), out MapLight light, out _) ||
            !light.IsMoving) return false;

        double closestSquared = 100;
        int endpointIndex = -1;
        Vector3[] directions = [light.SweepStartDirection, light.SweepEndDirection];
        for (int index = 0; index < directions.Length; index++)
        {
            Vector3 endpoint = light.Origin + directions[index] * LightInfluenceGeometry.SweepHandleDistance(light);
            if (!CameraPicking.InCubicClip(session, endpoint, camera.Eye) ||
                !CameraPicking.Project(camera, endpoint, size, out Point screen, out _)) continue;
            double distanceSquared = ((Avalonia.Vector)(screen - point)).SquaredLength;
            if (distanceSquared >= closestSquared) continue;
            closestSquared = distanceSquared;
            endpointIndex = index;
        }
        if (endpointIndex < 0) return false;
        gesture = new CameraSweepGesture(session, camera, entity, light, point, endpointIndex, rotatePlane);
        if (rotatePlane) return true;
        if (!gesture.Intersect(point, size, out Vector3 position))
        {
            gesture = null;
            notice = "Orbit the camera to view the sweep arc from the side.";
        }
        else gesture._pressAngle = gesture.Angle(position);
        return true;
    }

    internal string? Update(Point point, Size size)
    {
        if (!IsCurrent) throw new InvalidOperationException("The light selection changed during the sweep drag.");
        if (!_editing && ((Avalonia.Vector)(point - _startPoint)).SquaredLength < 9) return null;
        if (!MapLightProperties.TryRead(_entity, out MapLightProperties properties, out _)) return null;
        MapLightProperties updated;
        string status;
        if (_rotatePlane)
        {
            float plane = MathF.IEEERemainder(_startPlane + (float)(point.X - _startPoint.X), 360);
            if (MathF.Abs(MathF.IEEERemainder(plane - properties.SweepPlane, 360)) < 0.01f) return null;
            updated = properties with { SweepPlane = plane };
            status = $"Sweep plane: {plane.ToString("0.##", CultureInfo.InvariantCulture)}°";
        }
        else
        {
            if (!Intersect(point, size, out Vector3 position) || _maximumArc < 0.1f) return null;
            float angleDelta = MathF.IEEERemainder(Angle(position) - _pressAngle, 360);
            float arc = Math.Clamp(_startArc + 2 * _endpointSign * angleDelta, 0.1f, _maximumArc);
            if (MathF.Abs(arc - properties.SweepAngle) < 0.01f) return null;
            updated = properties with { SweepAngle = arc };
            status = $"Sweep arc: {arc.ToString("0.##", CultureInfo.InvariantCulture)}°";
        }
        if (!_editing)
        {
            _session.BeginEdit();
            _editing = true;
        }
        updated.ApplyTo(_entity);
        _changed = true;
        _session.RefreshLightInfluencePreview(_entity);
        return status;
    }

    internal void Complete(bool cancel)
    {
        if (!_editing || !ReferenceEquals(_document, _session.Document)) return;
        if (cancel) _session.CancelEdit();
        else _session.CompleteEdit(_changed);
    }

    private bool Intersect(Point point, Size size, out Vector3 position)
    {
        position = default;
        if (size.Width <= 0 || size.Height <= 0) return false;
        var ray = _camera.PickRay((float)(point.X / size.Width * 2 - 1),
            (float)(1 - point.Y / size.Height * 2), (float)(size.Width / size.Height));
        float denominator = Vector3.Dot(ray.Direction, _planeNormal);
        if (MathF.Abs(denominator) < 0.05f) return false;
        float distance = Vector3.Dot(_origin - ray.Origin, _planeNormal) / denominator;
        position = ray.Origin + ray.Direction * distance;
        return distance > 0 && float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }

    private float Angle(Vector3 position)
    {
        Vector3 offset = position - _origin;
        return MathF.Atan2(Vector3.Dot(offset, _tangent), Vector3.Dot(offset, _aim)) * (180 / MathF.PI);
    }
}
