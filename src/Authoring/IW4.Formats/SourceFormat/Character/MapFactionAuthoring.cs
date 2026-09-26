using System.Text;
using System.Text.Json;

namespace IW4.Formats.SourceFormat.Character;

public sealed record FactionAppearance(string Body, string? Head, string ViewHands,
    bool HeadIncluded = false, string? CustomAssetFolder = null);

public sealed record FactionAppearanceChoice(string Model, string Label);

public sealed record MapFactionSettings(
    string Allies,
    string Axis,
    FactionAppearance? AlliesAssaultA = null,
    FactionAppearance? AxisAssaultA = null);

public static class RangersAssaultAppearance
{
    public static FactionAppearance StockA { get; } = new("mp_body_us_army_assault_a", null, "viewhands_us_army");

    public static IReadOnlyList<FactionAppearanceChoice> Bodies { get; } =
    [
        new("mp_body_us_army_assault_a", "Rangers assault A"),
        new("mp_body_us_army_assault_b", "Rangers assault B"),
        new("mp_body_us_army_assault_c", "Rangers assault C")
    ];

    public static IReadOnlyList<FactionAppearanceChoice> Heads { get; } =
    [
        new("head_us_army_a", "Rangers head A"),
        new("head_us_army_b", "Rangers head B"),
        new("head_us_army_c", "Rangers head C"),
        new("head_us_army_d", "Rangers head D"),
        new("head_us_army_f", "Rangers head F")
    ];

    public static IReadOnlyList<FactionAppearanceChoice> ViewHands { get; } =
    [
        new("viewhands_us_army", "Rangers assault hands"),
        new("viewhands_sniper_us_army", "Rangers sniper hands")
    ];

    public static FactionAppearance Resolve(MapFactionSettings settings, string team)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return team switch
        {
            "allies" => settings.AlliesAssaultA ?? StockA,
            "axis" => settings.AxisAssaultA ?? StockA,
            _ => throw new ArgumentOutOfRangeException(nameof(team), team, "Expected allies or axis.")
        };
    }

    internal static void Validate(FactionAppearance appearance, string team)
    {
        if (appearance.CustomAssetFolder is { } folder)
        {
            if (!IsCustomAssetFolder(folder) || appearance.Body != folder + "_body" ||
                appearance.ViewHands != folder + "_viewhands" || !appearance.HeadIncluded || appearance.Head is not null)
                throw new InvalidDataException($"The {team} custom appearance requires an imported body with its head included and matching hands.");
            return;
        }
        if (appearance.HeadIncluded)
            throw new InvalidDataException($"The {team} stock body requires a separate head.");
        if (!Bodies.Any(choice => choice.Model == appearance.Body))
            throw new InvalidDataException($"Unsupported {team} Rangers assault A body '{appearance.Body}'.");
        if (appearance.Head is not null && !Heads.Any(choice => choice.Model == appearance.Head))
            throw new InvalidDataException($"Unsupported {team} Rangers assault A head '{appearance.Head}'.");
        if (!ViewHands.Any(choice => choice.Model == appearance.ViewHands))
            throw new InvalidDataException($"Unsupported {team} Rangers assault A hands '{appearance.ViewHands}'.");
    }

    public static bool IsCustomAssetFolder(string folder) => folder.Length is > 0 and <= 64 &&
        folder.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}

public static class MapFactionAuthoring
{
    public const string MapPropertyName = "_iw4radiant_factions";
    public const string UsArmy = "us_army";
    public const string OpforceAirborne = "opforce_airborne";

    public static MapFactionSettings Default { get; } = new(UsArmy, OpforceAirborne);

    public static string GetCharacterAssetsDirectory(string mapFilePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mapFilePath)) ??
            throw new ArgumentException("The saved map must have a containing directory.", nameof(mapFilePath)), "characters");

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
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int formatVersion) || formatVersion is not (1 or 2 or 3))
                throw new InvalidDataException("Expected a version 1, 2 or 3 faction manifest.");
            return Validate(new MapFactionSettings(
                RequiredString(root, "allies"),
                RequiredString(root, "axis"),
                formatVersion >= 2 ? OptionalAppearance(root, "alliesAssaultA") : null,
                formatVersion >= 2 ? OptionalAppearance(root, "axisAssaultA") : null));
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
        bool hasAppearance = settings.AlliesAssaultA is not null || settings.AxisAssaultA is not null;
        bool hasCustom = settings.AlliesAssaultA?.CustomAssetFolder is not null || settings.AxisAssaultA?.CustomAssetFolder is not null;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", hasCustom ? 3 : hasAppearance ? 2 : 1);
            writer.WriteString("allies", settings.Allies);
            writer.WriteString("axis", settings.Axis);
            if (settings.AlliesAssaultA is { } allies)
                WriteAppearance(writer, "alliesAssaultA", allies);
            if (settings.AxisAssaultA is { } axis)
                WriteAppearance(writer, "axisAssaultA", axis);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteAppearance(Utf8JsonWriter writer, string name, FactionAppearance appearance)
    {
        writer.WriteStartObject(name);
        writer.WriteString("body", appearance.Body);
        if (appearance.Head is not null)
            writer.WriteString("head", appearance.Head);
        writer.WriteString("viewHands", appearance.ViewHands);
        if (appearance.HeadIncluded) writer.WriteBoolean("headIncluded", true);
        if (appearance.CustomAssetFolder is { } folder) writer.WriteString("customAssetFolder", folder);
        writer.WriteEndObject();
    }

    private static FactionAppearance? OptionalAppearance(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The faction manifest requires '{name}' to be an appearance object.");
        string? head = null;
        if (value.TryGetProperty("head", out JsonElement headValue))
        {
            if (headValue.ValueKind != JsonValueKind.String ||
                headValue.GetString() is not string headName || string.IsNullOrWhiteSpace(headName))
                throw new InvalidDataException($"The faction manifest has an invalid '{name}.head'.");
            head = headName;
        }
        bool headIncluded = false;
        if (value.TryGetProperty("headIncluded", out JsonElement included))
        {
            if (included.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"The faction manifest has an invalid '{name}.headIncluded'.");
            headIncluded = included.GetBoolean();
        }
        string? folder = value.TryGetProperty("customAssetFolder", out _) ? RequiredString(value, "customAssetFolder") : null;
        return new FactionAppearance(RequiredString(value, "body"), head, RequiredString(value, "viewHands"), headIncluded, folder);
    }

    private static MapFactionSettings Validate(MapFactionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateFaction(settings.Allies, "allies");
        ValidateFaction(settings.Axis, "axis");
        if (settings.AlliesAssaultA is { } allies)
        {
            if (settings.Allies != UsArmy)
                throw new InvalidDataException("An allies Rangers assault A appearance requires the Rangers faction.");
            RangersAssaultAppearance.Validate(allies, "allies");
        }
        if (settings.AxisAssaultA is { } axis)
        {
            if (settings.Axis != UsArmy)
                throw new InvalidDataException("An axis Rangers assault A appearance requires the Rangers faction.");
            RangersAssaultAppearance.Validate(axis, "axis");
        }
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
