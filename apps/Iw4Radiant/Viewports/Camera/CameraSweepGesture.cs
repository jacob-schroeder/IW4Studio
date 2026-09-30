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
    private readonly Vector3 _origin, _horizontal, _planeNormal;
    private readonly float _maximumArc;
    private readonly float _startArc, _endpointSign;
    private readonly Point _startPoint;
    private float _pressPitch;
    private bool _editing, _changed;

    private CameraSweepGesture(EditorSession session, CameraNavigation camera, MapEntity entity, MapLight light,
        Point startPoint, int endpointIndex)
    {
        _session = session;
        _camera = camera;
        _document = session.Document;
        _entity = entity;
        _startPoint = startPoint;
        _startArc = light.SweepAngle;
        _endpointSign = endpointIndex == 0 ? -1 : 1;
        _origin = light.Origin;
        float yaw = light.SweepStartAngles.Y * (MathF.PI / 180);
        _horizontal = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        _planeNormal = new Vector3(-_horizontal.Y, _horizontal.X, 0);
        _maximumArc = MathF.Min(120, 180 - light.OuterAngle * (360 / MathF.PI) - 0.01f);
    }

    internal bool IsCurrent => ReferenceEquals(_document, _session.Document) &&
        ReferenceEquals(_session.Selection.Active, _entity);

    internal static bool TryBegin(EditorSession session, CameraNavigation camera, Point point, Size size,
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
        gesture = new CameraSweepGesture(session, camera, entity, light, point, endpointIndex);
        if (!gesture.Intersect(point, size, out Vector3 position))
        {
            gesture = null;
            notice = "Orbit the camera to view the sweep arc from the side.";
        }
        else gesture._pressPitch = gesture.Pitch(position);
        return true;
    }

    internal string? Update(Point point, Size size)
    {
        if (!IsCurrent) throw new InvalidOperationException("The light selection changed during the sweep drag.");
        if (!_editing && ((Avalonia.Vector)(point - _startPoint)).SquaredLength < 9) return null;
        if (!Intersect(point, size, out Vector3 position)) return null;
        if (_maximumArc < 0.1f) return null;
        float pitchDelta = MathF.IEEERemainder(Pitch(position) - _pressPitch, 360);
        float arc = Math.Clamp(_startArc + 2 * _endpointSign * pitchDelta, 0.1f, _maximumArc);
        if (!MapLightProperties.TryRead(_entity, out MapLightProperties properties, out _) ||
            MathF.Abs(arc - properties.SweepAngle) < 0.01f) return null;
        if (!_editing)
        {
            _session.BeginEdit();
            _editing = true;
        }
        (properties with { SweepAngle = arc }).ApplyTo(_entity);
        _changed = true;
        _session.RefreshLightInfluencePreview(_entity);
        return $"Sweep arc: {arc.ToString("0.##", CultureInfo.InvariantCulture)}°";
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

    private float Pitch(Vector3 position)
    {
        float horizontal = Vector3.Dot(position - _origin, _horizontal);
        return MathF.Atan2(-(position.Z - _origin.Z), horizontal) * (180 / MathF.PI);
    }
}
