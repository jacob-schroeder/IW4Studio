namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions

module internal DestructibleVehicleRules =
    let private invalid message = raise (InvalidDataException message)

    let private property (entity: IReadOnlyDictionary<string, string>) key =
        match entity.TryGetValue key with
        | true, value -> value
        | _ -> null

    let private familyAndColor (sourceType: string) =
        let matched = Regex.Match(sourceType, @"^(vehicle_80s_(?:sedan1|hatch1|wagon1)_)([a-z0-9]+)$",
                                  RegexOptions.CultureInvariant)
        if not matched.Success then
            invalid $"Unsupported COD4 destructible vehicle type '{sourceType}'."
        matched.Groups[1].Value, matched.Groups[2].Value

    /// Only original script-model vehicles with a matching intact XModel use
    /// the recovered sedan, hatchback, and wagon recipes. Unknown destructibles fail closed.
    let SourceType(entity: IReadOnlyDictionary<string, string>) : string =
        if isNull entity then nullArg (nameof entity)
        if not (String.Equals(property entity "targetname", "destructible", StringComparison.OrdinalIgnoreCase)) then
            null
        else
            let sourceType = property entity "destructible_type"
            if String.IsNullOrEmpty sourceType then
                invalid "COD4 destructible entity has no source type."
            familyAndColor sourceType |> ignore
            if not (String.Equals(property entity "classname", "script_model", StringComparison.OrdinalIgnoreCase)) ||
               not (String.Equals(property entity "model", sourceType + "_destructible_mp", StringComparison.OrdinalIgnoreCase)) then
                invalid $"COD4 destructible vehicle '{sourceType}' has an unexpected classname or intact model."
            sourceType

    let EntityPropertyValue(prefix: string, entity: IReadOnlyDictionary<string, string>,
                            key: string, value: string) : string =
        let sourceType = SourceType entity
        if isNull sourceType then value
        elif String.Equals(key, "targetname", StringComparison.OrdinalIgnoreCase) then "destructible_vehicle"
        elif String.Equals(key, "destructible_type", StringComparison.OrdinalIgnoreCase) then prefix + sourceType
        else value

    let private quote (value: string) =
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""

    let private sourceBody (clean: string) (family: string) =
        let functionName = family.Substring(0, family.Length - 1)
        let pattern = @"(?m)^\s*" + Regex.Escape(functionName) + @"\s*\(\s*color\s*\)\s*\{"
        let matches = Regex.Matches(clean, pattern)
        if matches.Count <> 1 then invalid $"COD4 source recipe '{functionName}' is missing or ambiguous."
        let start = matches[0].Index + matches[0].Length
        let mutable depth = 1
        let mutable cursor = start
        while depth <> 0 && cursor < clean.Length do
            if clean[cursor] = '{' then depth <- depth + 1
            elif clean[cursor] = '}' then depth <- depth - 1
            cursor <- cursor + 1
        if depth <> 0 then invalid $"COD4 source recipe '{functionName}' has an unmatched brace."
        let body = clean.Substring(start, cursor - start - 1)
        if body.Contains('{') || body.Contains('}') then
            invalid $"COD4 source recipe '{functionName}' has unsupported control flow."
        body

    let CompileWithPrefixes(sourceRecipeText: string, prefix: string, modelPrefix: string,
                            effectPrefix: string, soundPrefix: string,
                            sourceTypes: IReadOnlyCollection<string>) : struct (string * string[] * string[] * string[]) =
        if isNull sourceTypes then nullArg (nameof sourceTypes)
        if sourceTypes.Count = 0 then struct ("", [||], [||], [||])
        else
            if String.IsNullOrEmpty sourceRecipeText then invalid "COD4 vehicle recipe source is absent."
            if String.IsNullOrEmpty prefix then invalid "Converted vehicle asset prefix is absent."
            let clean = Regex.Replace(sourceRecipeText, @"(?s)/\*.*?\*/|//[^\r\n]*", "")
            let models = HashSet<string>(StringComparer.OrdinalIgnoreCase)
            let effects = HashSet<string>(StringComparer.OrdinalIgnoreCase)
            let sounds = HashSet<string>(StringComparer.OrdinalIgnoreCase)
            let script = StringBuilder()
            script.Append("#include common_scripts\\_destructible;\r\n") |> ignore
            script.Append("#using_animtree( \"destructibles\" );\r\n\r\n") |> ignore
            script.Append("main()\r\n{\r\n") |> ignore
            script.Append("\tvehicles = GetEntArray( \"destructible_vehicle\", \"targetname\" );\r\n") |> ignore
            script.Append("\tforeach ( vehicle in vehicles )\r\n\t{\r\n") |> ignore
            script.Append("\t\tif ( common_scripts\\_destructible_types::getInfoIndex( vehicle.destructible_type ) >= 0 )\r\n") |> ignore
            script.Append("\t\t\tcontinue;\r\n") |> ignore
            script.Append("\t\tswitch ( vehicle.destructible_type )\r\n\t\t{\r\n") |> ignore
            let types = sourceTypes |> Seq.distinct |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b)) |> Seq.toArray
            for sourceType in types do
                familyAndColor sourceType |> ignore
                script.Append("\t\t\tcase ").Append(quote (prefix + sourceType))
                    .Append(": vehicle register_").Append(sourceType).Append("(); break;\r\n") |> ignore
            script.Append("\t\t\tdefault: AssertMsg( \"Unsupported converted destructible vehicle\" ); break;\r\n") |> ignore
            script.Append("\t\t}\r\n\t}\r\n}\r\n") |> ignore

            for sourceType in types do
                let family, color = familyAndColor sourceType
                let body = sourceBody clean family
                let mutable tag = ""
                let stringValue (expression: string) =
                    expression.Split('+')
                    |> Array.map (fun part ->
                        let token = part.Trim()
                        if token = "color" then color
                        elif token = "tag" then
                            if tag = "" then invalid $"COD4 vehicle '{sourceType}' uses tag before assignment."
                            tag
                        elif token.Length >= 2 && token[0] = '"' && token[token.Length - 1] = '"' then
                            token.Substring(1, token.Length - 2)
                        else invalid $"COD4 vehicle '{sourceType}' has unsupported expression '{expression}'.")
                    |> String.concat ""
                let tokenValue (expression: string) =
                    let token = expression.Trim()
                    if token = "undefined" || token = "true" || token = "false" ||
                       token = "#animtree" || token.StartsWith("%", StringComparison.Ordinal) ||
                       Regex.IsMatch(token, @"^-?(?:\d+\.?\d*|\.\d+)$") then token
                    else quote (stringValue token)
                let asset (kind: string) (expression: string) =
                    let name = stringValue expression
                    if String.IsNullOrEmpty name then invalid $"COD4 vehicle '{sourceType}' has empty {kind} name."
                    let resolvedName =
                        if kind = "model" && modelPrefix = "" then name.ToLowerInvariant()
                        else name
                    match kind with
                    | "model" -> models.Add resolvedName |> ignore
                    | "fx" -> effects.Add name |> ignore
                    | "sound" -> sounds.Add name |> ignore
                    | _ -> invalid $"Unknown vehicle dependency kind '{kind}'."
                    let assetPrefix =
                        match kind with
                        | "model" -> modelPrefix
                        | "fx" -> effectPrefix
                        | "sound" -> soundPrefix
                        | _ -> invalid $"Unknown vehicle dependency kind '{kind}'."
                    quote (assetPrefix + resolvedName)
                script.Append("\r\nregister_").Append(sourceType).Append("()\r\n{\r\n") |> ignore
                let statements = body.Split(';') |> Array.map (fun item -> item.Trim()) |> Array.filter ((<>) "")
                if statements.Length = 0 then invalid $"COD4 vehicle '{sourceType}' has no recipe actions."
                for statement in statements do
                    let assignment = Regex.Match(statement, @"^tag\s*=\s*(.+)$", RegexOptions.Singleline)
                    if assignment.Success then
                        tag <- stringValue assignment.Groups[1].Value
                    else
                        let call = Regex.Match(statement, @"^(destructible_[a-z]+)\s*\((.*)\)$", RegexOptions.Singleline)
                        if not call.Success then invalid $"COD4 vehicle '{sourceType}' has unsupported statement '{statement}'."
                        let helper = call.Groups[1].Value
                        let raw = call.Groups[2].Value.Trim()
                        let args = if raw = "" then [||] else raw.Split(',') |> Array.map (fun arg -> arg.Trim())
                        let requireCount low high =
                            if args.Length < low || args.Length > high then
                                invalid $"COD4 vehicle '{sourceType}' has unexpected {helper} argument count {args.Length}."
                        match helper with
                        | "destructible_create" ->
                            requireCount 5 5
                            if stringValue args[0] <> sourceType then invalid $"COD4 vehicle '{sourceType}' has a mismatched create type."
                        | "destructible_part" -> requireCount 2 8
                        | "destructible_state" -> requireCount 1 6
                        | "destructible_fx" -> requireCount 2 3
                        | "destructible_loopfx" -> requireCount 3 3
                        | "destructible_sound" -> requireCount 1 2
                        | "destructible_loopsound" -> requireCount 1 1
                        | "destructible_healthdrain" -> requireCount 2 2
                        | "destructible_explode" -> requireCount 5 5
                        | "destructible_anim" -> requireCount 3 3
                        | "destructible_physics" -> requireCount 0 0
                        | _ -> invalid $"COD4 vehicle '{sourceType}' uses unsupported helper '{helper}'."
                        let mutable mapped = args |> Array.map tokenValue
                        match helper with
                        | "destructible_create" ->
                            mapped <- [| quote (prefix + sourceType); quote "tag_body"; mapped[1]; mapped[2]; mapped[3]; mapped[4] |]
                        | "destructible_part" | "destructible_state" ->
                            if mapped.Length > 1 && args[1] <> "undefined" then mapped[1] <- asset "model" args[1]
                            if mapped.Length > 3 then mapped[3] <- "undefined"
                        | "destructible_fx" | "destructible_loopfx" -> mapped[1] <- asset "fx" args[1]
                        | "destructible_sound" | "destructible_loopsound" -> mapped[0] <- asset "sound" args[0]
                        | "destructible_explode" -> mapped <- Array.insertAt 3 mapped[2] mapped
                        | "destructible_healthdrain" -> mapped <- Array.append mapped [| "undefined" |]
                        | _ -> ()
                        script.Append("\t").Append(helper).Append("( ")
                            .Append(String.Join(", ", mapped)).Append(" );\r\n") |> ignore
                script.Append("}\r\n") |> ignore
            let ordered (items: HashSet<string>) = items |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b)) |> Seq.toArray
            struct (script.ToString(), ordered models, ordered effects, ordered sounds)

    let Compile(sourceRecipeText: string, prefix: string,
                sourceTypes: IReadOnlyCollection<string>) : struct (string * string[] * string[] * string[]) =
        CompileWithPrefixes(sourceRecipeText, prefix, prefix, prefix, prefix, sourceTypes)
