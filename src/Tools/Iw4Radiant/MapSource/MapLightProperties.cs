using System.Globalization;
using System.Numerics;

namespace Iw4Radiant.MapSource;

internal readonly record struct MapLightProperties
{
    public string Definition { get; init; }
    public float Radius { get; init; }
    public float Intensity { get; init; }
    public Vector3 Color { get; init; }
    public string Target { get; init; }
    public float? OuterFov { get; init; }
    public float InnerFov { get; init; }
    public float Exponent { get; init; }
    public int SpawnFlags { get; init; }
    public bool IsSpotlight => (SpawnFlags & MapLightDefaults.PrimaryOmni) == 0 &&
        (!string.IsNullOrWhiteSpace(Target) || (SpawnFlags & MapLightDefaults.PrimarySpot) != 0);

    public static bool TryRead(MapEntity entity, out MapLightProperties properties, out string? error)
    {
        properties = default;
        error = null;
        if (entity.ClassName != "light")
        {
            error = "Select a light entity to edit light properties.";
            return false;
        }
        if (!ReadScalar(entity, "radius", MapLightDefaults.Radius, out float radius, out error) ||
            !ReadScalar(entity, "intensity", MapLightDefaults.Intensity, out float intensity, out error) ||
            !ReadScalar(entity, "fov_inner", MapLightDefaults.InnerFov, out float inner, out error) ||
            !ReadScalar(entity, "exponent", MapLightDefaults.Exponent, out float exponent, out error))
            return false;

        float? outer = null;
        if (entity.Properties.ContainsKey("fov_outer"))
        {
            if (!ReadScalar(entity, "fov_outer", 0, out float value, out error)) return false;
            outer = value;
        }
        string[] components = entity.Properties.GetValueOrDefault("_color", MapLightDefaults.Color)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (components.Length != 3 || !ReadNumber(components[0], out float red) ||
            !ReadNumber(components[1], out float green) || !ReadNumber(components[2], out float blue))
        {
            error = "Light _color must contain three finite numbers.";
            return false;
        }
        int flags = 0;
        if (entity.Properties.TryGetValue("spawnflags", out string? flagText) &&
            !int.TryParse(flagText, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
        {
            error = "Light spawnflags must be an integer.";
            return false;
        }
        properties = new MapLightProperties
        {
            Definition = entity.Properties.GetValueOrDefault("def", MapLightDefaults.Definition),
            Radius = radius, Intensity = intensity, Color = new Vector3(red, green, blue),
            Target = entity.Properties.GetValueOrDefault("target", ""), OuterFov = outer,
            InnerFov = inner, Exponent = exponent, SpawnFlags = flags
        };
        error = properties.Validate();
        return error is null;
    }

    public void ApplyTo(MapEntity entity)
    {
        if (entity.ClassName != "light")
            throw new ArgumentException("Light properties can only be applied to a light entity.", nameof(entity));
        if (Validate() is { } error) throw new ArgumentException(error);
        bool valid = TryRead(entity, out MapLightProperties previous, out _);
        if (!valid || Definition != previous.Definition) entity.Properties["def"] = Definition;
        if (!valid || Radius != previous.Radius) entity.Properties["radius"] = Number(Radius);
        if (!valid || Intensity != previous.Intensity) entity.Properties["intensity"] = Number(Intensity);
        if (!valid || Color != previous.Color)
            entity.Properties["_color"] = $"{Number(Color.X)} {Number(Color.Y)} {Number(Color.Z)}";
        if (string.IsNullOrWhiteSpace(Target)) entity.Properties.Remove("target");
        else if (!valid || Target != previous.Target) entity.Properties["target"] = Target;
        if (!valid || OuterFov != previous.OuterFov)
        {
            if (OuterFov is { } outer) entity.Properties["fov_outer"] = Number(outer);
            else entity.Properties.Remove("fov_outer");
        }
        if (!valid || InnerFov != previous.InnerFov) entity.Properties["fov_inner"] = Number(InnerFov);
        if (!valid || Exponent != previous.Exponent) entity.Properties["exponent"] = Number(Exponent);
        if (!valid || SpawnFlags != previous.SpawnFlags)
            entity.Properties["spawnflags"] = SpawnFlags.ToString(CultureInfo.InvariantCulture);
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Definition) || Definition.IndexOfAny(['\r', '\n', '\0']) >= 0)
            return "The light definition must be a nonempty single-line asset name.";
        if (Target is null || Target.IndexOfAny(['\r', '\n', '\0']) >= 0)
            return "The light target must be a single-line targetname.";
        if (!float.IsFinite(Radius) || Radius < 0) return "Light radius must be finite and nonnegative.";
        if (!float.IsFinite(Intensity) || Intensity < 0) return "Light intensity must be finite and nonnegative.";
        if (!float.IsFinite(Color.X) || !float.IsFinite(Color.Y) || !float.IsFinite(Color.Z) ||
            Color.X < 0 || Color.Y < 0 || Color.Z < 0)
            return "Light color components must be finite and nonnegative.";
        if (!float.IsFinite(InnerFov) || InnerFov < 0) return "The inner light FOV must be finite and nonnegative.";
        if (OuterFov is { } outer && (!float.IsFinite(outer) || outer <= 0 || outer > 360))
            return "The outer light FOV must be greater than zero and at most 360 degrees.";
        if (OuterFov is { } limit && InnerFov >= limit) return "The inner light FOV must be smaller than the outer FOV.";
        if (!float.IsFinite(Exponent) || Exponent < 0) return "The light exponent must be finite and nonnegative.";
        return null;
    }

    private static bool ReadScalar(MapEntity entity, string key, float fallback, out float value, out string? error)
    {
        value = fallback;
        error = null;
        if (!entity.Properties.TryGetValue(key, out string? text) || ReadNumber(text, out value)) return true;
        error = $"Light {key} must be a finite number.";
        return false;
    }

    private static bool ReadNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
