namespace Iw4Radiant.Views;

public sealed record FxSoundAsset(string Name, bool IsSound)
{
    public string DisplayName
    {
        get
        {
            string leaf = Name.Replace('\\', '/').Split('/').Last();
            return string.Join(' ', leaf.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        }
    }

    public string Category
    {
        get
        {
            string path = Name.Replace('\\', '/');
            int separator = path.LastIndexOf('/');
            return separator < 0 ? (IsSound ? "Sound" : "Effects") :
                path[..separator].Replace('_', ' ').Replace("/", " › ", StringComparison.Ordinal);
        }
    }

    // These exact aliases are used by createLoopSound in the local PS3 MP map fastfiles.
    // Other owned aliases remain available to inspect, but their loop behavior is unknown.
    private static readonly HashSet<string> LoopUsedAliases = new(StringComparer.Ordinal)
    {
        "ambient_airport_tarmac_l",
        "ambient_airport_tarmac_r",
        "emt_ac_air_Vent",
        "emt_ac_air_vent",
        "emt_ac_duct_rattle",
        "emt_ac_metal_rattle",
        "emt_airport_baggage_belt",
        "emt_airport_flyover_interior",
        "emt_airport_fuel_filling",
        "emt_airport_fuel_pump",
        "emt_airport_tarmac_dull",
        "emt_airport_tarmac_interior",
        "emt_airport_tarmac_sharp",
        "emt_airport_traffic",
        "emt_bird_distant_caw_loop",
        "emt_bird_distant_chirp_loop",
        "emt_bird_distant_peck_loop",
        "emt_cave_stress",
        "emt_cicada_loop",
        "emt_cloth_flap_tent",
        "emt_computer_beep_bloop",
        "emt_computer_fan",
        "emt_computer_fan_beeps",
        "emt_cricket_loop",
        "emt_cricket_loop1",
        "emt_dog_distant_loop",
        "emt_elec_transformer",
        "emt_elec_transformer_box",
        "emt_elec_transformer_close",
        "emt_fan_house",
        "emt_fan_industrial_med",
        "emt_fan_large_close",
        "emt_ferris_rattle_squeak",
        "emt_ferris_wheel_distant",
        "emt_fly_loop",
        "emt_fly_loop_res",
        "emt_frog_loop1",
        "emt_frog_loop2",
        "emt_frog_loop3",
        "emt_highelevation_wind",
        "emt_highrise_outside_loop",
        "emt_hum_splash2",
        "emt_industrial_air_vent",
        "emt_industrial_hum_large",
        "emt_industrial_hum_small",
        "emt_jet_engine_close",
        "emt_jet_engine_close_interior",
        "emt_jet_engine_dist",
        "emt_large_flag_flap",
        "emt_light_flourescent_hum",
        "emt_light_fluorescent_hum",
        "emt_light_fluorescent_hum2",
        "emt_light_fluorescent_hum3",
        "emt_lightrain_building",
        "emt_lightrain_foliage",
        "emt_lightrain_metal",
        "emt_lightrain_wood",
        "emt_lightrain_wood_roof",
        "emt_mediumrain_metal1",
        "emt_mediumrain_metal2",
        "emt_mediumrain_metal2_small",
        "emt_metal_bumpercars",
        "emt_metal_rattle_dull",
        "emt_metal_rattle_dull_by",
        "emt_metal_rattle_flagpole",
        "emt_metal_rattle_pole",
        "emt_metal_rattle_ring",
        "emt_metal_rattle_squeak",
        "emt_metal_rattle_squeak_by",
        "emt_metal_rocketship_groan",
        "emt_metal_sheet_knocking",
        "emt_missile_silo_close",
        "emt_mtl_chainlink_rattle",
        "emt_mtl_corrugate_rattle",
        "emt_mtl_creak_hvy_by",
        "emt_mtl_crumple_settle",
        "emt_mtl_drum_pings",
        "emt_mtl_rattle_tap_by",
        "emt_mtl_tower_creaking",
        "emt_mx_airport_bookstore",
        "emt_mx_airport_dining",
        "emt_ocean_water_slosh",
        "emt_ocean_water_slosh_small",
        "emt_paper_flutter_rustle",
        "emt_pipe_air_hiss",
        "emt_pipe_clanking",
        "emt_pipe_gas",
        "emt_pipe_metal_hum",
        "emt_pipe_stress",
        "emt_pipe_water",
        "emt_pumpjack_motor",
        "emt_pumpjack_rattle",
        "emt_pumpjack_squeak",
        "emt_pumpjack_whine",
        "emt_rain_foliage",
        "emt_rain_metal",
        "emt_rain_metal_int",
        "emt_rain_metal_mvmt",
        "emt_rain_wood",
        "emt_refrigerator_hum",
        "emt_rock_rubble",
        "emt_rock_small_debris",
        "emt_rock_small_debris_verby",
        "emt_ship_chain_sway",
        "emt_siren_police",
        "emt_traffic_bigcity_distant",
        "emt_tree_leaf_rustle",
        "emt_tree_palm_rustle",
        "emt_twig_snap_snow_fall",
        "emt_vendingmachine_hum",
        "emt_water_drain",
        "emt_water_drain_reverb",
        "emt_water_drain_splash",
        "emt_water_fall_bubbly",
        "emt_water_fall_gurgle",
        "emt_water_fall_medium_dist",
        "emt_water_lake",
        "emt_water_pipe_splashy",
        "emt_water_river_distant",
        "emt_water_stream_fast",
        "emt_water_stream_slow",
        "emt_wind_corner",
        "emt_wind_desert_heavy",
        "emt_wind_desert_light",
        "emt_wind_fence_whistle",
        "emt_wind_flat_surface",
        "emt_wind_interior_wood",
        "emt_wind_mountain_heavy",
        "emt_wind_mountain_light",
        "emt_wind_obstacle_corner",
        "emt_wind_tarp_flap",
        "emt_wind_wire_whip",
        "emt_wood_creak_heavy",
        "emt_wood_creak_heavy_res",
        "emt_wood_creak_light",
        "emt_wood_creak_light_res",
        "emt_wood_rock_heavy_res",
        "fire_wood_small",
    };

    public bool IsKnownLoop => IsSound && LoopUsedAliases.Contains(Name);
}

public static class FxSoundAssetCatalog
{
    public static async Task<IReadOnlyList<FxSoundAsset>> ReadAsync(
        string sourceDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string root = Path.GetFullPath(sourceDirectory);
        return await Task.Run<IReadOnlyList<FxSoundAsset>>(() =>
        {
            var assets = new List<FxSoundAsset>();
            AddAssets("fx", isSound: false);
            AddAssets("soundaliases", isSound: true);
            if (assets.Count == 0)
                throw new InvalidDataException("This folder has no fx or soundaliases JSON source files. Choose the raw asset folder.");
            return assets.OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            void AddAssets(string folderName, bool isSound)
            {
                string folder = Path.Combine(root, folderName);
                if (!Directory.Exists(folder)) return;
                foreach (string file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(folder, file);
                    string name = relative[..^Path.GetExtension(relative).Length].Replace(Path.DirectorySeparatorChar, '/');
                    assets.Add(new FxSoundAsset(name, isSound));
                }
            }
        }, cancellationToken);
    }
}
