using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal sealed record DestructiblePreset(
    string Name, string Description, string ModelName, string DestructibleType, string TargetName, string CsvInclude)
{
    internal required IReadOnlyList<string> ModelNames { get; init; }
    internal required IReadOnlyList<string> FxNames { get; init; }
    internal required IReadOnlyList<string> SoundNames { get; init; }
}

internal static class DestructiblePresets
{
    // Stock PS3 common_scripts/_destructible_types::vehicle_policecar and
    // Terminal's script_model properties. Common MP supplies the script and animations.
    internal static IReadOnlyList<DestructiblePreset> All { get; } =
    [
        new("LAPD Police Car", "Breakable windows and tires, burning damage stages and an exploding wreck.",
            "vehicle_policecar_lapd_destructible", "vehicle_policecar", "destructible_vehicle",
            "destructible_vehicle_policecar_lapd_destructible")
        {
            ModelNames =
            [
                "vehicle_policecar_lapd_destructible", "vehicle_policecar_lapd_destroy",
                "vehicle_policecar_lapd_door_lb", "vehicle_policecar_lapd_door_lf", "vehicle_policecar_lapd_door_rf",
                "vehicle_policecar_lapd_mirror_l", "vehicle_policecar_lapd_mirror_r", "vehicle_policecar_lapd_wheel_lf"
            ],
            FxNames =
            [
                "explosions/small_vehicle_explosion", "props/car_glass_headlight", "props/car_glass_large",
                "props/car_glass_med", "smoke/car_damage_blacksmoke", "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            SoundNames =
            [
                "car_explode_police", "fire_vehicle_flareup_med", "fire_vehicle_med",
                "veh_glass_break_large", "veh_glass_break_small", "veh_tire_deflate"
            ]
        }
    ];

    internal static DestructiblePreset? Find(IReadOnlyDictionary<string, string> properties) =>
        All.FirstOrDefault(preset => properties.GetValueOrDefault("destructible_type") == preset.DestructibleType);

    internal static void Validate(IReadOnlyDictionary<string, string> properties)
    {
        if (Find(properties) is not { } preset) return;
        if (properties.GetValueOrDefault("classname") != "script_model" ||
            properties.GetValueOrDefault("model") != preset.ModelName ||
            properties.GetValueOrDefault("targetname") != preset.TargetName)
            throw new InvalidDataException($"{preset.Name} requires its original model, script_model class and '{preset.TargetName}' targetname. Place the preset again to restore its setup.");
    }

    internal static bool HasDiscoveryName(MapEntity entity) => Find(entity.Properties) is { } preset &&
        entity.ClassName == "script_model" && entity.Properties.GetValueOrDefault("model") == preset.ModelName &&
        entity.Properties.GetValueOrDefault("targetname") == preset.TargetName;

    internal static MapEntity Place(EditorSession session, DestructiblePreset preset, Vector3 position)
    {
        var model = session.Scene.ResolveModel?.Invoke(preset.ModelName) ??
            throw new InvalidDataException($"Load the raw asset library containing {preset.Name} before placing it.");
        MapEntity? placed = null;
        session.Edit(() =>
        {
            placed = XModelEditing.Add(session, model, position, null, 0, 1);
            placed.Properties["classname"] = "script_model";
            placed.Properties["targetname"] = preset.TargetName;
            placed.Properties["destructible_type"] = preset.DestructibleType;
            placed.Properties["csv_include"] = preset.CsvInclude;
        });
        return placed ?? throw new InvalidOperationException("The destructible could not be placed.");
    }
}
