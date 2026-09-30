using System.Numerics;

namespace Iw4Radiant.MapSource;

internal readonly record struct MapLight(Vector3 Origin, float Radius, Vector3 Color, Vector3 Direction,
    float InnerAngle, float OuterAngle, float Exponent, bool IsSpotlight, bool IsPrimary, bool DynamicShadows,
    float SweepAngle, float SweepSeconds)
{
    internal const float SweepEaseFraction = 1f / 6f;
    internal const float SweepWarmupSeconds = 2;
    internal bool IsMoving => SweepAngle > 0;
    internal float CoverageAngle => OuterAngle + SweepAngle * (MathF.PI / 360);
    internal Vector3 SweepStartAngles => AimAngles - new Vector3(SweepAngle / 2, 0, 0);
    internal Vector3 SweepEndAngles => AimAngles + new Vector3(SweepAngle / 2, 0, 0);
    internal Vector3 SweepStartDirection => FromAngles(SweepStartAngles);
    internal Vector3 SweepEndDirection => FromAngles(SweepEndAngles);

    private Vector3 AimAngles => new(
        MathF.Atan2(-Direction.Z, new Vector2(Direction.X, Direction.Y).Length()) * (180 / MathF.PI),
        MathF.Atan2(Direction.Y, Direction.X) * (180 / MathF.PI), 0);

    internal Vector3 DirectionAt(double elapsedSeconds)
    {
        if (!IsMoving || elapsedSeconds <= SweepWarmupSeconds) return Direction;
        double elapsed = elapsedSeconds - SweepWarmupSeconds;
        double firstLeg = SweepSeconds / 2d;
        if (elapsed < firstLeg)
            return FromAngles(Vector3.Lerp(AimAngles, SweepStartAngles, Ease(elapsed / firstLeg)));
        double leg = (elapsed - firstLeg) / SweepSeconds;
        float fraction = Ease(leg - Math.Floor(leg));
        return FromAngles((Math.Floor(leg) % 2) == 0
            ? Vector3.Lerp(SweepStartAngles, SweepEndAngles, fraction)
            : Vector3.Lerp(SweepEndAngles, SweepStartAngles, fraction));
    }

    private static float Ease(double fraction)
    {
        // rotateTo's equal acceleration/deceleration intervals leave a constant-speed middle.
        double edge = SweepEaseFraction;
        if (fraction < edge) return (float)(fraction * fraction / (2 * edge * (1 - edge)));
        if (fraction > 1 - edge) return 1 - (float)(Math.Pow(1 - fraction, 2) / (2 * edge * (1 - edge)));
        return (float)((fraction - edge / 2) / (1 - edge));
    }

    private static Vector3 FromAngles(Vector3 angles)
    {
        float pitch = angles.X * (MathF.PI / 180), yaw = angles.Y * (MathF.PI / 180);
        return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
    }

    internal static IEnumerable<(MapEntity Entity, MapLight Light, int Index)> EnumeratePrimary(MapDocument document, int sunCount = 1)
    {
        if (sunCount is < 1 or > 127) throw new ArgumentOutOfRangeException(nameof(sunCount));
        // None, then contiguous directional Suns. The top S bytes are occluded-Sun markers.
        int index = sunCount + 1;
        foreach (MapEntity entity in document.Entities)
        {
            if (entity.ClassName != "light") continue;
            if (!TryCreate(entity, document.ResolveTargets(entity), out MapLight light, out string? error))
            {
                if (error is not null) throw new InvalidDataException(error);
                continue;
            }
            if (!light.IsPrimary) continue;
            if (index >= 256 - sunCount)
                throw new NotSupportedException($"A map with {sunCount} Suns supports at most {255 - 2 * sunCount} primary local lights.");
            yield return (entity, light, index++);
        }
    }

    internal static bool TryCreate(MapEntity entity, IReadOnlyList<MapEntity> resolvedTargets,
        out MapLight light, out string? error)
    {
        light = default;
        if (!MapLightProperties.TryRead(entity, out MapLightProperties properties, out error)) return false;
        if (properties.Definition != MapLightDefaults.Definition)
        {
            error = $"Light definition '{properties.Definition}' is not supported. Use '{MapLightDefaults.Definition}'.";
            return false;
        }
        bool primarySpot = properties.SpawnFlags == MapLightDefaults.PrimarySpot;
        float maximum = Math.Max(properties.Color.X, Math.Max(properties.Color.Y, properties.Color.Z));
        if (properties.Radius == 0 || properties.Intensity == 0 || maximum == 0) return false;

        if (!entity.TryGetOrigin(out Vector3 origin))
        {
            error = "Light origin must contain three finite numbers.";
            return false;
        }
        Vector3 direction = Vector3.Zero;
        float innerAngle = 0, outerAngle = MathF.PI;
        if (properties.IsSpotlight)
        {
            if (string.IsNullOrWhiteSpace(properties.Target))
            {
                error = "The spotlight needs a target entity to determine its direction.";
                return false;
            }
            if (resolvedTargets.Count != 1)
            {
                error = resolvedTargets.Count == 0 ? $"Light target '{properties.Target}' was not found." :
                    $"Light target '{properties.Target}' matches multiple entities.";
                return false;
            }
            if (!resolvedTargets[0].TryGetOrigin(out Vector3 target))
            {
                error = $"Light target '{properties.Target}' origin must contain three finite numbers.";
                return false;
            }
            double dx = (double)target.X - origin.X, dy = (double)target.Y - origin.Y, dz = (double)target.Z - origin.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance == 0)
            {
                error = "The spotlight target must be away from the light origin.";
                return false;
            }
            direction = new Vector3((float)(dx / distance), (float)(dy / distance), (float)(dz / distance));
            double outer = properties.OuterFov is { } fov ? fov * (Math.PI / 360) :
                Math.Atan(MapLightDefaults.TargetRadius / distance);
            double inner = properties.InnerFov * (Math.PI / 360);
            if (inner >= outer)
            {
                error = "The inner spotlight FOV must be smaller than the cone determined by its target.";
                return false;
            }
            innerAngle = (float)inner;
            outerAngle = (float)outer;
            if (innerAngle >= outerAngle)
            {
                error = "The inner and outer spotlight FOVs are too close to distinguish.";
                return false;
            }
            if (primarySpot && (outerAngle >= MathF.PI / 2 ||
                MathF.Cos(innerAngle) <= MathF.Cos(outerAngle)))
            {
                error = "A primary spotlight needs distinguishable inner and outer cones with an outer FOV below 180 degrees.";
                return false;
            }
            if (properties.SweepAngle > 0 && outerAngle + properties.SweepAngle * (MathF.PI / 360) >= MathF.PI / 2)
            {
                error = "The outer FOV plus the sweep arc must be below 180 degrees. Narrow the cone or sweep arc.";
                return false;
            }
        }
        Vector3 color = properties.Color / maximum * properties.Intensity;
        light = new MapLight(origin, properties.Radius, color, direction, innerAngle, outerAngle,
            properties.Exponent, properties.IsSpotlight,
            properties.SpawnFlags is MapLightDefaults.PrimaryOmni or MapLightDefaults.PrimarySpot,
            properties.DynamicShadows, properties.SweepAngle, properties.SweepSeconds);
        return true;
    }
}
