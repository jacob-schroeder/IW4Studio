using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal sealed record DestructiblePreset(
    string Name, string Description, string ModelName, string DestructibleType, string TargetName, string? CsvInclude = null)
{
    internal required string Category { get; init; }
    internal required DestructiblePreviewDefinition Preview { get; init; }
    internal required IReadOnlyList<string> ModelNames { get; init; }
    internal required IReadOnlyList<string> FxNames { get; init; }
    internal required IReadOnlyList<string> SoundNames { get; init; }
    internal IReadOnlyList<string> AnimationNames { get; init; } = [];
    internal string? Family { get; init; }
    internal string? Variant { get; init; }
    internal string? SoundCsvInclude { get; init; }
    internal string? PrecacheScript { get; init; }
    internal string? PrecacheRawFileName => PrecacheScript is null ? null :
        PrecacheScript.Replace(' ', '/') + ".gsc";
    internal float ModelScale { get; init; } = 1;
}

internal static class DestructiblePresets
{
    // Stock MP script_model pairings from map entity data, with direct recipe assets from
    // common_scripts/_destructible_types.gsc. Common MP supplies the shared script.
    internal static IReadOnlyList<DestructiblePreset> All { get; } =
    [
        new("LAPD Police Car", "Breakable windows and tires, burning damage stages and an exploding wreck.",
            "vehicle_policecar_lapd_destructible", "vehicle_policecar", "destructible_vehicle",
            "destructible_vehicle_policecar_lapd_destructible")
        {
            Category = "Vehicles",
            Preview = new(
                [
                    new("Intact"),
                    new("White smoke", FxName: "smoke/car_damage_whitesmoke", FxTag: "tag_hood_fx", RepeatFx: true),
                    new("Black smoke", FxName: "smoke/car_damage_blacksmoke", FxTag: "tag_hood_fx", RepeatFx: true),
                    new("Burning", FxName: "smoke/car_damage_blacksmoke_fire", FxTag: "tag_hood_fx",
                        SoundName: "fire_vehicle_med", TransitionSoundName: "fire_vehicle_flareup_med", RepeatFx: true),
                    new("Wreck", "vehicle_policecar_lapd_destroy", "explosions/small_vehicle_explosion", "tag_death_fx",
                        TransitionSoundName: "car_explode_police", WorldUpFx: true)
                ],
                [
                    new("Front windshield", "tag_glass_front", "tag_glass_front_d", "tag_glass_front_fx",
                        "props/car_glass_large", "veh_glass_break_large"),
                    new("Rear windshield", "tag_glass_back", "tag_glass_back_d", "tag_glass_back_fx",
                        "props/car_glass_large", "veh_glass_break_large"),
                    new("Center divider", "tag_center_glass", "tag_center_glass_d", "tag_center_glass_fx",
                        "props/car_glass_large", "veh_glass_break_large"),
                    new("Left front window", "tag_glass_left_front", "tag_glass_left_front_d", "tag_glass_left_front_fx",
                        "props/car_glass_med", "veh_glass_break_large"),
                    new("Right front window", "tag_glass_right_front", "tag_glass_right_front_d", "tag_glass_right_front_fx",
                        "props/car_glass_med", "veh_glass_break_large"),
                    new("Left rear window", "tag_glass_left_back", "tag_glass_left_back_d", "tag_glass_left_back_fx",
                        "props/car_glass_med", "veh_glass_break_large"),
                    new("Right rear window", "tag_glass_right_back", "tag_glass_right_back_d", "tag_glass_right_back_fx",
                        "props/car_glass_med", "veh_glass_break_large")
                ], HasTires: true),
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
        },
        new("Small Hatchback (Blue)", "Breakable windows, lights and tires, with burning damage stages and an exploding wreck.",
            "vehicle_small_hatch_blue_destructible_mp", "vehicle_small_hatch_blue", "destructible_vehicle")
        {
            Category = "Vehicles",
            Family = "Small Hatchbacks",
            Variant = "Blue",
            CsvInclude = "destructible_vehicle_small_hatch_blue_destructible_mp",
            SoundCsvInclude = "vehicle_car_exp",
            Preview = SmallHatchPreview("vehicle_small_hatch_blue_destroyed"),
            ModelNames =
            [
                "vehicle_small_hatch_blue_destructible_mp", "vehicle_small_hatch_blue_destroyed", "vehicle_small_hatch_blue_hood",
                "vehicle_small_hatch_blue_door_lf", "vehicle_small_hatch_blue_door_rf",
                "vehicle_small_hatch_blue_mirror_l", "vehicle_small_hatch_blue_mirror_r"
            ],
            FxNames =
            [
                "smoke/car_damage_whitesmoke", "smoke/car_damage_blacksmoke", "smoke/car_damage_blacksmoke_fire",
                "explosions/small_vehicle_explosion", "props/car_glass_large", "props/car_glass_med",
                "props/car_glass_headlight", "props/car_glass_brakelight"
            ],
            SoundNames =
            [
                "fire_vehicle_flareup_med", "fire_vehicle_med", "car_explode", "veh_tire_deflate",
                "veh_glass_break_large", "veh_glass_break_small"
            ]
        },
        new("Wallmount Flatscreen TV 01", "Shatters into a broken flatscreen with a burst and explosion.",
            "ma_flatscreen_tv_wallmount_01", "toy_tv_flatscreen_wallmount_01", "destructible_toy")
        {
            Category = "Electronics",
            Family = "Wallmount Flatscreen TVs",
            Variant = "01",
            SoundCsvInclude = "destruct_tv_exp",
            Preview = new(
                [
                    new("Intact"),
                    new("Broken", "ma_flatscreen_tv_wallmount_broken_01", "explosions/tv_flatscreen_explosion",
                        "tag_fx", TransitionSoundName: "tv_shot_burst")
                ]),
            ModelNames = ["ma_flatscreen_tv_wallmount_01", "ma_flatscreen_tv_wallmount_broken_01"],
            FxNames = ["explosions/tv_flatscreen_explosion"],
            SoundNames = ["tv_shot_burst"]
        },
        new("Running Generator", "Running exhaust, smoke and sparks lead to an exploding generator.",
            "machinery_generator", "toy_generator_on", "destructible_toy")
        {
            Category = "Machinery",
            Family = "Generators",
            Variant = "Running",
            SoundCsvInclude = "destruct_generator",
            PrecacheScript = "common_scripts _destructible_types_anim_generator",
            AnimationNames = ["generator_explode", "generator_explode_02", "generator_explode_03", "generator_vibration"],
            Preview = new(
                [
                    new("Running", FxName: "smoke/generator_exhaust", FxTag: "tag_fx2",
                        SoundName: "generator_running", RepeatFx: true),
                    new("White smoke", FxName: "smoke/generator_damage_whitesmoke", FxTag: "tag_fx2",
                        SoundName: "generator_running", RepeatFx: true),
                    new("Black smoke", FxName: "smoke/generator_damage_blacksmoke", FxTag: "tag_fx2",
                        SoundName: "generator_damage_loop", RepeatFx: true),
                    new("Sparking", FxName: "explosions/generator_spark_runner", FxTag: "tag_fx4",
                        SoundName: "generator_spark_loop", RepeatFx: true),
                    new("Destroyed", "machinery_generator_des", "explosions/generator_explosion", "tag_fx",
                        TransitionSoundName: "generator01_explode")
                ]),
            ModelNames = ["machinery_generator", "machinery_generator_des"],
            FxNames =
            [
                "smoke/generator_exhaust", "smoke/generator_damage_whitesmoke", "smoke/generator_damage_blacksmoke",
                "explosions/generator_spark_runner", "explosions/generator_explosion", "fire/generator_des_fire"
            ],
            SoundNames =
            [
                "generator_running", "generator_damage_loop", "generator_spark_loop", "generator01_explode"
            ]
        },
        .. StockToys(),
        .. StockVehicles()
    ];

