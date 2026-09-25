using System.Text;
using System.Text.Json;

namespace IW4.Formats.SourceFormat.Character;

public sealed record MapFactionSettings(string Allies, string Axis);

public static class MapFactionAuthoring
{
    public const string MapPropertyName = "_iw4radiant_factions";
    public const string UsArmy = "us_army";
    public const string OpforceAirborne = "opforce_airborne";

    public static MapFactionSettings Default { get; } = new(UsArmy, OpforceAirborne);

    public static MapFactionSettings Read(IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.TryGetValue(MapPropertyName, out string? source))
            return Default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(source);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                throw new InvalidDataException("Expected a version 1 faction manifest.");
            return Validate(new MapFactionSettings(RequiredString(root, "allies"), RequiredString(root, "axis")));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The faction map property is not valid JSON.", exception);
        }
    }

    public static void Write(IDictionary<string, string> properties, MapFactionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(properties);
        settings = Validate(settings);
        if (settings == Default)
            properties.Remove(MapPropertyName);
        else
            properties[MapPropertyName] = Serialize(settings);
    }

    public static string Serialize(MapFactionSettings settings)
    {
        settings = Validate(settings);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("allies", settings.Allies);
            writer.WriteString("axis", settings.Axis);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static MapFactionSettings Validate(MapFactionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateFaction(settings.Allies, "allies");
        ValidateFaction(settings.Axis, "axis");
        return settings;
    }

    private static void ValidateFaction(string faction, string team)
    {
        if (faction is not (UsArmy or OpforceAirborne))
            throw new InvalidDataException($"Unsupported {team} faction '{faction}'.");
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not string text || string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"The faction manifest requires '{name}'.");
        return text;
    }
}
