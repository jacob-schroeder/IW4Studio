using System.Globalization;
using System.Numerics;
using Avalonia;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraLightConeGesture
{
    private readonly EditorSession _session;
    private readonly CameraNavigation _camera;
    private readonly MapDocument _document;
    private readonly MapEntity _entity;
    private readonly Vector3 _origin, _axis, _radial, _planeNormal;
    private readonly Point _startPoint;
    private readonly float _radius, _startFov, _startOuterFov, _pressAngle;
    private readonly Avalonia.Vector? _projectedSlope;
    private readonly bool _inner;
    private bool _editing, _changed;

    private CameraLightConeGesture(EditorSession session, CameraNavigation camera, MapEntity entity,
        MapLight light, bool inner, Vector3 radial, Point startPoint, float pressAngle,
        Avalonia.Vector? projectedSlope = null)
    {
        _session = session;
        _camera = camera;
        _document = session.Document;
        _entity = entity;
        _origin = light.Origin;
        _radius = light.Radius;
        _axis = light.Direction;
        _radial = radial;
        _planeNormal = Vector3.Normalize(Vector3.Cross(_axis, radial));
        _inner = inner;
        _startPoint = startPoint;
        _startFov = (inner ? light.InnerAngle : light.OuterAngle) * (360 / MathF.PI);
        _startOuterFov = light.OuterAngle * (360 / MathF.PI);
        _pressAngle = pressAngle;
        _projectedSlope = projectedSlope;
    }

    internal bool IsCurrent => ReferenceEquals(_document, _session.Document) &&
        ReferenceEquals(_session.Selection.Active, _entity);
    internal string Label => _inner ? "Inner FOV" : "Outer FOV";

    internal static bool TryBegin(EditorSession session, CameraNavigation camera, Point point, Size size,
        out CameraLightConeGesture? gesture, out string? notice)
    {
        gesture = null;
        notice = null;
        if (session.Selection.Active is not MapEntity { ClassName: "light" } entity ||
            !MapLight.TryCreate(entity, session.Scene.ResolveTargets(entity), out MapLight light, out _) ||
            !light.IsSpotlight) return false;

        double closestSquared = 64;
        float closestDepth = float.PositiveInfinity;
        bool pickedInner = false;
        Vector3 picked = default;
        foreach (bool inner in new[] { false, true })
        {
            if (inner && light.InnerAngle <= 0) continue;
            foreach (var (worldA, worldB) in LightInfluenceGeometry.GetConeRing(light, inner))
            {
                if (!CameraPicking.InCubicClip(session, worldA, camera.Eye) &&
                    !CameraPicking.InCubicClip(session, worldB, camera.Eye)) continue;
                if (!CameraPicking.Project(camera, worldA, size, out Point a, out float depthA) ||
                    !CameraPicking.Project(camera, worldB, size, out Point b, out float depthB)) continue;
                Avalonia.Vector segment = b - a;
                double lengthSquared = segment.SquaredLength;
                if (lengthSquared < 0.01) continue;
                double t = Math.Clamp(((point.X - a.X) * segment.X + (point.Y - a.Y) * segment.Y) /
                    lengthSquared, 0, 1);
                double distanceSquared = ((Avalonia.Vector)(point - (a + segment * t))).SquaredLength;
                float depth = depthA + (depthB - depthA) * (float)t;
                if (distanceSquared > closestSquared ||
                    Math.Abs(distanceSquared - closestSquared) < 0.25 && depth >= closestDepth) continue;
                closestSquared = distanceSquared;
                closestDepth = depth;
                pickedInner = inner;
                picked = Vector3.Lerp(worldA, worldB, (float)t);
            }
        }
        if (light.InnerAngle <= 0)
        {
            Vector3 center = light.Origin + light.Direction * light.Radius;
            if (CameraPicking.InCubicClip(session, center, camera.Eye) &&
                CameraPicking.Project(camera, center, size, out Point screen, out float depth))
            {
                double distanceSquared = ((Avalonia.Vector)(point - screen)).SquaredLength;
                if (distanceSquared < closestSquared ||
                    Math.Abs(distanceSquared - closestSquared) < 0.25 && depth < closestDepth)
                {
                    closestSquared = distanceSquared;
                    closestDepth = depth;
                    pickedInner = true;
                    picked = center;
                }
            }
        }
        if (closestSquared >= 64) return false;

        if (pickedInner && light.InnerAngle <= 0)
        {
            Vector3 markerRadial = camera.Right - Vector3.Dot(camera.Right, light.Direction) * light.Direction;
            if (markerRadial.LengthSquared() < 0.0001f)
            {
                Vector3 screenUp = Vector3.Cross(camera.Right, camera.Forward);
                markerRadial = screenUp - Vector3.Dot(screenUp, light.Direction) * light.Direction;
            }
            if (markerRadial.LengthSquared() < 0.0001f) return false;
            markerRadial = Vector3.Normalize(markerRadial);
            var markerGesture = new CameraLightConeGesture(session, camera, entity, light,
                inner: true, markerRadial, point, 0);
            return BeginWithProjectionFallback(markerGesture, session, camera, entity, light,
                inner: true, markerRadial, point, size, out gesture, out notice);
        }

        Vector3 radial = picked - light.Origin;
        radial -= Vector3.Dot(radial, light.Direction) * light.Direction;
        if (radial.LengthSquared() < 0.0001f) return false;
        radial = Vector3.Normalize(radial);
        var candidate = new CameraLightConeGesture(session, camera, entity, light, pickedInner, radial, point, 0);
        return BeginWithProjectionFallback(candidate, session, camera, entity, light,
            pickedInner, radial, point, size, out gesture, out notice);
    }

    private static bool BeginWithProjectionFallback(CameraLightConeGesture candidate, EditorSession session,
        CameraNavigation camera, MapEntity entity, MapLight light, bool inner, Vector3 radial,
        Point point, Size size, out CameraLightConeGesture? gesture, out string? notice)
    {
        gesture = null;
        notice = null;
        if (candidate.TryAngle(point, size, out float pressAngle))
        {
            gesture = new CameraLightConeGesture(session, camera, entity, light, inner, radial, point, pressAngle);
            return true;
        }
        if (candidate.TryProjectedSlope(size, out Avalonia.Vector slope))
        {
            gesture = new CameraLightConeGesture(session, camera, entity, light, inner, radial, point, 0, slope);
            return true;
        }
        notice = "Orbit the camera to view the cone ring from the side.";
        return true;
    }

    internal string? Update(Point point, Size size)
    {
        if (!IsCurrent) throw new InvalidOperationException("The light selection changed during the cone drag.");
        if (!_editing && ((Avalonia.Vector)(point - _startPoint)).SquaredLength < 9) return null;
        float fov;
        if (_projectedSlope is { } slope)
        {
            Avalonia.Vector delta = point - _startPoint;
            fov = _startFov + (float)((delta.X * slope.X + delta.Y * slope.Y) / slope.SquaredLength);
        }
        else
        {
            if (!TryAngle(point, size, out float angle)) return null;
            fov = _startFov + 2 * MathF.IEEERemainder(angle - _pressAngle, 360);
        }
        if (!MapLightProperties.TryRead(_entity, out MapLightProperties properties, out _)) return null;
        float outer = properties.OuterFov ?? _startOuterFov;
        if (_inner)
        {
            float maximum = outer - MathF.Min(0.01f, outer / 2);
            if (maximum <= 0) return null;
            fov = Math.Clamp(fov, 0, maximum);
        }
        else
        {
            float maximum = properties.SpawnFlags == MapLightDefaults.PrimarySpot || properties.SweepAngle > 0
                ? 180 - properties.SweepAngle - 0.01f : 360;
            if (maximum <= properties.InnerFov) return null;
            fov = Math.Clamp(fov, properties.InnerFov + MathF.Min(0.01f, (maximum - properties.InnerFov) / 2), maximum);
        }
        if (MathF.Abs(fov - (_inner ? properties.InnerFov : outer)) < 0.01f) return null;

        MapLightProperties changed = _inner ? properties with { InnerFov = fov } :
            properties with { OuterFov = fov };
        // Validate a resolved light before touching the selected entity.
        var candidate = new MapEntity();
        foreach (var property in _entity.Properties) candidate.Properties.Add(property.Key, property.Value);
        candidate.Properties[_inner ? "fov_inner" : "fov_outer"] = fov.ToString("R", CultureInfo.InvariantCulture);
        if (!MapLight.TryCreate(candidate, _session.Scene.ResolveTargets(_entity), out _, out _)) return null;

        if (!_editing)
        {
            _session.BeginEdit();
            _editing = true;
        }
        changed.ApplyTo(_entity);
        _changed = true;
        _session.RefreshLightInfluencePreview(_entity);
        return $"{Label}: {fov.ToString("0.##", CultureInfo.InvariantCulture)}°";
    }

    internal void Complete(bool cancel)
    {
        if (!_editing || !ReferenceEquals(_document, _session.Document)) return;
        if (cancel) _session.CancelEdit();
        else _session.CompleteEdit(_changed);
    }

    private bool TryAngle(Point point, Size size, out float angle)
    {
        angle = 0;
        if (size.Width <= 0 || size.Height <= 0) return false;
        var ray = _camera.PickRay((float)(point.X / size.Width * 2 - 1),
            (float)(1 - point.Y / size.Height * 2), (float)(size.Width / size.Height));
        float denominator = Vector3.Dot(ray.Direction, _planeNormal);
        if (MathF.Abs(denominator) < 0.05f) return false;
        float distance = Vector3.Dot(_origin - ray.Origin, _planeNormal) / denominator;
        if (distance <= 0 || !float.IsFinite(distance)) return false;
        Vector3 offset = ray.Origin + ray.Direction * distance - _origin;
        angle = MathF.Atan2(Vector3.Dot(offset, _radial), Vector3.Dot(offset, _axis)) * (180 / MathF.PI);
        return float.IsFinite(angle);
    }

    private bool TryProjectedSlope(Size size, out Avalonia.Vector slope)
    {
        slope = default;
        float low = Math.Max(0, _startFov - 0.5f);
        float high = Math.Min(360, _startFov + 0.5f);
        if (high <= low || !ProjectAtFov(low, size, out Point a) ||
            !ProjectAtFov(high, size, out Point b)) return false;
        slope = (b - a) / (high - low);
        return slope.SquaredLength >= 0.01;
    }

    private bool ProjectAtFov(float fov, Size size, out Point screen)
    {
        float angle = fov * (MathF.PI / 360);
        Vector3 point = _origin + (_axis * MathF.Cos(angle) + _radial * MathF.Sin(angle)) * _radius;
        return CameraPicking.Project(_camera, point, size, out screen, out _);
    }
}