    private static IEnumerable<DestructiblePreset> StockToys()
    {
        yield return StockToy("Electric Box 2", "Electrical box that sparks and breaks into a damaged shell.",
            "toy_electricbox2", "me_electricbox2", "destructible_electricalbox2", "destruct_spark_box", "Electronics",
            [new("Intact"), new("Destroyed", "me_electricbox2_dest", "props/electricbox4_explode", "tag_fx",
                TransitionSoundName: "exp_fusebox_sparks")],
            ["me_electricbox2_dest", "me_electricbox2_door", "me_electricbox2_door_upper"],
            ["props/electricbox4_explode"], ["exp_fusebox_sparks"], "Electric Boxes", "2");
        yield return StockToy("Electric Box 4", "Electrical box that sparks and breaks into a damaged shell.",
            "toy_electricbox4", "me_electricbox4", "destructible_electricalbox4", "destruct_spark_box", "Electronics",
            [new("Intact"), new("Destroyed", "me_electricbox4_dest", "props/electricbox4_explode", "tag_fx",
                TransitionSoundName: "exp_fusebox_sparks")],
            ["me_electricbox4_dest", "me_electricbox4_door"],
            ["props/electricbox4_explode"], ["exp_fusebox_sparks"], "Electric Boxes", "4");
        yield return StockToy("File Cabinet", "Cabinet that dents and breaks, with loose drawers.",
            "toy_filecabinet", "com_filecabinetblackclosed", "destructible_filecabinet", "destruct_filecabinet", "Furniture",
            [new("Intact"), new("Damaged", "com_filecabinetblackclosed_dam", "props/filecabinet_dam",
                "tag_drawer_lower", TransitionSoundName: "exp_filecabinet"),
                new("Destroyed", "com_filecabinetblackclosed_des", "props/filecabinet_des", "tag_drawer_upper",
                    TransitionSoundName: "exp_filecabinet")],
            ["com_filecabinetblackclosed_dam", "com_filecabinetblackclosed_des", "com_filecabinetblackclosed_drawer"],
            ["props/filecabinet_dam", "props/filecabinet_des"], ["exp_filecabinet"]);
        yield return StockToy("Fire Hydrant", "Hydrant that leaks, sprays and bursts when damaged.",
            "toy_firehydrant", "com_firehydrant", "destructible_firehydrant", "destruct_firehydrant", "Street Props",
            [new("Intact"), new("Leaking", FxName: "props/firehydrant_leak", FxTag: "tag_cap",
                SoundName: "firehydrant_spray_loop", RepeatFx: true),
                new("Destroyed", "com_firehydrant_dest", "props/firehydrant_exp", "tag_fx",
                    TransitionSoundName: "firehydrant_burst")],
            ["com_firehydrant_dest", "com_firehydrant_dam", "com_firehydrant_cap"],
            ["props/firehydrant_leak", "props/firehydrant_exp", "props/firehydrant_spray_10sec"],
            ["firehydrant_spray_loop", "firehydrant_burst"]);
        yield return StockToy("Newspaper Stand (Red)", "Newspaper box that spills papers and breaks apart.",
            "toy_newspaper_stand_red", "com_newspaperbox_red", "destructible_newspaperbox_red", "destruct_newspaperbox", "Street Props",
            [new("Intact"), new("Damaged", "com_newspaperbox_red_dam", "props/news_stand_paper_spill",
                "tag_door", TransitionSoundName: "exp_newspaper_box"),
                new("Destroyed", "com_newspaperbox_red_des", "props/news_stand_explosion", "tag_fx")],
            ["com_newspaperbox_red_dam", "com_newspaperbox_red_des", "com_newspaperbox_red_door"],
            ["props/news_stand_paper_spill", "props/news_stand_explosion"], ["exp_newspaper_box"],
            "Newspaper Stands", "Red");
        yield return StockToy("Newspaper Stand (Blue)", "Newspaper box that spills papers and breaks apart.",
            "toy_newspaper_stand_blue", "com_newspaperbox_blue", "destructible_newspaperbox_blue", "destruct_newspaperbox", "Street Props",
            [new("Intact"), new("Damaged", "com_newspaperbox_blue_dam", "props/news_stand_paper_spill_shatter",
                "tag_door", TransitionSoundName: "exp_newspaper_box"),
                new("Destroyed", "com_newspaperbox_blue_des", "props/news_stand_explosion", "tag_fx")],
            ["com_newspaperbox_blue_dam", "com_newspaperbox_blue_des", "com_newspaperbox_blue_door"],
            ["props/news_stand_paper_spill_shatter", "props/news_stand_explosion"], ["exp_newspaper_box"],
            "Newspaper Stands", "Blue");
        yield return StockToy("Trash Bin 01", "Trash bin that bursts and loses its lid.",
            "toy_trashbin_01", "com_trashbin01", "destructible_trashbin_01", "destruct_trashcan", "Street Props",
            [new("Intact"), new("Damaged", "com_trashbin01_dmg", "props/garbage_spew", "tag_fx",
                TransitionSoundName: "exp_trashcan_sweet")],
            ["com_trashbin01_dmg", "com_trashbin01_lid"],
            ["props/garbage_spew_des", "props/garbage_spew"], ["exp_trashcan_sweet"], "Trash Bins", "01");
        yield return StockToy("Trash Bin 02", "Trash bin that bursts and loses its lid.",
            "toy_trashbin_02", "com_trashbin02", "destructible_trashbin_02", "destruct_trashcan", "Street Props",
            [new("Intact"), new("Damaged", "com_trashbin02_dmg", "props/garbage_spew", "tag_fx",
                TransitionSoundName: "exp_trashcan_sweet")],
            ["com_trashbin02_dmg", "com_trashbin02_lid"],
            ["props/garbage_spew_des", "props/garbage_spew"], ["exp_trashcan_sweet"], "Trash Bins", "02");
        yield return StockToy("Tube TV 1", "Tube television that shatters in one hit.",
            "toy_tubetv_tv1", "com_tv1", "destructible_tubetv_tv1", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "com_tv1_d", "explosions/tv_explosion", "tag_fx",
                TransitionSoundName: "tv_shot_burst")],
            ["com_tv1_d"], ["explosions/tv_explosion"], ["tv_shot_burst"], "Tube TVs", "TV 1");
        yield return StockToy("Tube TV 1 (Test Pattern)", "Test-pattern tube television that shatters in one hit.",
            "toy_tubetv_tv1", "com_tv1_testpattern", "destructible_tubetv_tv1", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "com_tv1_d", "explosions/tv_explosion", "tag_fx",
                TransitionSoundName: "tv_shot_burst")],
            ["com_tv1_d"], ["explosions/tv_explosion"], ["tv_shot_burst"], "Tube TVs", "TV 1 Test Pattern");
        yield return StockToy("Tube TV 2", "Tube television that shatters in one hit.",
            "toy_tubetv_tv2", "com_tv2", "destructible_tubetv_tv2", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "com_tv2_d", "explosions/tv_explosion", "tag_fx",
                TransitionSoundName: "tv_shot_burst")],
            ["com_tv2_d"], ["explosions/tv_explosion"], ["tv_shot_burst"], "Tube TVs", "TV 2");
        yield return StockToy("Flatscreen TV 01", "Flatscreen television that shatters in one hit.",
            "toy_tv_flatscreen_01", "ma_flatscreen_tv_01", "destructible_flatscreen_tv_01", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "ma_flatscreen_tv_broken_01", "explosions/tv_flatscreen_explosion",
                "tag_fx", TransitionSoundName: "tv_shot_burst")],
            ["ma_flatscreen_tv_broken_01"], ["explosions/tv_flatscreen_explosion"], ["tv_shot_burst"]);
        yield return StockToy("Wallmount Flatscreen TV 02", "Wall-mounted flatscreen that shatters in one hit.",
            "toy_tv_flatscreen_wallmount_02", "ma_flatscreen_tv_wallmount_02", "destructible_flatscreen_tv_wallmount_02", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "ma_flatscreen_tv_wallmount_broken_02", "explosions/tv_flatscreen_explosion",
                "tag_fx", TransitionSoundName: "tv_shot_burst")],
            ["ma_flatscreen_tv_wallmount_broken_02"], ["explosions/tv_flatscreen_explosion"], ["tv_shot_burst"],
            "Wallmount Flatscreen TVs", "02");
        yield return StockToy("Wallmount Flatscreen TV 02 (On)", "Powered wall-mounted flatscreen that shatters in one hit.",
            "toy_tv_flatscreen_wallmount_02", "ma_flatscreen_tv_on_wallmount_02", "destructible_flatscreen_tv_wallmount_02", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "ma_flatscreen_tv_wallmount_broken_02", "explosions/tv_flatscreen_explosion",
                "tag_fx", TransitionSoundName: "tv_shot_burst")],
            ["ma_flatscreen_tv_wallmount_broken_02"], ["explosions/tv_flatscreen_explosion"], ["tv_shot_burst"],
            "Wallmount Flatscreen TVs", "02 On");
        yield return StockToy("Wallmount Flatscreen TV 02 (On Static)", "Static-screen wall-mounted flatscreen that shatters in one hit.",
            "toy_tv_flatscreen_wallmount_02", "ma_flatscreen_tv_on_wallmount_02_static", "destructible_flatscreen_tv_wallmount_02", "destruct_tv_exp", "Electronics",
            [new("Intact"), new("Broken", "ma_flatscreen_tv_wallmount_broken_02", "explosions/tv_flatscreen_explosion",
                "tag_fx", TransitionSoundName: "tv_shot_burst")],
            ["ma_flatscreen_tv_wallmount_broken_02"], ["explosions/tv_flatscreen_explosion"], ["tv_shot_burst"],
            "Wallmount Flatscreen TVs", "02 On Static");
        yield return StockToy("Dressing Table Mirror", "Mirror that shatters into damaged and broken states.",
            "toy_dt_mirror", "dt_mirror", "destructible_dt_mirror", "destruct_mirror", "Furniture",
            [new("Intact"), new("Damaged", "dt_mirror_dam", "props/mirror_shatter", "tag_fx",
                TransitionSoundName: "mirror_shatter"),
                new("Broken", "dt_mirror_des", "props/mirror_dt_panel_broken", "tag_fx")],
            ["dt_mirror_dam", "dt_mirror_des"], ["props/mirror_shatter", "props/mirror_dt_panel_broken"],
            ["mirror_shatter"], "Dressing Table Mirrors", "Standard");
        yield return StockToy("Dressing Table Mirror (Large)", "Large mirror that shatters into damaged and broken states.",
            "toy_dt_mirror_large", "dt_mirror_large", "destructible_dt_mirror_large", "destruct_mirror", "Furniture",
            [new("Intact"), new("Damaged", "dt_mirror_large_dam", "props/mirror_shatter_large", "tag_fx",
                TransitionSoundName: "mirror_shatter"),
                new("Broken", "dt_mirror_large_des", "props/mirror_dt_panel_large_broken", "tag_fx")],
            ["dt_mirror_large_dam", "dt_mirror_large_des"],
            ["props/mirror_shatter_large", "props/mirror_dt_panel_large_broken"], ["mirror_shatter"],
            "Dressing Table Mirrors", "Large");
        yield return StockToy("Water Collector", "Water collector that bursts into a damaged base.",
            "toy_water_collector", "utility_water_collector", "destructible_water_collector", "destruct_water_collector", "Street Props",
            [new("Intact"), new("Destroyed", "utility_water_collector_base_dest", "explosions/water_collector_explosion",
                "tag_fx", TransitionSoundName: "water_collector_splash")],
            ["utility_water_collector_base_dest"], ["explosions/water_collector_explosion"], ["water_collector_splash"]);
        yield return StockToy(
            "Large Electrical Transformer",
            "Sparks and catches fire before its cabinet explodes.",
            "destructible_electrical_transformer_large",
            "com_electrical_transformer_large_dam",
            "destructible_electrical_transformer_large",
            "destruct_electrical_transformer_large",
            "Machinery",
            [new("Intact"), new("Destroyed", "com_electrical_transformer_large_des")],
            [
                "com_electrical_transformer_large_dam_door1",
                "com_electrical_transformer_large_dam_door2",
                "com_electrical_transformer_large_dam_door3",
                "com_electrical_transformer_large_dam_door4",
                "com_electrical_transformer_large_dam_door5",
                "com_electrical_transformer_large_dam_door6",
                "com_electrical_transformer_large_dam_door7",
                "com_electrical_transformer_large_des"
            ],
            [
                "explosions/electrical_transformer_explosion",
                "explosions/electrical_transformer_spark_runner",
                "explosions/generator_explosion",
                "explosions/generator_spark_runner",
                "explosions/generator_sparks_c",
                "fire/electrical_transformer_blacksmoke_fire",
                "props/electricbox4_explode"
            ],
            ["electrical_transformer01_explode", "electrical_transformer01_explode_detail", "electrical_transformer_sparks"]
        );
        yield return StockToy(
            "Gas Pump 01",
            "Smoke and fire lead to an explosion; body panels can break away.",
            "destructible_gaspump",
            "furniture_gaspump01_damaged",
            "destructible_gaspump01",
            "destruct_gaspump",
            "Street Props",
            [new("Intact"), new("Destroyed", "furniture_gaspump01_destroyed")],
            [
                "furniture_gaspump01_destroyed",
                "furniture_gaspump01_panel01",
                "furniture_gaspump01_panel02",
                "furniture_gaspump01_panel03"
            ],
            [
                "explosions/gas_pump_exp",
                "fire/gas_pump_fire_damage",
                "fire/gas_pump_fire_handle",
                "props/electricbox4_explode",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_whitesmoke"
            ],
            ["exp_gaspump_sparks", "gaspump01_explode", "gaspump01_fire_med", "gaspump01_flareup_med"]
        );
        yield return StockToy(
            "Air Conditioner",
            "Air conditioner fan and housing react to damage; preview omits fan animation.",
            "toy_airconditioner",
            "com_ex_airconditioner",
            "destructible_airconditioner_ex",
            "destruct_airconditioner",
            "Machinery",
            [new("Intact"), new("Damaged", "com_ex_airconditioner_dam")],
            ["com_ex_airconditioner_dam", "com_ex_airconditioner_fan"],
            ["explosions/airconditioner_ex_explode"],
            ["airconditioner_burst", "airconditioner_running_loop"],
            precacheScript: "common_scripts _destructible_types_anim_airconditioner",
            animations: ["ex_airconditioner_fan"]
        );
        yield return StockToy(
            "Ceiling Fan",
            "Spinning ceiling fan that breaks; preview omits spin animation.",
            "toy_ceiling_fan",
            "me_fanceil1",
            "destructible_ceiling_fan",
            "destruct_ceiling_fan",
            "Machinery",
            [new("Intact"), new("Destroyed", "me_fanceil1_des")],
            ["me_fanceil1_des"],
            ["explosions/ceiling_fan_explosion"],
            ["ceiling_fan_sparks"],
            precacheScript: "common_scripts _destructible_types_anim_me_fanceil1_spin",
            animations: ["me_fanceil1_spin", "me_fanceil1_spin_stop"]
        );
        yield return StockToy(
            "Chicken (Black and White)",
            "Animated chicken cage; preview shows the intact model without cage animation.",
            "toy_chicken_black_white",
            "chicken_black_white",
            "destructible_chicken",
            "destruct_chicken",
            "Animals",
            [new("Intact")],
            [],
            ["props/chicken_exp_black_white"],
            ["animal_chicken_death", "animal_chicken_idle_loop"],
            "Chickens",
            "Black and White",
            precacheScript: "common_scripts _destructible_types_anim_chicken",
            animations: ["chicken_cage_death", "chicken_cage_death_02", "chicken_cage_loop_01", "chicken_cage_loop_02"]
        );
        yield return StockToy(
            "Chicken (White)",
            "Animated chicken cage; preview shows the intact model without cage animation.",
            "toy_chicken_white",
            "chicken_white",
            "destructible_chicken",
            "destruct_chicken",
            "Animals",
            [new("Intact")],
            [],
            ["props/chicken_exp_white"],
            ["animal_chicken_death", "animal_chicken_idle_loop"],
            "Chickens",
            "White",
            precacheScript: "common_scripts _destructible_types_anim_chicken",
            animations: ["chicken_cage_death", "chicken_cage_death_02", "chicken_cage_loop_01", "chicken_cage_loop_02"]
        );
        yield return StockToy(
            "Photocopier",
            "Photocopier that breaks into a destroyed shell.",
            "toy_copier",
            "prop_photocopier_destructible_02",
            "destructible_copier",
            "destruct_copier",
            "Electronics",
            [new("Intact"), new("Destroyed", "prop_photocopier_destroyed")],
            [
                "prop_photocopier_destroyed",
                "prop_photocopier_destroyed_left_feeder",
                "prop_photocopier_destroyed_right_shelf",
                "prop_photocopier_destroyed_top"
            ],
            [
                "props/photocopier_exp",
                "props/photocopier_fire",
                "props/photocopier_sparks",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_whitesmoke"
            ],
            ["copier_exp", "copier_fire_loop", "copier_spark_loop"]
        );
        yield return StockToy(
            "Generator (Off)",
            "Generator that smokes and explodes; preview omits its destruction animation.",
            "toy_generator",
            "machinery_generator",
            "destructible_generator",
            "destruct_generator",
            "Machinery",
            [new("Intact"), new("Destroyed", "machinery_generator_des")],
            ["machinery_generator_des"],
            [
                "explosions/generator_explosion",
                "explosions/generator_spark_runner",
                "fire/generator_des_fire",
                "smoke/generator_damage_blacksmoke",
                "smoke/generator_damage_whitesmoke"
            ],
            ["generator01_explode", "generator_spark_loop"],
            "Generators",
            "Off",
            precacheScript: "common_scripts _destructible_types_anim_generator",
            animations: ["generator_explode", "generator_explode_02", "generator_explode_03", "generator_vibration"]
        );
        yield return StockToy(
            "Fluorescent Ceiling Light",
            "Double fluorescent fixture that swings and breaks; preview omits swing animation.",
            "toy_light_ceiling_fluorescent",
            "me_lightfluohang_double",
            "destructible_light_fluorescent_on",
            "destruct_fluorescent_light",
            "Lighting",
            [new("Intact"), new("Destroyed", "me_lightfluohang_double_destroyed")],
            ["me_lightfluohang_double_destroyed"],
            ["misc/light_blowout_swinging_runner", "misc/light_fluorescent_blowout_runner", "misc/fluorescent_spotlight"],
            ["fluorescent_light_bulb", "fluorescent_light_fall"],
            "Fluorescent Ceiling Lights",
            "Double Off",
            precacheScript: "common_scripts _destructible_types_anim_light_fluo_on",
            modelScale: 1.1f
        );
        yield return StockToy(
            "Fluorescent Ceiling Light (On)",
            "Double fluorescent fixture that swings and breaks; preview omits swing animation.",
            "toy_light_ceiling_fluorescent",
            "me_lightfluohang_double_on",
            "destructible_light_fluorescent_on",
            "destruct_fluorescent_light",
            "Lighting",
            [new("Intact"), new("Destroyed", "me_lightfluohang_double_destroyed")],
            ["me_lightfluohang_double_destroyed"],
            ["misc/light_blowout_swinging_runner", "misc/light_fluorescent_blowout_runner", "misc/fluorescent_spotlight"],
            ["fluorescent_light_bulb", "fluorescent_light_fall"],
            "Fluorescent Ceiling Lights",
            "Double On",
            precacheScript: "common_scripts _destructible_types_anim_light_fluo_on",
            modelScale: 1.1f
        );
        yield return StockToy(
            "Single Fluorescent Ceiling Light",
            "Single fluorescent fixture that swings and breaks; preview omits swing animation.",
            "toy_light_ceiling_fluorescent_single",
            "me_lightfluohang",
            "destructible_light_fluorescent_single",
            "destruct_fluorescent_light",
            "Lighting",
            [new("Intact"), new("Destroyed", "me_lightfluohang_single_destroyed")],
            ["me_lightfluohang_single_destroyed"],
            ["misc/light_blowout_swinging_runner", "misc/light_fluorescent_single_blowout_runner", "misc/fluorescent_spotlight"],
            ["fluorescent_light_bulb", "fluorescent_light_fall", "fluorescent_light_hinge"],
            "Fluorescent Ceiling Lights",
            "Single Off",
            precacheScript: "common_scripts _destructible_types_anim_light_fluo_single",
            modelScale: 1.1f
        );
        yield return StockToy(
            "Single Fluorescent Ceiling Light (On)",
            "Single fluorescent fixture that swings and breaks; preview omits swing animation.",
            "toy_light_ceiling_fluorescent_single",
            "me_lightfluohang_on",
            "destructible_light_fluorescent_single",
            "destruct_fluorescent_light",
            "Lighting",
            [new("Intact"), new("Destroyed", "me_lightfluohang_single_destroyed")],
            ["me_lightfluohang_single_destroyed"],
            ["misc/light_blowout_swinging_runner", "misc/light_fluorescent_single_blowout_runner", "misc/fluorescent_spotlight"],
            ["fluorescent_light_bulb", "fluorescent_light_fall", "fluorescent_light_hinge"],
            "Fluorescent Ceiling Lights",
            "Single On",
            precacheScript: "common_scripts _destructible_types_anim_light_fluo_single",
            modelScale: 1.1f
        );
        yield return StockToy(
            "Double Locker",
            "Locker doors break with several animations; preview omits door motion.",
            "toy_locker_double",
            "com_locker_double",
            "destructible_locker_double",
            "destruct_lockers",
            "Furniture",
            [new("Intact"), new("Destroyed", "com_locker_double_destroyed")],
            ["com_locker_double_destroyed"],
            [
                "misc/no_effect",
                "props/locker_double_des_01_left",
                "props/locker_double_des_02_right",
                "props/locker_double_des_03_both"
            ],
            ["lockers_double", "lockers_fast", "lockers_minor"],
            precacheScript: "common_scripts _destructible_types_anim_lockers",
            animations: ["locker_broken_both_doors_1", "locker_broken_both_doors_2", "locker_broken_both_doors_3",
                "locker_broken_both_doors_4", "locker_broken_door1_fast", "locker_broken_door1_slow",
                "locker_broken_door2_fast", "locker_broken_door2_slow"]
        );
        yield return StockToy(
            "Oxygen Tank 01",
            "Oxygen tank with damaged and exploded shell states.",
            "toy_oxygen_tank_01",
            "machinery_oxygen_tank01",
            "destructible_oxygen_tank",
            "destruct_oxygen_tank",
            "Machinery",
            [new("Intact"), new("Destroyed", "machinery_oxygen_tank01_des")],
            ["machinery_oxygen_tank01_dam", "machinery_oxygen_tank01_des"],
            ["distortion/oxygen_tank_leak", "explosions/oxygen_tank01_explosion", "props/oxygen_tank01_cap"],
            ["oxygen_tank_explode", "oxygen_tank_leak_loop"],
            "Oxygen Tanks",
            "01"
        );
        yield return StockToy(
            "Oxygen Tank 02",
            "Oxygen tank with damaged and exploded shell states.",
            "toy_oxygen_tank_02",
            "machinery_oxygen_tank02",
            "destructible_oxygen_tank",
            "destruct_oxygen_tank",
            "Machinery",
            [new("Intact"), new("Destroyed", "machinery_oxygen_tank02_des")],
            ["machinery_oxygen_tank02_dam", "machinery_oxygen_tank02_des"],
            ["distortion/oxygen_tank_leak", "explosions/oxygen_tank02_explosion", "props/oxygen_tank02_cap"],
            ["oxygen_tank_explode", "oxygen_tank_leak_loop"],
            "Oxygen Tanks",
            "02"
        );
        yield return StockToy(
            "Propane Tank",
            "Propane tank that bursts into a broken shell.",
            "toy_propane_tank02",
            "com_propane_tank02",
            "destructible_propane_tank02",
            "destruct_large_propane_tank",
            "Machinery",
            [new("Intact"), new("Destroyed", "com_propane_tank02_des")],
            ["com_propane_tank02_des", "com_propane_tank02_cap", "com_propane_tank02_valve"],
            [
                "distortion/propane_cap_distortion",
                "explosions/propane_large_exp",
                "explosions/propane_large_exp_fireball",
                "fire/propane_capfire",
                "fire/propane_capfire_flareup",
                "fire/propane_capfire_leak",
                "fire/propane_small_fire",
                "fire/propane_valvefire",
                "fire/propane_valvefire_flareup"
            ],
            [
                "propanetank02_explode",
                "propanetank02_fire_blown_med",
                "propanetank02_fire_med",
                "propanetank02_flareup2_med",
                "propanetank02_flareup_med",
                "propanetank02_gas_leak_loop"
            ],
            "Propane Tanks",
            "Large"
        );
        yield return StockToy(
            "Small Propane Tank",
            "Small propane tank that bursts into a broken shell.",
            "toy_propane_tank02_small",
            "com_propane_tank02_small",
            "destructible_propane_tank02_small",
            "destruct_large_propane_tank",
            "Machinery",
            [new("Intact"), new("Destroyed", "com_propane_tank02_small_des")],
            ["com_propane_tank02_small_des", "com_propane_tank02_small_cap", "com_propane_tank02_small_valve"],
            [
                "distortion/propane_cap_distortion",
                "explosions/propane_large_exp",
                "fire/propane_capfire",
                "fire/propane_capfire_flareup",
                "fire/propane_capfire_leak",
                "fire/propane_small_fire",
                "fire/propane_valvefire",
                "fire/propane_valvefire_flareup"
            ],
            ["propanetank02_explode", "propanetank02_fire_med", "propanetank02_flareup_med", "propanetank02_gas_leak_loop"],
            "Propane Tanks",
            "Small"
        );
        yield return StockToy(
            "Ratnest Transformer",
            "Electrical transformer that sparks and breaks.",
            "toy_transformer_ratnest01",
            "utility_transformer_ratnest01",
            "destructible_transformer_ratnest01",
            "destruct_transformer",
            "Machinery",
            [new("Intact"), new("Destroyed", "utility_transformer_ratnest01_dest")],
            ["utility_transformer_ratnest01_dest"],
            [
                "explosions/transformer_explosion",
                "explosions/transformer_spark_runner",
                "fire/firelp_small_pm",
                "fire/transformer_blacksmoke_fire",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_whitesmoke"
            ],
            ["transformer01_explode", "transformer01_flareup_med", "transformer_spark_loop"],
            "Utility Transformers",
            "Ratnest"
        );
        yield return StockToy(
            "Small Transformer",
            "Small electrical transformer that sparks and breaks.",
            "toy_transformer_small01",
            "utility_transformer_small01",
            "destructible_transformer_small01",
            "destruct_transformer",
            "Machinery",
            [new("Intact"), new("Destroyed", "utility_transformer_small01_dest")],
            ["utility_transformer_small01_dest"],
            [
                "explosions/transformer_explosion",
                "explosions/transformer_spark_runner",
                "fire/firelp_small_pm",
                "fire/transformer_small_blacksmoke_fire",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_whitesmoke"
            ],
            ["transformer01_explode", "transformer01_flareup_med", "transformer_spark_loop"],
            "Utility Transformers",
            "Small"
        );
        yield return StockToy(
            "Closed Metal Trash Can",
            "Metal trash can that bursts and loses its lid.",
            "toy_trashcan_metal_closed",
            "com_trashcan_metal_closed",
            "destructible_trashcan_metal_closed",
            "destruct_trashcan",
            "Street Props",
            [new("Intact"), new("Destroyed", "com_trashcan_metal_with_trash")],
            ["com_trashcan_metallid", "com_trashcan_metal_with_trash"],
            ["props/garbage_spew", "props/garbage_spew_des"],
            ["exp_trashcan_sweet"]
        );
        yield return StockToy(
            "Gas Station Trash Bin 01",
            "Gas station trash bin that bursts and loses its lid.",
            "toy_usa_gas_station_trash_bin_01",
            "usa_gas_station_trash_bin_01",
            "destructible_usa_gas_station_trash_bin_01",
            "destruct_trashcan",
            "Street Props",
            [new("Intact"), new("Destroyed", "usa_gas_station_trash_bin_01_base")],
            ["usa_gas_station_trash_bin_01_base", "usa_gas_station_trash_bin_01_lid"],
            ["props/garbage_spew", "props/garbage_spew_des"],
            [],
            "Gas Station Trash Bins",
            "01"
        );
        yield return StockToy(
            "Gas Station Trash Bin 02",
            "Gas station trash bin that bursts and loses its lid.",
            "toy_usa_gas_station_trash_bin_02",
            "usa_gas_station_trash_bin_02",
            "destructible_usa_gas_station_trash_bin_02",
            "destruct_trashcan",
            "Street Props",
            [new("Intact"), new("Destroyed", "usa_gas_station_trash_bin_02_base")],
            ["usa_gas_station_trash_bin_02_base", "usa_gas_station_trash_bin_02_lid"],
            ["props/garbage_spew", "props/garbage_spew_des"],
            [],
            "Gas Station Trash Bins",
            "02"
        );
        yield return StockToy(
            "Wall Fan",
            "Wall fan that stops and breaks when hit; preview omits rotation animation.",
            "toy_wall_fan",
            "cs_wallfan1",
            "destructible_wallfan",
            "destruct_wall_fan",
            "Machinery",
            [new("Intact"), new("Damaged", "cs_wallfan1_dmg")],
            ["cs_wallfan1_dmg"],
            ["explosions/wallfan_explosion_des", "explosions/wallfan_explosion_dmg"],
            ["wall_fan_break", "wall_fan_fanning", "wall_fan_sparks"],
            precacheScript: "common_scripts _destructible_types_anim_wallfan",
            animations: ["wall_fan_rotate", "wall_fan_wobble", "wall_fan_stop"]
        );
    }

    private static IEnumerable<DestructiblePreset> StockVehicles()
    {
        yield return StockVehicle(
            "80s Wagon (Red)",
            "Breakable red wagon with tire and glass damage, smoke, fire and a wreck.",
            "vehicle_80s_wagon1_red",
            "vehicle_80s_wagon1_red_destructible_mp",
            "destructible_vehicle_80s_wagon1_red_destructible_mp",
            "vehicle_car_exp",
            "vehicle_80s_wagon1_red_destroyed",
            [
                "vehicle_80s_wagon1_red_bumper_f",
                "vehicle_80s_wagon1_red_destroyed",
                "vehicle_80s_wagon1_red_door_rb",
                "vehicle_80s_wagon1_red_hood",
                "vehicle_80s_wagon1_red_mirror_l",
                "vehicle_80s_wagon1_red_mirror_r",
                "vehicle_80s_wagon1_red_wheel_lf"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_brakelight",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ]
        );
        yield return StockVehicle(
            "BM21 Covered Truck",
            "Covered BM21 truck with staged damage and a wreck model.",
            "vehicle_bm21_cover",
            "vehicle_bm21_cover_destructible",
            "destructible_vehicle_bm21_cover",
            null,
            "vehicle_bm21_mobile_cover_dstry",
            ["vehicle_bm21_mobile_cover_dstry"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            ["car_explode", "fire_vehicle_flareup_med", "fire_vehicle_med", "veh_glass_break_large", "veh_tire_deflate"],
            "BM21 Trucks",
            "Covered"
        );
        yield return StockVehicle(
            "BM21 Mobile Bed Truck",
            "BM21 mobile bed truck with staged damage and a wreck model.",
            "vehicle_bm21_mobile_bed",
            "vehicle_bm21_mobile_bed_destructible",
            "destructible_vehicle_bm21_mobile_bed",
            null,
            "vehicle_bm21_mobile_bed_dstry",
            ["vehicle_bm21_mobile_bed_dstry"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            ["car_explode", "fire_vehicle_flareup_med", "fire_vehicle_med", "veh_glass_break_large", "veh_tire_deflate"],
            "BM21 Trucks",
            "Mobile Bed"
        );
        yield return StockVehicle(
            "Coupe (White)",
            "White coupe with staged damage and a wreck model.",
            "vehicle_coupe_white",
            "vehicle_coupe_white_destructible",
            "destructible_vehicle_coupe_white",
            "vehicle_car_exp",
            "vehicle_coupe_white_destroyed",
            [
                "vehicle_coupe_wheel_lf",
                "vehicle_coupe_white_destroyed",
                "vehicle_coupe_white_door_lf",
                "vehicle_coupe_white_mirror_l",
                "vehicle_coupe_white_mirror_r",
                "vehicle_coupe_white_spoiler"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ]
        );
        yield return StockVehicle(
            "Hummer",
            "Hummer with staged damage and a wreck model.",
            "vehicle_hummer",
            "vehicle_hummer_destructible",
            "destructible_vehicle_hummer_destructible",
            null,
            "vehicle_hummer_destroyed",
            ["vehicle_hummer_destroyed"],
            [
                "explosions/vehicle_explosion_hummer",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            ["car_explode", "fire_vehicle_flareup_med", "fire_vehicle_med", "veh_glass_break_large", "veh_tire_deflate"]
        );
        yield return StockVehicle(
            "2008 Luxury Sedan",
            "Luxury sedan with staged damage and a wreck model.",
            "vehicle_luxurysedan_2008",
            "vehicle_luxurysedan_2008_destructible",
            "destructible_luxurysedan_2008",
            "vehicle_car_exp",
            "vehicle_luxurysedan_2008_destroy",
            [
                "vehicle_luxurysedan_2008_destroy",
                "vehicle_luxurysedan_2008_door_lb",
                "vehicle_luxurysedan_2008_door_lf",
                "vehicle_luxurysedan_2008_door_rb",
                "vehicle_luxurysedan_2008_door_rf",
                "vehicle_luxurysedan_2008_hood",
                "vehicle_luxurysedan_2008_mirror_l",
                "vehicle_luxurysedan_2008_mirror_r",
                "vehicle_luxurysedan_2008_wheel_lf"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ]
        );
        yield return StockVehicle(
            "Moving Truck",
            "Moving truck with staged damage and a wreck model.",
            "vehicle_moving_truck",
            "vehicle_moving_truck_destructible",
            "destructible_vehicle_moving_truck",
            null,
            "vehicle_moving_truck_dst",
            ["vehicle_moving_truck_dst"],
            [
                "explosions/vehicle_explosion_medium",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            ["car_explode", "fire_vehicle_flareup_med", "fire_vehicle_med", "veh_glass_break_large", "veh_tire_deflate"]
        );
        yield return StockVehicle(
            "Pickup Truck",
            "Pickup with breakable parts, staged damage and a wreck model.",
            "vehicle_pickup",
            "vehicle_pickup_destructible_mp",
            "destructible_vehicle_pickup_destructible_mp",
            null,
            "vehicle_pickup_destroyed",
            [
                "vehicle_pickup_destroyed",
                "vehicle_pickup_door_lf",
                "vehicle_pickup_door_rf",
                "vehicle_pickup_hood",
                "vehicle_pickup_mirror_l",
                "vehicle_pickup_mirror_r"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_brakelight",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ]
        );
        yield return StockVehicle(
            "Small Hatchback (Turquoise)",
            "Turquoise hatchback with breakable parts, staged damage and a wreck.",
            "vehicle_small_hatch_turq",
            "vehicle_small_hatch_turq_destructible_mp",
            "destructible_vehicle_small_hatch_turq_destructible_mp",
            "vehicle_car_exp",
            "vehicle_small_hatch_turq_destroyed",
            [
                "vehicle_small_hatch_turq_destroyed",
                "vehicle_small_hatch_turq_door_lf",
                "vehicle_small_hatch_turq_door_rf",
                "vehicle_small_hatch_turq_hood",
                "vehicle_small_hatch_turq_mirror_l",
                "vehicle_small_hatch_turq_mirror_r"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_brakelight",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Small Hatchbacks",
            "Turquoise",
            smallHatchPreview: true
        );
        yield return StockVehicle(
            "Small Hatchback (White)",
            "White hatchback with breakable parts, staged damage and a wreck.",
            "vehicle_small_hatch_white",
            "vehicle_small_hatch_white_destructible_mp",
            "destructible_vehicle_small_hatch_white_destructible_mp",
            null,
            "vehicle_small_hatch_white_destroyed",
            [
                "vehicle_small_hatch_white_destroyed",
                "vehicle_small_hatch_white_door_lf",
                "vehicle_small_hatch_white_door_rf",
                "vehicle_small_hatch_white_hood",
                "vehicle_small_hatch_white_mirror_l",
                "vehicle_small_hatch_white_mirror_r"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_brakelight",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Small Hatchbacks",
            "White",
            smallHatchPreview: true
        );
        yield return StockVehicle(
            "Suburban (Beige)",
            "Beige Suburban with staged damage and a wreck model.",
            "vehicle_suburban_beige",
            "vehicle_suburban_destructible_beige",
            "destructible_vehicle_suburban_beige",
            "vehicle_car_exp",
            "vehicle_suburban_destroyed_beige",
            ["vehicle_suburban_destroyed_beige", "vehicle_suburban_door_lb_beige", "vehicle_suburban_wheel_rf"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Suburbans",
            "Beige"
        );
        yield return StockVehicle(
            "Suburban (Dull)",
            "Dull Suburban with staged damage and a wreck model.",
            "vehicle_suburban_dull",
            "vehicle_suburban_destructible_dull",
            "destructible_vehicle_suburban_dull",
            "vehicle_car_exp",
            "vehicle_suburban_destroyed_dull",
            ["vehicle_suburban_destroyed_dull", "vehicle_suburban_door_lb_dull", "vehicle_suburban_wheel_rf"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Suburbans",
            "Dull"
        );
        yield return StockVehicle(
            "Suburban (Red)",
            "Red Suburban with staged damage and a wreck model.",
            "vehicle_suburban_red",
            "vehicle_suburban_destructible_red",
            "destructible_vehicle_suburban_red",
            "vehicle_car_exp",
            "vehicle_suburban_destroyed_red",
            ["vehicle_suburban_destroyed_red", "vehicle_suburban_door_lb_red", "vehicle_suburban_wheel_rf"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Suburbans",
            "Red"
        );
        yield return StockVehicle(
            "Yellow Taxi",
            "Yellow taxi with breakable parts, staged damage and a wreck.",
            "vehicle_taxi",
            "vehicle_taxi_yellow_destructible",
            "destructible_vehicle_taxi",
            null,
            "vehicle_taxi_yellow_destroy",
            ["vehicle_taxi_mirror_l", "vehicle_taxi_mirror_r", "vehicle_taxi_wheel_lf", "vehicle_taxi_yellow_destroy"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ]
        );
        yield return StockVehicle(
            "UAZ (Open)",
            "Open UAZ with staged damage and a wreck model.",
            "vehicle_uaz_open",
            "vehicle_uaz_open_destructible",
            "destructible_uaz_open",
            "vehicle_car_exp",
            "vehicle_uaz_open_dsr",
            ["vehicle_uaz_mirror_l", "vehicle_uaz_mirror_r", "vehicle_uaz_open_dsr", "vehicle_uaz_wheel_lf_d"],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "UAZ",
            "Open"
        );
        yield return StockVehicle(
            "UAZ (Winter)",
            "Winter UAZ with staged damage and a wreck model.",
            "vehicle_uaz_winter",
            "vehicle_uaz_winter_destructible",
            "destructible_uaz_winter",
            "vehicle_car_exp",
            "vehicle_uaz_winter_destroy",
            [
                "vehicle_uaz_wheel_lf_d",
                "vehicle_uaz_wheel_rf_d",
                "vehicle_uaz_winter_destroy",
                "vehicle_uaz_winter_mirror_l",
                "vehicle_uaz_winter_mirror_r"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "UAZ",
            "Winter"
        );
        yield return StockVehicle(
            "Van (Green)",
            "Green van with staged damage and a wreck model.",
            "vehicle_van_green",
            "vehicle_van_green_destructible",
            "destructible_vehicle_van_green",
            "vehicle_car_exp",
            "vehicle_van_green_destroyed",
            [
                "vehicle_van_green_destroyed",
                "vehicle_van_green_door_rb",
                "vehicle_van_green_hood",
                "vehicle_van_green_mirror_l",
                "vehicle_van_green_mirror_r",
                "vehicle_van_wheel_lf"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Vans",
            "Green"
        );
        yield return StockVehicle(
            "Van (Slate)",
            "Slate van with staged damage and a wreck model.",
            "vehicle_van_slate",
            "vehicle_van_slate_destructible",
            "destructible_vehicle_van_slate",
            "vehicle_car_exp",
            "vehicle_van_slate_destroyed",
            [
                "vehicle_van_slate_destroyed",
                "vehicle_van_slate_door_rb",
                "vehicle_van_slate_hood",
                "vehicle_van_slate_mirror_l",
                "vehicle_van_slate_mirror_r",
                "vehicle_van_wheel_lf"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Vans",
            "Slate"
        );
        yield return StockVehicle(
            "Van (White)",
            "White van with staged damage and a wreck model.",
            "vehicle_van_white",
            "vehicle_van_white_destructible",
            "destructible_vehicle_van_white",
            "vehicle_car_exp",
            "vehicle_van_white_destroyed",
            [
                "vehicle_van_wheel_lf",
                "vehicle_van_white_destroyed",
                "vehicle_van_white_door_rb",
                "vehicle_van_white_hood",
                "vehicle_van_white_mirror_l",
                "vehicle_van_white_mirror_r"
            ],
            [
                "explosions/small_vehicle_explosion",
                "props/car_glass_headlight",
                "props/car_glass_large",
                "props/car_glass_med",
                "smoke/car_damage_blacksmoke",
                "smoke/car_damage_blacksmoke_fire",
                "smoke/car_damage_whitesmoke"
            ],
            [
                "car_explode",
                "fire_vehicle_flareup_med",
                "fire_vehicle_med",
                "veh_glass_break_large",
                "veh_glass_break_small",
                "veh_tire_deflate"
            ],
            "Vans",
            "White"
        );
    }

    private static DestructiblePreset StockToy(string name, string description, string type, string model,
        string csvInclude, string soundCsvInclude, string category, IReadOnlyList<DestructiblePreviewStage> stages,
        IReadOnlyList<string> otherModels, IReadOnlyList<string> fx, IReadOnlyList<string> sounds,
        string? family = null, string? variant = null, string? precacheScript = null,
        IReadOnlyList<string>? animations = null, float modelScale = 1) =>
        new(name, description, model, type, "destructible_toy", csvInclude)
        {
            Category = category,
            Family = family,
            Variant = variant,
            SoundCsvInclude = soundCsvInclude,
            PrecacheScript = precacheScript,
            AnimationNames = animations ?? [],
            ModelScale = modelScale,
            Preview = new(stages),
            ModelNames = [model, .. otherModels],
            FxNames = fx,
            SoundNames = sounds
        };

    private static DestructiblePreviewDefinition SmallHatchPreview(string wreckModel) => new(
        [
            new("Intact"),
            new("White smoke", FxName: "smoke/car_damage_whitesmoke", FxTag: "tag_hood_fx", RepeatFx: true),
            new("Black smoke", FxName: "smoke/car_damage_blacksmoke", FxTag: "tag_hood_fx", RepeatFx: true),
            new("Burning", FxName: "smoke/car_damage_blacksmoke_fire", FxTag: "tag_hood_fx",
                SoundName: "fire_vehicle_med", TransitionSoundName: "fire_vehicle_flareup_med", RepeatFx: true),
            new("Wreck", wreckModel, "explosions/small_vehicle_explosion", "tag_death_fx",
                TransitionSoundName: "car_explode", WorldUpFx: true)
        ],
        [
            new("Front windshield", "tag_glass_front", "tag_glass_front_d", "tag_glass_front_fx",
                "props/car_glass_large", "veh_glass_break_large"),
            new("Rear windshield", "tag_glass_back", "tag_glass_back_d", "tag_glass_back_fx",
                "props/car_glass_large", "veh_glass_break_large"),
            new("Left front window", "tag_glass_left_front", "tag_glass_left_front_d", "tag_glass_left_front_fx",
                "props/car_glass_med", "veh_glass_break_large"),
            new("Right front window", "tag_glass_right_front", "tag_glass_right_front_d", "tag_glass_right_front_fx",
                "props/car_glass_med", "veh_glass_break_large"),
            new("Left rear window", "tag_glass_left_back", "tag_glass_left_back_d", "tag_glass_left_back_fx",
                "props/car_glass_med", "veh_glass_break_large"),
            new("Right rear window", "tag_glass_right_back", "tag_glass_right_back_d", "tag_glass_right_back_fx",
                "props/car_glass_med", "veh_glass_break_large")
        ], HasTires: true);

    private static DestructiblePreset StockVehicle(string name, string description, string type, string model, string csvInclude,
        string? soundCsvInclude, string wreckModel, IReadOnlyList<string> otherModels,
        IReadOnlyList<string> fx, IReadOnlyList<string> sounds, string? family = null, string? variant = null,
        bool smallHatchPreview = false) =>
        new(name, description, model, type,
            "destructible_vehicle", csvInclude)
        {
            Category = "Vehicles",
            Family = family,
            Variant = variant,
            SoundCsvInclude = soundCsvInclude,
            Preview = smallHatchPreview ? SmallHatchPreview(wreckModel) :
                new DestructiblePreviewDefinition([new("Intact"), new("Wreck", wreckModel)]),
            ModelNames = [model, .. otherModels],
            FxNames = fx,
            SoundNames = sounds
        };

    internal static DestructiblePreset? Find(IReadOnlyDictionary<string, string> properties) =>
        All.FirstOrDefault(preset => properties.GetValueOrDefault("destructible_type") == preset.DestructibleType &&
            properties.GetValueOrDefault("model") == preset.ModelName);

    internal static void Validate(IReadOnlyDictionary<string, string> properties)
    {
        string? type = properties.GetValueOrDefault("destructible_type");
        if (type == "vehicle_bus_destructible")
            throw new InvalidDataException("The bus destructible type has no confirmed stock MP model pairing and cannot be built from this catalog.");
        if (Find(properties) is not { } preset)
        {
            if (All.Any(candidate => candidate.DestructibleType == type))
                throw new InvalidDataException($"Destructible type '{type}' is paired with an unsupported model. Place a catalog preset to restore its stock model.");
            return;
        }
        if (properties.GetValueOrDefault("classname") != "script_model" ||
            properties.GetValueOrDefault("model") != preset.ModelName ||
            properties.GetValueOrDefault("targetname") != preset.TargetName)
            throw new InvalidDataException($"{preset.Name} requires its original stock model, script_model class and discovery fields. Place the preset again to restore its setup.");
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
            placed = XModelEditing.Add(session, model, position, null, 0, preset.ModelScale);
            placed.Properties["classname"] = "script_model";
            placed.Properties["targetname"] = preset.TargetName;
            placed.Properties["destructible_type"] = preset.DestructibleType;
            if (preset.CsvInclude is not null) placed.Properties["csv_include"] = preset.CsvInclude;
            if (preset.SoundCsvInclude is not null) placed.Properties["sound_csv_include"] = preset.SoundCsvInclude;
            if (preset.PrecacheScript is not null) placed.Properties["precache_script"] = preset.PrecacheScript;
        });
        return placed ?? throw new InvalidOperationException("The destructible could not be placed.");
    }
}
