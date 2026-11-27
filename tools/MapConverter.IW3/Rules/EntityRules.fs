namespace MapConverter.Game.IW3.Rules

open System
open System.Collections.Generic
open System.IO

module internal EntityRules =
    [<Literal>]
    let NativeTurretModel = "weapon_minigun"

    [<Literal>]
    let NativeTurretWeapon = "turret_minigun_mp"

    let private equal left right = String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

    /// Native IW4 Demolition uses the shared A/B bombzones, but requires its
    /// own attacker/defender start and respawn classes. Reuse authored S&D
    /// transforms for missing classes, keeping the original S&D entities.
    let DemolitionSpawnCopies(entities: IReadOnlyList<IReadOnlyDictionary<string, string>>) : IReadOnlyList<struct (int * string)> =
        let property key (entity: IReadOnlyDictionary<string, string>) =
            match entity.TryGetValue key with
            | true, value -> value
            | _ -> ""
        let mappings =
            [| "mp_sd_spawn_attacker", "mp_dd_spawn_attacker"
               "mp_sd_spawn_attacker", "mp_dd_spawn_attacker_start"
               "mp_sd_spawn_defender", "mp_dd_spawn_defender"
               "mp_sd_spawn_defender", "mp_dd_spawn_defender_start" |]
            |> Array.filter (fun (_, target) ->
                not (entities |> Seq.exists (fun entity -> equal (property "classname" entity) target)))
        let zones = entities |> Seq.filter (fun entity -> equal (property "targetname" entity) "bombzone") |> Seq.toArray
        if mappings.Length = 0 || zones.Length = 0 then Array.empty :> IReadOnlyList<struct (int * string)>
        else
            if zones.Length <> 2 ||
               not ([| "_a"; "_b" |] |> Array.forall (fun label ->
                   zones |> Array.exists (fun entity -> equal (property "script_label" entity) label))) then
                raise (InvalidDataException("Demolition conversion requires exactly two authored bombzones labeled _a and _b."))
            mappings
            |> Array.collect (fun (source, target) ->
                let indices =
                    entities |> Seq.mapi (fun index entity -> index, entity)
                    |> Seq.choose (fun (index, entity) ->
                        if equal (property "classname" entity) source then Some(struct (index, target)) else None)
                    |> Seq.toArray
                if indices.Length = 0 then
                    raise (InvalidDataException($"Demolition conversion requires authored '{source}' positions for '{target}'."))
                indices)
            :> IReadOnlyList<struct (int * string)>

    /// MW2 has no COD4 oldschool pickup mode. Remove the authored pickup rows
    /// and only the exclusive use triggers linked by their perk models.
    let RetainGameplayEntities(entities: IReadOnlyList<IReadOnlyDictionary<string, string>>) : IReadOnlyList<bool> =
        let property key (entity: IReadOnlyDictionary<string, string>) =
            match entity.TryGetValue key with
            | true, value -> value
            | _ -> ""
        let keep = entities |> Seq.map (fun entity -> not (equal (property "targetname" entity) "oldschool_pickup")) |> Seq.toArray
        let triggerTargets =
            entities
            |> Seq.mapi (fun index entity -> index, entity)
            |> Seq.choose (fun (index, entity) ->
                let target = property "target" entity
                if not keep[index] && equal (property "classname" entity) "script_model" && not (String.IsNullOrWhiteSpace target)
                then Some target else None)
            |> fun targets -> HashSet<string>(targets, StringComparer.OrdinalIgnoreCase)
        for target in triggerTargets do
            let linked =
                entities
                |> Seq.mapi (fun index entity -> index, entity)
                |> Seq.filter (fun (_, entity) -> equal (property "targetname" entity) target)
                |> Seq.toArray
            if linked.Length <> 1 || not (equal (property "classname" (snd linked[0])) "trigger_use") ||
               (entities |> Seq.mapi (fun index entity -> index, entity)
                |> Seq.exists (fun (index, entity) -> keep[index] && equal (property "target" entity) target)) then
                raise (InvalidDataException($"Oldschool perk target '{target}' is missing, ambiguous or shared with retained gameplay."))
            keep[fst linked[0]] <- false
        keep

    // Retain placement, firing arcs and all unrelated entity properties. The
    // native MW2 profile supplies the model, weapon and their asset closure.
    let TurretPropertyValue(entity: IReadOnlyDictionary<string, string>, key: string, value: string) =
        match entity.TryGetValue "classname", entity.TryGetValue "weaponinfo" with
        | (true, classname), (true, weapon) when equal classname "misc_turret" &&
                (equal weapon "saw_bipod_stand_mp" || equal weapon "saw_bipod_crouch_mp") ->
            match entity.TryGetValue "model" with
            | true, model when equal model "weapon_saw_MG_setup" ->
                if equal key "model" then NativeTurretModel
                elif equal key "weaponinfo" then NativeTurretWeapon
                else value
            | _ -> raise (InvalidDataException("COD4 mounted SAW turret has an unexpected model."))
        | _ -> value
