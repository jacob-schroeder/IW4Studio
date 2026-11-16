namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open IW4.Game.Assets.Sound

module internal SoundRules =
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
    let TargetStreamFlags(donorFlags: int, sourceFlags: int, ambient: bool) =
        let source = SndAliasBits(sourceFlags)
        let playback = SndAliasBits(donorFlags).WithPlaybackType(2uy)
        let looping = playback.WithLooping(source.IsLooping)
        let master = looping.WithMaster(source.IsMaster)
        let slave = master.WithConditionalVolumeScale(source.UsesConditionalVolumeScale)
        slave.WithEntityChannelIndex(if ambient then 25uy else 19uy).RawValue
