namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open System.Globalization
open IW4.Game.Assets.Material
open IW4.Game.Assets.Sound

type internal ScriptSoundAliasArgument =
    | None = 0
    | Required = 1
    | Optional = 2

module internal SoundRules =
    /// All ten common_mp shock profiles mix these IW4 channels at the levels
    /// of the paired IW3 channels. default.shock sets all four to 0.1.
    let MissingShellShockVolumes (assigned: IReadOnlyDictionary<string, string>) =
        [| for channel, source in [ "ambdist1", "element"; "ambdist2", "element"
                                    "explosiveimpact", "bulletimpact"; "local3", "local2" ] do
               let field = "bg_shock_volume_" + channel
               if not (assigned.ContainsKey field) then
                   let volume =
                       match assigned.TryGetValue("bg_shock_volume_" + source) with
                       | true, text ->
                           match Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
                           | true, value when Single.IsFinite value -> text
                           | _ -> "0.1"
                       | _ -> "0.1"
                   yield field + " \"" + volume + "\"" |]

    /// Native runtime names whose definitions belong to the map fastfile.
    let NativeRuntimeAliases() =
        [| yield "ui_pulse_text_type"
           yield "ui_pulse_text_delete"
           for surface in Enum.GetValues<MaterialSurfaceType>() do
               if surface <> MaterialSurfaceType.Count then
                   yield "rocket_explode_" + surface.ToString().ToLowerInvariant()
           yield "rocket_explode_brick_layer"
           yield "rocket_explode_concrete_layer"
           yield "rocket_explode_sand_layer"
           yield "bullet_small_snow"
           yield "bullet_large_snow"
           yield "bulletspray_small_snow"
           for surface in [ "dirt"; "metal"; "sand"; "snow" ] do
               yield "step_run_plr_" + surface
               yield "step_walk_plr_" + surface
           yield "step_sprint_plr_metal"
           yield "step_sprint_plr_snow"
           for surface in [ "metal"; "sand"; "snow" ] do
               yield "Land_" + surface
               yield "Land_plr_" + surface
           yield "phy_cargocontainer_default"
           yield "physics_tire_default" |]

    let ScriptAliasArgument(functionName: string, ambientOnly: bool) =
        match functionName.ToLowerInvariant() with
        | "ambientplay" -> ScriptSoundAliasArgument.Required
        | _ when ambientOnly -> ScriptSoundAliasArgument.None
        | "playlocalsound"
        | "playsound"
        | "playsoundasmaster"
        | "playloopsound" -> ScriptSoundAliasArgument.Required
        | "stoploopsound" -> ScriptSoundAliasArgument.Optional
        | _ -> ScriptSoundAliasArgument.None

    /// These native turret aliases belong to IW4 common_mp. Reuse them without
    /// importing their shared LoadedSound/SndCurve graph into the converted map.
    /// Other aliases, including the ten minigun_gatling aliases, remain map-owned.
    let IsResidentNativeTurretAlias(name: string) =
        match name.ToLowerInvariant() with
        | "smokegrenade_explode_default"
        | "weap_ammo_pickup"
        | "weap_dryfire_smg_npc"
        | "weap_pickup"
        | "weap_putaway"
        | "weap_raise"
        | "weap_raise_layer" -> true
        | _ -> false

    let IsAmbientAlias(name: string, selected: IReadOnlySet<string>) =
        selected.Contains(name) || name.StartsWith("ambient_", StringComparison.Ordinal)

    let DonorName(name: string, ambient: bool) =
        if not ambient then
            "emt_fly_loop"
        else if name.EndsWith("_sur", StringComparison.Ordinal) then
            "ambient_airport_tarmac_r"
        else
            "ambient_airport_tarmac_l"

    let OwnedName(prefix: string, sourceName: string) = prefix + sourceName

    /// The stock _global_fx script selects prefab emitters by script_struct
    /// targetname. Its literal alias arguments must remain available by their
    /// original names because that shared script is not rewritten per map.
    let SharedPrefabAliases
        (entities: IReadOnlyList<IReadOnlyDictionary<string, string>>,
         declarations: IReadOnlyList<struct (string * string)>) =
        let targets = HashSet<string>(StringComparer.Ordinal)
        for entity in entities do
            match entity.TryGetValue "classname", entity.TryGetValue "targetname" with
            | (true, "script_struct"), (true, target) -> targets.Add target |> ignore
            | _ -> ()
        declarations
        |> Seq.choose (fun struct (target, alias) -> if targets.Contains target then Some alias else None)
        |> Seq.distinct
        |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right))
        |> Seq.toArray

    /// Emit only identities with a current consumer. A shared-script alias
    /// and a map-owned alias can refer to the same packaged sample ranges.
    let ExportNames(prefix: string, sourceName: string, mapOwned: bool, shared: bool) =
        [| if mapOwned then yield OwnedName(prefix, sourceName)
           if shared then yield sourceName |]

    let ReferenceName(prefix: string, sourceName: string) =
        if String.IsNullOrWhiteSpace(sourceName) then null
        else OwnedName(prefix, sourceName)

    /// Source MP3 bytes use IW4 stream playback. The Bloc hardware correction
    /// established local=25 (2D) for AmbientPlay; effects1=19 remains 3D for
    /// placed emitters. IW3 SoundAliasFlags in iw3x-port Structs.hpp puts
    /// looping/master/slave at bits 0/1/2; Isnd_alias_list_t.cpp copies
    /// those to IW4. PS3 SndAliasBits confirms loop, master request, and
    /// conditional slave volume at the same bits. Other bits keep the native
    /// donor's target semantics.
    let TargetStreamFlags(donorFlags: SndAliasBits, sourceFlags: int, ambient: bool) =
        let source = SndAliasBits(sourceFlags)
        let playback = donorFlags.WithPlaybackType(SndAliasPlaybackType.Stream)
        let looping = playback.WithLooping(source.IsLooping)
        let master = looping.WithMaster(source.IsMaster)
        let slave = master.WithConditionalVolumeScale(source.UsesConditionalVolumeScale)
        slave.WithEntityChannelIndex(if ambient then 25uy else 19uy)
