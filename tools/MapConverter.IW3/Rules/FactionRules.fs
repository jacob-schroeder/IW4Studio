namespace MapConverter.Game.IW3.Rules

open System
open System.Collections.Generic

type FactionProfile =
    | UsRussia = 0
    | Tf141Opfor = 1

module internal FactionRules =
    let ProfileName(profile: FactionProfile) =
        match profile with
        | FactionProfile.UsRussia -> "us/russia"
        | FactionProfile.Tf141Opfor -> "tf141/opfor"
        | _ -> invalidArg (nameof profile) "Unsupported faction profile."

    let Parse(value: string) : Nullable<FactionProfile> =
        if String.Equals(value, "us/russia", StringComparison.OrdinalIgnoreCase) then
            Nullable FactionProfile.UsRussia
        elif String.Equals(value, "tf141/opfor", StringComparison.OrdinalIgnoreCase) then
            Nullable FactionProfile.Tf141Opfor
        else Nullable()

    /// Native _teams.gsc uses these identities to select the mptype families.
    let TeamValue(profile: FactionProfile, key: string, original: string) =
        let allies, axis =
            match profile with
            | FactionProfile.UsRussia -> "us_army", "opforce_airborne"
            | FactionProfile.Tf141Opfor -> "socom_141_desert", "opforce_composite"
            | _ -> invalidArg (nameof profile) "Unsupported faction profile."
        match key with
        | "allies" -> allies
        | "axis" -> axis
        | _ -> original

    /// Afghan's native character/mptype closure, including both desert ghillies.
    let XModelNames(profile: FactionProfile, defaultNames: IReadOnlyList<string>) : IReadOnlyList<string> =
        match profile with
        | FactionProfile.UsRussia -> defaultNames
        | FactionProfile.Tf141Opfor ->
            [| "com_plasticcase_arab"; "com_plasticcase_taskforce141"
               "head_allies_sniper_ghillie_desert"; "head_allies_tf141_desert_sniper"
               "head_op_arab_sniper"; "head_op_sniper_ghillie_desert"
               "head_opforce_arab_a"; "head_opforce_arab_b"; "head_opforce_arab_c"
               "head_opforce_arab_d_hat"; "head_opforce_arab_e"
               "head_riot_op_arab"; "head_riot_tf141_desert"
               "head_tf141_desert_a"; "head_tf141_desert_b"; "head_tf141_desert_c"; "head_tf141_desert_d"
               "mp_body_ally_sniper_ghillie_desert"
               "mp_body_desert_tf141_assault_a"; "mp_body_desert_tf141_assault_b"
               "mp_body_desert_tf141_lmg"; "mp_body_desert_tf141_shotgun"; "mp_body_desert_tf141_smg"
               "mp_body_op_arab_sniper"; "mp_body_op_sniper_ghillie_desert"
               "mp_body_opforce_arab_assault_a"; "mp_body_opforce_arab_lmg_a"
               "mp_body_opforce_arab_shotgun_a"; "mp_body_opforce_arab_smg_a"
               "mp_body_riot_op_arab"; "mp_body_riot_tf141_desert"; "mp_body_tf141_desert_sniper"
               "viewhands_ghillie_desert"; "viewhands_militia"
               "viewhands_sniper_op_arab"; "viewhands_sniper_tf141_desert"; "viewhands_tf141" |]
        | _ -> invalidArg (nameof profile) "Unsupported faction profile."

    let MaterialNames(profile: FactionProfile, defaultNames: IReadOnlyList<string>) : IReadOnlyList<string> =
        match profile with
        | FactionProfile.UsRussia -> defaultNames
        | FactionProfile.Tf141Opfor ->
            [| "faction_128_taskforce141"; "faction_128_taskforce141_fade"
               "faction_128_arab"; "faction_128_arab_fade" |]
        | _ -> invalidArg (nameof profile) "Unsupported faction profile."
