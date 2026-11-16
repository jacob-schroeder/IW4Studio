namespace MapConverter.Game.IW3.PS3.Rules

open System

module internal ScriptRules =
    /// Register source vehicle recipes before native map startup discovers
    /// the converted destructible_vehicle entities.
    let VehicleStartupCall(ownedPrefix: string) =
        "maps\\mp\\" + ownedPrefix.TrimEnd('/') + "_vehicles::main();\r\n\t"

    /// Native IW4 CreateFX files share this syntax between the client parser
    /// and the development GSC path. Preserve authored placements and delays.
    let CreateFxHelpers(source: string) : string =
        let text =
            source
                .Replace(@"maps\mp\_utility::createOneshotEffect", "createOneshotEffect", StringComparison.Ordinal)
                .Replace(@"maps\mp\_createfx::createLoopSound", "createLoopSound", StringComparison.Ordinal)
        let headerEnd = if text.StartsWith("//", StringComparison.Ordinal) then text.IndexOf('\n') + 1 else 0
        let includes =
            [ "#include common_scripts\\utility;"; "#include common_scripts\\_createfx;" ]
            |> List.filter (fun directive -> not (text.Contains(directive, StringComparison.Ordinal)))
        if List.isEmpty includes then text
        else text.Insert(headerEnd, String.concat "\r\n" includes + "\r\n")
