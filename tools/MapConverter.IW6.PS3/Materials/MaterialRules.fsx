#load "../Collision/SurfaceTypes.fsx"

open System
open System.Text.Json
open System.Text.RegularExpressions

// The source reader supplies names and native slot identities for each zone.
// Packed pointers are opaque here. Their values and physical roots change between maps.
type Material = { root: int; name: string; technique: string; role: string; layers: string[]; surfaceBits: uint32 }
type Model = { name: string; handles: uint32[]; inlineRoots: int[] }
type Inventory = { materials: Material[]; models: Model[] }

let inventory = JsonSerializer.Deserialize<Inventory>(Console.In.ReadToEnd())
let models = inventory.models |> Array.map (fun m -> m.name, m) |> Map.ofArray
let materials = inventory.materials |> Array.map (fun m -> m.root, m) |> Map.ofArray
let oldPlanks = "w/has_wood_plank_old_01"
let stonePath = "w/skl_ground_dirt_stone_path_02_dec"

let terrainTechnique (m: Material) =
    if m.layers.Length < 2 then null
    else
        let shape = Regex.Replace(m.technique, "hb[0-3]|v[0-3]|sr[0-3]", "")
        let layers = shape.Split('_')
        if not (shape.StartsWith("l_sm_", StringComparison.Ordinal)) || layers.Length <> m.layers.Length + 2 then
            failwithf "Unrecognized terrain technique %s for %s" m.technique m.name
        match shape with
        | "l_sm_r0c0n0_m1c1p0" -> "l_sm_r0c0n0_m1c1"
        | "l_sm_r0c0n0sf0_b1c1n1s1_b2c2s2_b3c3s3" -> shape + "p0"
        | "l_sm_t0c0n0_b1c1n1s1_b2c2n2s2" -> "l_sm_t0c0n0sf0_b1c1n1s1_b2c2n2s2"
        | "l_sm_r0c0n0_b1c1s1_b2c2s2_b3c3s3" -> "l_sm_r0c0n0s0_b1c1_b2c2s2_b3c3s3p0"
        | _ -> shape

// Ghosts adds a q to its model stream family names. The surface exporter
// expands those quantized streams to the matching IW4 vertex declaration.
let nativeTechniques (m: Material) =
    let parts = m.technique.Split('_', 2)
    if parts.Length < 2 || m.layers.Length > 1 then [||]
    elif m.technique = "mqc_l_sm_flagext_wv0_r0c0n0s0" &&
         (m.name = "mqc/mtl_swamp_tent_01_matte" || m.name = "mqc/mtl_swamp_tent_01_shiny") then
        // Both tent fabrics have white vertex RGB. The native s0 family
        // supplies both the two-sided matte and back-culled shiny states.
        [| "m_l_sm_r0c0n0s0" |]
    else
        let prefix =
            match parts[0] with
            | "mq" | "mvq" -> "m"
            | "mqc" | "mvqc" -> "mc"
            | "mqg" -> "mg"
            | "w" | "wc" -> parts[0]
            | _ -> ""
        let suffix = parts[1].Replace("du_dm_", "").Replace("hb0c0", "c0").Replace("c0v0", "c0")
        let comparable =
            match suffix with
            // The equipped body uses Ghosts' rim-lit variants of these passes.
            | "l_sm_rim_r0c0n0sf0p0" -> [ "l_sm_r0c0n0sf0p0" ]
            | "l_sm_rim_r0c0n0sf0" -> [ "l_sm_r0c0n0sf0" ]
            | "unlit_blend_lin_ct" -> [ "unlit_blend_lin" ]
            | "l_r0c0_nocast" when prefix = "w" -> [ "l_sm_r0c0_nocast" ]
            | "l_flag_t0c0n0sf0" | "l_sm_flag_t0c0n0sf0_nocastp0" -> [ "l_sm_flag_t0c0n0sf0p0" ]
            | "l_sm_flagext_wv0wv1wv2_t0c0n0sf0" -> [ "l_sm_t0c0n0sf0" ]
            | "l_sm_flagext_wv0_r0c0n0sf0" -> [ "l_sm_r0c0n0sf0" ]
            | "l_sm_flagext_wv0_r0c0n0s0" -> [ "l_sm_r0c0n0s0"; "l_sm_r0c0n0sf0" ]
            | "l_sm_t0c0d0n0sf0" -> [ suffix; "l_sm_t0c0q0n0sf0" ]
            | "l_sm_t0c0d0n0s0p0" -> [ suffix; "l_sm_t0c0q0n0s0p0" ]
            | "l_sm_r0c0q0sf0" | "l_sm_r0c0q0n0" -> [ suffix; "l_sm_r0c0q0n0sf0" ]
            | "l_sm_b0c0q0sf0" -> [ suffix; "l_sm_b0c0q0n0s0" ]
            | _ -> [ suffix ]
        if prefix = "" then [||] else comparable |> List.map (sprintf "%s_%s" prefix) |> List.toArray

