namespace MapConverter.Game.IW3.PS3.Rules

open System

module internal ScriptRules =
    /// Register source vehicle recipes before native map startup discovers
    /// the converted destructible_vehicle entities.
    let VehicleStartupCall(ownedPrefix: string) =
        "maps\\mp\\" + ownedPrefix.TrimEnd('/') + "_vehicles::main();\r\n\t"

    let CreateFxHelper(sourcePath: string, name: string, clientCall: bool) =
        let target path canonicalName = if clientCall then canonicalName else path + "::" + canonicalName
        match sourcePath.Replace('/', '\\').ToLowerInvariant(), name.ToLowerInvariant() with
        | @"maps\mp\_utility", "createoneshoteffect" -> target @"common_scripts\utility" "createOneshotEffect"
        | @"maps\mp\_utility", "createloopeffect" -> target @"common_scripts\utility" "createLoopEffect"
        | @"maps\mp\_createfx", "createloopsound" -> target @"common_scripts\_createfx" "createLoopSound"
        | _ -> null

    /// The native client CreateFX parser expects bare helper calls and these includes.
    let ClientCreateFxIncludes(text: string) =
        let headerEnd = if text.StartsWith("//", StringComparison.Ordinal) then text.IndexOf('\n') + 1 else 0
        let includes =
            [ "#include common_scripts\\utility;"; "#include common_scripts\\_createfx;" ]
            |> List.filter (fun directive -> not (text.Contains(directive, StringComparison.OrdinalIgnoreCase)))
        headerEnd, (if List.isEmpty includes then "" else String.concat "\r\n" includes + "\r\n")
