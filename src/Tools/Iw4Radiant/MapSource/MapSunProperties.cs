using System.Globalization;
using System.Numerics;
using Iw4Radiant.Editing;

namespace Iw4Radiant.MapSource;

internal readonly record struct MapSunProperties
{
    internal float Intensity { get; init; }
    internal Vector3 Color { get; init; }
    internal Vector3 Angles { get; init; }
    // Source sundirection is pitch/yaw/roll pointing toward the sun, not along its rays.
    internal Vector3 Direction => EntityOrientation.Forward(Angles);

    internal static bool TryRead(MapEntity world, out MapSunProperties? properties, out string? error)
    {
        properties = null;
        error = null;
        if (world.ClassName != "worldspawn")
        {
            error = "Sun properties belong to worldspawn.";
            return false;
        }
        bool hasIntensity = world.Properties.TryGetValue("sunlight", out string? intensityText);
        bool hasColor = world.Properties.TryGetValue("suncolor", out string? colorText);
        bool hasAngles = world.Properties.TryGetValue("sundirection", out string? anglesText);
        if (!hasIntensity && !hasColor && !hasAngles) return true;
        if (!hasIntensity || !hasColor || !hasAngles)
        {
            error = "Sunlight requires sunlight, suncolor and sundirection together.";
            return false;
        }
        if (!ReadNumber(intensityText, out float intensity))
        {
            error = "Sunlight intensity must be a finite number.";
            return false;
        }
        if (!ReadVector(colorText, out Vector3 color))
        {
            error = "Suncolor must contain three finite color components.";
            return false;
        }
        if (!ReadVector(anglesText, out Vector3 angles))
        {
            error = "Sundirection must contain finite pitch, yaw and roll in degrees.";
            return false;
        }
        var result = new MapSunProperties { Intensity = intensity, Color = color, Angles = angles };
        error = result.Validate();
        if (error is not null) return false;
        properties = result;
        return true;
    }

    internal void ApplyTo(MapEntity world)
    {
        if (world.ClassName != "worldspawn")
            throw new ArgumentException("Sun properties can only be applied to worldspawn.", nameof(world));
        if (Validate() is { } error) throw new ArgumentException(error);
        TryRead(world, out MapSunProperties? previous, out _);
        if (previous is not { } saved || saved.Intensity != Intensity)
            world.Properties["sunlight"] = Number(Intensity);
        if (previous is not { } savedColor || savedColor.Color != Color)
            world.Properties["suncolor"] = Vector(Color);
        if (previous is not { } savedAngles || savedAngles.Angles != Angles)
            world.Properties["sundirection"] = Vector(Angles);
    }

    internal static void RemoveFrom(MapEntity world)
    {
        if (world.ClassName != "worldspawn")
            throw new ArgumentException("Sun properties can only be removed from worldspawn.", nameof(world));
        world.Properties.Remove("sunlight");
        world.Properties.Remove("suncolor");
        world.Properties.Remove("sundirection");
    }

    private string? Validate()
    {
        if (!float.IsFinite(Intensity) || Intensity < 0)
            return "Sunlight intensity must be finite and nonnegative.";
        if (!Finite(Color) || Color.X < 0 || Color.Y < 0 || Color.Z < 0)
            return "Suncolor components must be finite and nonnegative.";
        if (!Finite(Angles)) return "Sundirection angles must be finite numbers.";
        return null;
    }

    private static bool ReadVector(string? text, out Vector3 value)
    {
        value = default;
        if (text is null) return false;
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !ReadNumber(parts[0], out float x) ||
            !ReadNumber(parts[1], out float y) || !ReadNumber(parts[2], out float z)) return false;
        value = new Vector3(x, y, z);
        return true;
    }

    private static bool ReadNumber(string? text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Vector(Vector3 value) => $"{Number(value.X)} {Number(value.Y)} {Number(value.Z)}";
}