let decide (m: Material) =
    let stockImport =
        if m.name.StartsWith(",mq/", StringComparison.Ordinal) then "m/" + m.name.Substring(4)
        else null
    let unlit =
        match m.technique, m.name with
        | "mq_unlit_atest_lin_nocast", _ -> "m/mtl_tree_green_pine_cruciform_a_g"
        | "mq_unlit_atest_lin", "mq/mtl_lamp_iron_lit" -> "m/mtl_utility_clamp_light_on"
        | "mqc_unlit_blend_lin_ua", "mqc/mtl_ng_old_tv_static" -> "m/mtl_uav_scanlines"
        | "mq_unlit_replace_lin", "mq/mtl_furniture_lamp_table1_db" -> "m/mtl_furniture_lamp_table1_db"
        | "mq_unlit_replace_lin", "mq/mtl_furniture_chandelier1" -> "m/mtl_furniture_chandelier1"
        | "mq_unlit_replace_lin", _ -> "m/mtl_utility_lightbulb_bare_on"
        | _ -> null
    let effect, effectVariant =
        match m.technique, m.role, m.name with
        | "distortion_scale", "effect", "gfx_distortion_puddle" -> "gfx_distortion_puddle", null
        | "distortion_scale", "effect", "gfx_distortion_heat" -> "gfx_distortion_heat", null
        | _, "effect", "gfx_test01" -> "gfx_test01", null
        | "effect_zfeather_blend", "effect", "gfx_blood_drops_atlas" -> "gfx_blood_drops_atlas", null
        | "effect_zfeather_blend", "effect", "gfx_blood_exit_wound_atlas" -> "gfx_blood_exit_wound_atlas", null
        | "effect_zfeather_blend", "effect", "vfx_blood_ball_cg"
        | "effect_zfeather_blend_tab", "effect", "vfx_blood_body_atlas_cg"
        | "effect_zfeather_blend_lin", "effect", "vfx_blood_spray_bits" -> "gfx_blood_drops_atlas", null
        | "effect_zfeather_blend", "effect", "vfx_smk_white_anim_cg" -> "gfx_smk_white_atlas", null
        | "effect_zfeather_add_lin_nofog", ("effect" | "effect_add"), _ ->
            "gfx_fire_ball_atlas_z15_nofog", "effect_zfeather_add_lin_nofog"
        | "effect_add_lin_nofog", ("effect" | "effect_add"), _ ->
            "gfx_spark_atlas", "effect_add_lin_nofog"
        | "effect_add", ("effect" | "effect_add"), _ -> "gfx_fire_ball_atlas", null
        | "effect_add_lin", ("effect" | "effect_add"), _ -> "gfx_water_boat_wake_trail", null
        | "effect_blend", "effect", _ -> "gfx_leaves", null
        | "effect_blend_nofog", "effect", _ -> "gfx_mud_splash02", null
        | "effect_falloff_blend", "effect", _ -> "gfx_smoke_trail02_godray", null
        | "effect_zfeather_blend_nofog", "effect", _ -> "gfx_whitesmoke", null
        | "effect_zfeather_blend_lin_nofog", "effect", _ -> "gfx_whitesmoke", "effect_zfeather_blend_lin_nofog"
        | "effect_zfeather_outdoor_blend", "effect", _ -> "gfx_wind_rain_atlas", null
        | "effect_zfeather_add_nofog", ("effect" | "effect_add"), _ -> "gfx_flare_halogen_glw_z40", null
        | "effect_zfeather_add_lin", ("effect" | "effect_add"), _ -> "gfx_fire_roar_pm_atlas", null
        | "effect_zfeather_add_eyeoffset", "effect", _ -> "gfx_flare_halogen_glw_z20_eye10", null
        | "effect_zfeather_falloff_add_nofog", "effect", _ -> "gfx_spotlight_lensflare_rainbow_z5_godray", null
        | "particle_cloud_replace", "effect", _ -> "cloud_dust_mote", null
        | "particle_cloud_spark_replace", "effect", _ -> "spark_rndhit_64", "particle_cloud_spark_replace"
        | "particle_cloud_spark_add", "effect", "spark_rndhit_64_sort" -> "spark_rndhit_64_sort", null
        | "particle_cloud_spark_add", "effect", _ -> "spark_rndhit_64", null
        | _ -> null, null
    // Ghosts shares the cobweb draw states across all five world webs, and the
    // vista light shares its states with tree_godray. Their shader inputs differ.
    let translucentTemplate, stateTemplate =
        match m.technique, m.name with
        | "w_l_sm_b0c0sf0", "w/ch_cobweb01" -> "w/ch_cobweb01", "w/ch_cobweb01"
        | "wc_l_sm_b0c0sf0", "wc/ch_cobweb01" -> "wc/decal_oil_stain01", "w/ch_cobweb01"
        | "w_l_sm_b0c0", "w/swamp_web_01" -> "w/me_graffiti02", "w/ch_cobweb01"
        | "wc_l_sm_b0c0", "wc/swamp_web_01"
        | "wc_l_sm_flagext_wv0wv1_b0c0", "wc/swamp_web_01_flag" -> "wc/me_graffiti02", "w/ch_cobweb01"
        | "mq_unlit_add_lin", "mq/cnd_decals_vista_01" -> "m/ap_light_embedded_round_glow", "wc/tree_godray"
        | _ -> null, null
    let litTemplate =
        match m.technique, m.name with
        | "mq_l_sm_rim_t0c0sf0", "mq/mtl_mp_mmyers_alpha_a" -> "m/mtl_sniper_ghillie_desert"
        // These opaque passes use color and normal maps with no specular sampler.
        | "mq_l_sm_r0c0n0", _ when m.role = "model" -> "m/ch_factory_rustybeam2"
        | "w_l_sm_r0c0n0", _ when m.role = "world" -> "w/ch_factory_rustybeam2"
        | "wc_l_sm_r0c0n0", _ when m.role = "world" -> "wc/ch_factory_rustybeam2"
        | "mq_l_sm_r0c0n0sf0", "mq/mtl_office_books_set_iw6" -> "m/mtl_office_books"
        | "mq_l_sm_r0c0n0sf0", "mq/mtl_boat_fishing_1"
        | "mq_l_sm_r0c0n0sf0", "mq/mtl_boat_fishing_1_plastic" -> "m/mtl_rowboat_and_paddles"
        | "mq_l_sm_b0c0sf0", "mq/mtl_spiderweb" -> "m/mtl_ch_gas_pump_glass"
        | "mq_l_sm_b0c0n0s0p0", "mq/mtl_shiny_lense_eye" -> "m/mtl_shiny_lense"
        | "w_l_sm_r0c0n0sf0", name when name = oldPlanks -> "w/ch_woodplank01"
        | _, _ when m.role = "world_layer_base" && m.layers[0] = oldPlanks -> "w/ch_woodplank01"
        // These single-sided passes share the corrugated sheet's source state table.
        | "mq_l_sm_t0c0n0sf0", _ when m.role = "model" -> "m/mtl_corrugated_metal_holes"
        | "mqg_l_sm_t0c0n0sf0", "mqg/mtl_lamp_iron" -> "m/mtl_corrugated_metal_holes"
        | "mq_l_sm_flagext_wv0_t0c0n0sf0", name when name.EndsWith("_alpha", StringComparison.Ordinal) ->
            "m/mtl_hedges_noshadow"
        // Ghosts outputs vertex red as grayscale (255 on both grass meshes)
        // and opaque vertex alpha. Green and alpha drive reactive motion.
        | "mqc_ambient_reactivemotion_t0c0_nocast_flagext_wv0", "mqc/mtl_swamp_foliage_grass_tall_01" ->
            "m/mtl_foliage_pacific_grass_clump01"
        // These extended-wind meshes use vertex alpha as a deformation weight.
        // The IW4 no-color passes keep that mask separate from texture opacity.
        | "mqc_l_sm_flagext_wv0_t0c0", "mqc/mtl_eerie_tree_moss_01"
        | "mq_l_sm_flagext_wv0_t0c0", "mq/mtl_eerie_tree_moss_01" -> "m/mtl_red_pine_med_cruciform"
        | "mq_l_sm_t0c0sf0", "mq/mtl_com_debris_paper_iw6_01"
        | "mq_l_sm_t0c0sf0", "mq/mtl_com_debris_paper_iw6_02" -> "m/mtl_cs_iron_wire_thick"
        | "mq_l_sm_r0c0n0sf0p0", "mq/mtl_pb_rock_brown1" -> "m/mtl_tf_141_desert_shotgun_metal"
        | _ -> null
    let fragmentFeature =
        if m.technique.Contains("_du_dm_") then "dust"
        elif m.technique = "mqc_unlit_blend_lin_ct" then "tint"
        elif m.layers.Length < 2 && m.technique.Contains("c0v0") then "reveal"
        else null
    let windSource =
        match m.technique with
        | "mqc_l_sm_flagext_wv0wv1wv2_t0c0n0sf0" -> "ivy"
        | "mqc_ambient_reactivemotion_t0c0_nocast_flagext_wv0" -> "grass"
        | "mqc_l_sm_flagext_wv0_t0c0" -> "moss"
        | "mq_l_sm_flagext_wv0_t0c0" -> "moss_nc"
        | "mqc_l_sm_flagext_wv0_r0c0n0sf0"
        | "mqc_l_sm_flagext_wv0_r0c0n0s0" -> "cloth"
        | "mq_l_sm_flagext_wv0_t0c0n0sf0" -> "moss_alpha"
        | _ -> null
    let isStonePath =
        match m.technique, m.name with
        | "w_l_sm_b0hb0c0v0n0sf0", name when name = stonePath -> true
        | "wc_l_sm_b0hb0c0v0n0sf0", "wc/skl_ground_dirt_stone_path_02_dec" -> true
        | _ -> false
    let revealTemplate =
        if isStonePath || (m.technique = "wc_l_sm_b0c0v0n0sf0" && m.name = "wc/ls_trash_pile_01_rev_dsp") then
            "wc/com_concrete_core_dec"
        else null
    // The path follows wet mud (DecalStatic) and precedes the oak leaves.
    let sortKey =
        if isStonePath then Nullable(10)
        elif m.name = "wc/swamp_ground_oakleaves01_dec" then Nullable(11)
        else Nullable<int>()
    let imageOwner =
        match m.name with
        | "mq/mtl_eerie_tree_moss_01" -> "mqc/mtl_eerie_tree_moss_01"
        | "mq/swamp_wood_planks_01" | "w/swamp_wood_planks_01" -> "mqc/swamp_wood_planks_01"
        | "mq/mtl_swamp_basement_door_metal" -> "mqc/mtl_swamp_basement_door_metal"
        | name when name = oldPlanks -> "mq/has_wood_plank_old_01"
        | "mq/mtl_boat_fishing_1" -> "mq/mtl_boat_fishing_1_plastic"
        | "mqg/mtl_tree_birch_trunk" -> "mq/mtl_tree_birch_trunk"
        | "mq/mtl_lamp_iron_lit" | "mqg/mtl_lamp_iron" -> "mq/mtl_lamp_iron"
        | "mq/mtl_ng_old_tv_glass" -> "mq/mtl_ng_old_tv"
        | "w/ct_glass02" -> "m/ct_glass02"
        | "w/dt_wooddamaged_02_ns" -> "mq/dt_wooddamaged_02_ns"
        | "wc/window_curtain01_tan_flag" -> "wc/window_curtain01_tan"
        | "mq/mtl_mirror_metal" -> "mq/mtl_mirror"
        | "mqc/mtl_birdpoop_decal" -> "mq/mtl_birdpoop_decal"
        | "mqg/mtl_table_patio" -> "mq/mtl_table_patio"
        // Whiteout's identical crate mesh shares the body images with the ice.
        | "mq/mtl_swamp_fish_crate_ice" -> "mq/mtl_swamp_fish_crate_01"
        | _ -> null
    {| root = m.root; stockImport = stockImport; nativeTechniques = nativeTechniques m; fragmentFeature = fragmentFeature; windSource = windSource; unlit = unlit; effect = effect; effectVariant = effectVariant; litTemplate = litTemplate
       translucentTemplate = translucentTemplate; stateTemplate = stateTemplate
       revealTemplate = revealTemplate; sortKey = sortKey; terrainTechnique = terrainTechnique m
       imageOwner = imageOwner; surfaceBits = SurfaceTypes.materialBits m.surfaceBits |}

