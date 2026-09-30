using System.Globalization;
using System.Numerics;

namespace Iw4Radiant.MapSource;

internal readonly record struct MapFogProperties
{
    internal Vector3 Color { get; init; }
    internal float StartDistance { get; init; }
    internal float HalfDistance { get; init; }
    internal float MaxOpacity { get; init; }

    internal static MapFogProperties Default => new()
    {
        Color = new Vector3(0.55f, 0.62f, 0.68f),
        StartDistance = 512f,
        HalfDistance = 1024f,
        MaxOpacity = 1f
    };

    internal static bool TryRead(MapEntity world, out MapFogProperties? properties, out string? error)
    {
        properties = null;
        error = null;
        if (world.ClassName != "worldspawn")
        {
            error = "Fog properties belong to worldspawn.";
            return false;
        }
        bool hasColor = world.Properties.TryGetValue("fog_color", out string? colorText);
        bool hasStart = world.Properties.TryGetValue("fog_start", out string? startText);
        bool hasHalf = world.Properties.TryGetValue("fog_half_distance", out string? halfText);
        bool hasOpacity = world.Properties.TryGetValue("fog_max_opacity", out string? opacityText);
        if (!hasColor && !hasStart && !hasHalf && !hasOpacity) return true;
        if (!hasColor || !hasStart || !hasHalf || !hasOpacity)
        {
            error = "Fog requires fog_color, fog_start, fog_half_distance and fog_max_opacity together.";
            return false;
        }
        if (!ReadColor(colorText, out Vector3 color) ||
            !ReadNumber(startText, out float start) ||
            !ReadNumber(halfText, out float half) ||
            !ReadNumber(opacityText, out float opacity))
        {
            error = "Fog values must be finite numbers; fog_color needs three components.";
            return false;
        }
        var result = new MapFogProperties
        {
            Color = color, StartDistance = start, HalfDistance = half, MaxOpacity = opacity
        };
        error = result.Validate();
        if (error is not null) return false;
        properties = result;
        return true;
    }

    internal void ApplyTo(MapEntity world)
    {
        if (world.ClassName != "worldspawn")
            throw new ArgumentException("Fog properties belong to worldspawn.", nameof(world));
        if (Validate() is { } error) throw new ArgumentException(error, nameof(world));
        world.Properties["fog_color"] = $"{Number(Color.X)} {Number(Color.Y)} {Number(Color.Z)}";
        world.Properties["fog_start"] = Number(StartDistance);
        world.Properties["fog_half_distance"] = Number(HalfDistance);
        world.Properties["fog_max_opacity"] = Number(MaxOpacity);
    }

    internal static void RemoveFrom(MapEntity world)
    {
        if (world.ClassName != "worldspawn")
            throw new ArgumentException("Fog properties belong to worldspawn.", nameof(world));
        world.Properties.Remove("fog_color");
        world.Properties.Remove("fog_start");
        world.Properties.Remove("fog_half_distance");
        world.Properties.Remove("fog_max_opacity");
    }

    internal string? Validate()
    {
        if (!float.IsFinite(StartDistance) || StartDistance < 0)
            return "Fog start distance must be finite and nonnegative.";
        if (!float.IsFinite(HalfDistance) || HalfDistance <= 0)
            return "Fog halfway distance must be finite and greater than zero.";
        float density = 0.6931472f / HalfDistance;
        if (!float.IsFinite(density) || density <= 0 || !float.IsFinite(density * StartDistance))
            return "Fog distances must produce a finite positive density and distance term.";
        if (!float.IsFinite(Color.X) || !float.IsFinite(Color.Y) || !float.IsFinite(Color.Z) ||
            Color.X < 0 || Color.X > 1 || Color.Y < 0 || Color.Y > 1 || Color.Z < 0 || Color.Z > 1)
            return "Fog color components must be finite and between zero and one.";
        if (!float.IsFinite(MaxOpacity) || MaxOpacity < 0 || MaxOpacity > 1)
            return "Fog maximum opacity must be finite and between zero and one.";
        return null;
    }

    private static bool ReadColor(string? text, out Vector3 value)
    {
        value = default;
        if (text is null) return false;
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !ReadNumber(parts[0], out float red) ||
            !ReadNumber(parts[1], out float green) || !ReadNumber(parts[2], out float blue)) return false;
        value = new Vector3(red, green, blue);
        return true;
    }

    private static bool ReadNumber(string? text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
