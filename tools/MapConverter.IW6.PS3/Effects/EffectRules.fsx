open System
open System.Text.Json
open System.Text.Json.Nodes

let integer (field: string) (node: JsonNode) = node[field].GetValue<int>()
let property (field: string) (node: JsonNode) = if isNull node then null else node[field]
let text field node =
    match property field node with
    | :? JsonValue as value when value.GetValueKind() = JsonValueKind.String -> value.GetValue<string>()
    | _ -> null

let counts effect =
    [| integer "elemDefCountLooping" effect
       integer "elemDefCountOneShot" effect
       integer "elemDefCountEmission" effect |]

let visualName elemType kind (element: JsonNode) =
    if integer "elemType" element = elemType && integer "visualCount" element = 1 then
        let visual = element |> property "visuals" |> property "visual"
        if text "kind" visual = kind then text "name" visual else null
    else null

let bindModel (carrier: JsonNode) (model: JsonNode) =
    let movingModel = carrier.DeepClone()
    for field in [ "elemType"; "visualCount"; "visuals"; "atlas"; "visStateIntervalCount"; "visSamples"
                   "spawnAngles"; "angularVelocity"; "initialRotation" ] do
        movingModel[field] <- model[field].DeepClone()
    movingModel

let inventory = JsonNode.Parse(Console.In.ReadToEnd())

let widowChanges (descent: JsonNode) (thread: JsonNode) =
    if isNull descent || isNull thread || counts descent <> [| 1; 0; 1 |] || counts thread <> [| 1; 1; 0 |] then [||]
    else
        let carrier = descent["elemDefs"][0]
        let emission = descent["elemDefs"][1]
        let model = thread["elemDefs"][0]
        let streak = thread["elemDefs"][1]
        let threadName = text "name" thread
        let modelName = visualName 7 "model" model

        if not (visualName 0 "material" carrier = "gfx_test01" &&
                not (isNull modelName) && modelName.StartsWith("iw6/", StringComparison.Ordinal) &&
                modelName.EndsWith("/black_widow_descend", StringComparison.Ordinal) &&
                visualName 2 "material" streak = "vfx_lensflare_streak_1" &&
                integer "flags" carrier = 0x01008040 && integer "flags" model = 0x80 &&
                not (isNull threadName) && text "name" carrier["effectEmitted"] = threadName &&
                JsonNode.DeepEquals(emission, streak) &&
                JsonNode.DeepEquals(carrier["lifeSpanMsec"], model["lifeSpanMsec"])) then [||]
        else
            // Give each moving carrier one spider. A distance emission starts
            // the full referenced effect in IW4, so its child carries only the thread.
            let streakName = threadName + "_streak"
            let movingModel = bindModel carrier model
            movingModel["effectEmitted"]["name"] <- JsonValue.Create(streakName)

            [| {| name = text "name" descent; elements = [| movingModel; emission |]; counts = counts descent
                  flags = integer "flags" descent; life = integer "msecLoopingLife" descent |}
               {| name = streakName; elements = [| streak |]; counts = [| 0; 1; 0 |]
                  flags = integer "flags" thread; life = 0 |} |]

let owlChanges (owl: JsonNode) =
    if isNull owl || counts owl.["flap"] <> [| 8; 0; 0 |] || counts owl.["sit"] <> [| 2; 0; 0 |] then [||]
    else
        let perch = owl.["sit"].["elemDefs"].[0]
        let poses = (owl.["flap"].["elemDefs"]).AsArray()
        // Use the wings-up frame at launch and the wings-down frame after the glide.
        let stages =
            [| owl.["takeoff"], poses.[0], owl.["flap"]
               owl.["glide"], owl.["glideModel"].["elemDefs"].[0], owl.["glideModel"]
               owl.["fly"], poses.[1], owl.["flap"]
               owl.["shrink"], owl.["shrinkModel"].["elemDefs"].[0], owl.["shrinkModel"] |]
        let carriersMatch =
            stages |> Array.forall (fun (effect, pose, child) ->
                let carrier = effect.["elemDefs"].[0]
                visualName 0 "material" carrier = "gfx_test01" &&
                text "name" carrier.["effectEmitted"] = text "name" child &&
                not (isNull (visualName 7 "model" pose)))

        if not carriersMatch then [||]
        else
            stages |> Array.mapi (fun index (effect, pose, _) ->
                // IW4 distance emission spawns separate effects along the path.
                // The IW6 owl carriers also have a source-only flag and trailing field.
                let model = bindModel effect.["elemDefs"].[0] pose
                model.["effectEmitted"].["name"] <- null
                // The perched body's axis puts the flight model's forward
                // direction along the carrier's local flight path.
                model.["spawnAngles"] <- perch.["spawnAngles"].DeepClone()
                // The exit has no following phase, so let the pose set its fade duration.
                if index = stages.Length - 1 then
                    model.["lifeSpanMsec"] <- pose.["lifeSpanMsec"].DeepClone()
                {| name = text "name" effect; elements = [| model |]; counts = [| 0; 1; 0 |]
                   flags = integer "flags" effect; life = 0 |})

let changes = Array.append (widowChanges inventory.["descent"] inventory.["thread"]) (owlChanges inventory.["owl"])
JsonSerializer.Serialize(changes) |> Console.WriteLine