// A shared slot must resolve to the verified named material in the donor model.
// Once resolved, the same source handle identifies the material for every consumer.
let binding ownerName ownerSlot materialName userName userSlot =
    match models.TryFind ownerName, models.TryFind userName with
    | Some owner, Some user when ownerSlot < owner.inlineRoots.Length && userSlot < user.handles.Length ->
        let root = owner.inlineRoots[ownerSlot]
        let handle = user.handles[userSlot]
        match materials.TryFind root with
        | Some mat when mat.name = materialName && handle <> 0u && handle < 0xfffffffeu ->
            Some {| handle = handle; root = root |}
        | _ -> None
    | _ -> None

let bindings =
    [| for model, slot in ["swamp_beam_woodandrope_old_01", 0; "swamp_beam_woodandrope_old_01", 2
                           "ac_prs_imp_ch_wooden_fence_post_02", 0] do
           yield! binding "moab_stair_wood_old_01" 0 "mq/ch_woodwall01" model slot |> Option.toArray
       yield! binding "ls_wood_burned_ehq_02" 0 "mq/ls_wood_burn_2" "ls_wood_burned_ehq_02" 2 |> Option.toArray
       yield! binding "ls_wood_burned_ehq_02" 1 "mqg/ls_wood_snapped_burnt_01_dec" "ls_wood_burned_01" 1 |> Option.toArray
       for ownerSlot, name, model, slot in [
           0, "mqc/mtl_eerie_tree_01", "swamp_eerie_tree_01", 3
           1, "mq/mtl_tree_mushroom_01", "swamp_eerie_tree_01", 4
           2, "mqc/mtl_eerie_tree_moss_01", "swamp_eerie_tree_02", 1
           2, "mqc/mtl_eerie_tree_moss_01", "swamp_eerie_tree_03", 2 ] do
           yield! binding "swamp_eerie_tree_01" ownerSlot name model slot |> Option.toArray
       // The paired models use the same handle in their other variants or LODs.
       for owner, ownerSlot, name, model, slot in [
           "viewmodel_axe_iw6", 0, "mq/mtl_weapon_axe_iw6", "weapon_axe_iw6", 0
           "ag_hanging_weeds_01", 0, "mq/mtl_ag_root_01", "ag_hanging_weeds_03", 0
           "ag_hanging_weeds_01", 1, "mq/mtl_foliage_afr_grass_tall_01", "ag_hanging_weeds_03", 1
           "ag_hanging_weeds_01", 2, "mq/mtl_ag_root_02", "ag_hanging_weeds_03", 2
           "ny_harbor_sub_int_barracks_picture_01", 0, "mq/mtl_ny_harbor_sub_int_pictures_01", "ny_harbor_sub_int_barracks_picture_02", 0
           "patio_chair_folding_iw6", 1, "mq/mtl_birdpoop_drip_decal", "patio_chair_folding_iw6", 4
           "patio_chair_folding_iw6", 2, "mq/mtl_chair_patio_iw6", "patio_chair_folding_iw6", 5
           "patio_chair_folding_iw6", 3, "mq/mtl_table_patio", "patio_chair_folding_iw6", 6
           "com_boat_fishing_buoy1", 0, "mq/mtl_boat_fishing_1_plastic", "com_boat_fishing_buoy2", 0
           "com_boat_fishing_buoy1", 1, "mq/mtl_boat_fishing_1", "com_boat_fishing_buoy2", 1
           "picture_frame_painting_tall_5", 0, "mq/mtl_pictureframes_painting_set_2", "picture_frame_painting_tall_6", 0
           "afr_crate32x48", 3, "mq/mtl_ch_crates", "oilrocks_crate48x64", 2
           "goat_carcass_02", 0, "mq/mtl_goat_carcass", "goat_carcass_03", 0
           "swamp_cave_planks", 0, "mq/has_wood_plank_old_01", "has_lath_corner_01", 0
           "mp_swamp_sign_live_crawfish", 0, "mq/mtl_signs_mp_swamp", "mp_swamp_sign_boat_rental", 0
           "swamp_eerie_tree_05", 2, "mqc/swamp_ground_dirt_leaves_no_dis", "swamp_eerie_tree_05", 6
           "swamp_eerie_tree_01", 5, "mq/mtl_eerie_tree_moss_01", "swamp_eerie_tree_05", 7
           "ng_old_tv_static", 0, "mq/mtl_ng_old_tv", "ng_old_tv", 0
           "ng_old_tv_static", 1, "mq/mtl_ng_old_tv_glass", "ng_old_tv", 1
           "mp_swamp_sign_bait_shop", 0, "mq/mtl_sign_bait_shop", "mp_swamp_sign_fresh_oysters", 0
           "pb_fishnet01", 0, "mq/mtl_pb_fishnet01", "pb_fishnet03", 0
           "lantern_iron_arm", 0, "mq/mtl_lamp_iron", "lantern_iron_on", 0
           "lantern_iron_on", 1, "mq/mtl_lamp_iron_lit", "lantern_iron_on", 3
           "lantern_iron_on", 2, "mqg/mtl_lamp_iron", "lantern_iron_on", 4 ] do
           yield! binding owner ownerSlot name model slot |> Option.toArray |]
    |> Array.distinct

JsonSerializer.Serialize({| materials = Array.map decide inventory.materials; bindings = bindings |})
|> Console.WriteLine
