using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Rendering;

internal readonly record struct SceneLight(Vector3 Origin, float Radius, Vector3 Color, Vector3 Direction,
    float InnerAngle, float OuterAngle, float Exponent, bool IsSpotlight)
{
    internal static bool TryCreate(EditorScene scene, MapEntity entity, out SceneLight light, out string? error)
    {
        light = default;
        if (!MapLightProperties.TryRead(entity, out MapLightProperties properties, out error)) return false;
        if (properties.Definition != MapLightDefaults.Definition)
        {
            error = $"Light definition '{properties.Definition}' is not supported by the preview.";
            return false;
        }
        float maximum = Math.Max(properties.Color.X, Math.Max(properties.Color.Y, properties.Color.Z));
        if (properties.Radius == 0 || properties.Intensity == 0 || maximum == 0) return false;

        Vector3 origin = EditorSession.EntityOrigin(entity);
        Vector3 direction = Vector3.Zero;
        float innerAngle = 0, outerAngle = MathF.PI;
        if (properties.IsSpotlight)
        {
            if (string.IsNullOrWhiteSpace(properties.Target))
            {
                error = "The spotlight needs a target entity to determine its direction.";
                return false;
            }
            IReadOnlyList<MapEntity> targets = scene.ResolveTargets(entity);
            if (targets.Count != 1)
            {
                error = targets.Count == 0 ? $"Light target '{properties.Target}' was not found." :
                    $"Light target '{properties.Target}' matches multiple entities.";
                return false;
            }
            Vector3 target = EditorSession.EntityOrigin(targets[0]);
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
                error = "The inner and outer spotlight FOVs are too close to distinguish in the preview.";
                return false;
            }
        }
        Vector3 color = properties.Color / maximum * properties.Intensity;
        light = new SceneLight(origin, properties.Radius, color, direction, innerAngle, outerAngle,
            properties.Exponent, properties.IsSpotlight);
        return true;
    }
}
